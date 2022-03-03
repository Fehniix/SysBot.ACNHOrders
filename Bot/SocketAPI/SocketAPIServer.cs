using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using SysBot.Base;

namespace SocketAPI {
	/// <summary>
	/// Acts as an API server, accepting requests and replying over TCP/IP.
	/// </summary>
	public sealed class SocketAPIServer
	{
		/// <summary>
		/// Useful for TcpListener's graceful shutdown.
		/// </summary>
		private CancellationTokenSource tcpListenerCancellationSource = new();

		/// <summary>
		/// Provides an alias to the cancellation token.
		/// </summary>
		private CancellationToken tcpListenerCancellationToken 
		{ 
			get { return tcpListenerCancellationSource.Token; }
			set { }
		}

		/// <summary>
		/// The TCP listener used to listen for incoming connections.
		/// </summary>
		private TcpListener? listener;

		/// <summary>
		/// Keeps a list of callable endpoints.
		/// </summary>
		private Dictionary<string, Delegate> apiEndpoints = new();

		/// <summary>
		/// Keeps the list of connected clients to broadcast events to.
		/// </summary>
		private ConcurrentDictionary<string, SocketAPIClient> clients = new();

		/// <summary>
		/// Keeps a list of heartbeats sent to client from which a response is expected.
		/// </summary>
		private Dictionary<string, bool> heartbeats = new();

		/// <summary>
		/// The server configuration file.
		/// </summary>
		private SocketAPIServerConfig? config;

		private SocketAPIServer() {}

		private static SocketAPIServer? _shared;

		/// <summary>
		///	The singleton instance of the `SocketAPIServer`.
		/// </summary>
		public static SocketAPIServer shared
		{
			get 
			{  
				if (_shared == null)
					_shared = new();
				return _shared;
			}
			private set { }
		}

		/// <summary>
		/// Starts listening for incoming connections on the configured port.
		/// </summary>
		public async Task Start(SocketAPIServerConfig config)
		{
			this.config = config;

			if (!config.Enabled)
				return;

			if (!config.LogsEnabled)
				Logger.DisableLogs();

			if (config.OutputVerboseDebugInfo)
				Logger.EnableVerboseDebugLogs();

			int eps = RegisterEndpoints();
			Logger.LogInfo($"n. of registered endpoints: {eps}");

			listener = new(IPAddress.Any, config.Port);

			try 
			{
				DedicatedConnection.connection.LoadDevConfigs();
				DedicatedConnection.connection.Start("", 0); // Dev configs override configs provided to .Start().

				listener.Start();
			}
			catch(SocketException ex)
			{
				Logger.LogError($"Socket API server failed to start: {ex.Message}");
				return;
			}

			Logger.LogInfo($"Socket API server listening on port {config.Port}.");

			tcpListenerCancellationToken.ThrowIfCancellationRequested();
			tcpListenerCancellationToken.Register(listener.Stop);

			while(!tcpListenerCancellationToken.IsCancellationRequested)
			{
				try
				{
					TcpClient tcpClient 	= await listener.AcceptTcpClientAsync();
					SocketAPIClient client 	= new(tcpClient, this.config);
					clients[client.uuid] 	= client;
					IPEndPoint? clientEP 	= tcpClient.Client.RemoteEndPoint as IPEndPoint;

					Logger.LogDebug($"A client connected! IP: {clientEP?.Address}, on port: {clientEP?.Port}");

					HandleTcpClient(client);
				}
				catch(OperationCanceledException) when (tcpListenerCancellationToken.IsCancellationRequested)
				{
					Logger.LogInfo("The socket API server was closed.", true);
					foreach(string key in this.clients.Keys)
						this.clients.Remove(key, out _);
				}
				catch(Exception ex)
				{
					Logger.LogError($"An error occured on the soket API server: {ex.Message}", true);
				}
			}
		}

		/// <summary>
		/// Given a connected TcpClient, this callback handles communication & graceful shutdown.
		/// </summary>
		private async void HandleTcpClient(SocketAPIClient apiClient)
		{
			TcpClient tcpClient 	= apiClient.tcpClient;
			NetworkStream stream 	= tcpClient.GetStream();

			apiClient.StartEmittingHeartbeatsToClient();

			while (true)
			{
				byte[] buffer = new byte[tcpClient.ReceiveBufferSize];
				int bytesRead = 0;
				
				try
				{
					bytesRead = await stream.ReadAsync(buffer, 0, tcpClient.ReceiveBufferSize, tcpListenerCancellationToken);
				}
				catch(Exception ex)
				{
					Logger.LogDebug($"There was an error while reading client {apiClient.uuid} stream: {ex.Message}\nClosing connection.");
					apiClient.Destroy();
				}

				if (bytesRead == 0)
				{
					Logger.LogDebug("A remote client closed the connection.");
					break;
				}

				string rawMessage = Encoding.UTF8.GetString(buffer);
				rawMessage = Regex.Replace(rawMessage, @"\r\n?|\n|\0", "");

				if (rawMessage.StartsWith("hb"))
				{
					string? heartbeatUUID = rawMessage.Split(" ")[1];

					if (heartbeatUUID == null)
					{
						Logger.LogDebug($"Malformed heartbeat: {rawMessage}. Closed connection to client.");
						apiClient.Destroy();
						break;
					}

					if (heartbeatUUID != apiClient.lastEmittedHeartbeatUUID)
					{
						Logger.LogDebug($"Received wrong heartbeat UUID from client {apiClient.uuid}. Expected {apiClient.lastEmittedHeartbeatUUID}, got {heartbeatUUID}. Closing connection with client.");
						apiClient.Destroy();
						break;
					}

					apiClient.SignalHeartbeatResponse();
					
					if (!(config?.NoDebugHeartbeatLogs ?? false))
						Logger.LogDebug($"Client {apiClient.uuid} responded to heartbeat ({heartbeatUUID}).");

					continue;
				}

				SocketAPIRequest? request = SocketAPIProtocol.DecodeMessage(rawMessage);

				if (request == null)
				{
					await this.SendResponse(apiClient, SocketAPIMessage.FromError("There was an error while JSON-parsing the provided request."));
					continue;
				}

				SocketAPIMessage? message = await this.InvokeEndpoint(request!.endpoint!, request?.args);

				if (message == null)
					message = SocketAPIMessage.FromError("The supplied endpoint was not found.");

				message.id = request!.id;

				await this.SendResponse(apiClient, message);
			}
		}

		/// <summary>
		/// Sends to the supplied client the given message of type `Response`.
		/// </summary>
		public async Task SendResponse(SocketAPIClient apiClient, SocketAPIMessage message)
		{
			message.type = SocketAPIMessageType.Response;
			await this.SendMessage(apiClient, message);
		}

		/// <summary>
		/// Sends to the supplied client the given message of type `Event`.
		/// </summary>
		public async Task SendEvent(SocketAPIClient apiClient, SocketAPIMessage message)
		{
			message.type = SocketAPIMessageType.Event;
			await this.SendMessage(apiClient, message);
		}

		/// <summary>
		/// Given an event name and event parameters, this method defines a standardized `SocketAPIMessage` sends it 
		/// to all currently connected clients in parallel encoded as an event.
		/// </summary>
		public async void BroadcastEvent(string eventName, object? args)
		{
			foreach(SocketAPIClient apiClient in this.clients.Values)
			{
				if (apiClient.tcpClient.Connected)
					await SendEvent(apiClient, new(
						new {
							eventName = eventName,
							eventArgs = args
						}, null
					));
			}
		}

		/// <summary>
		/// Encodes a message and sends it to a client.
		/// </summary>
		private async Task SendMessage(SocketAPIClient toAPIClient, SocketAPIMessage message)
		{
			if (this.config?.Enabled == false)
				return;

			byte[] wBuff = Encoding.UTF8.GetBytes(SocketAPIProtocol.EncodeMessage(message)!);
			try
			{
				await toAPIClient.tcpClient.GetStream().WriteAsync(wBuff, 0, wBuff.Length, tcpListenerCancellationToken);
			}
			catch(Exception ex)
			{
				Logger.LogError($"There was an error while sending a message to a client: {ex.Message}");
				toAPIClient.Destroy();
			}
		}

		/// <summary>
		/// Sends an heartbeat to the supplied client.
		/// </summary>
		public async Task SendHeartbeat(SocketAPIClient toAPIClient)
		{
			toAPIClient.lastEmittedHeartbeatUUID = System.Guid.NewGuid().ToString();

			byte[] wBuff = Encoding.UTF8.GetBytes($"hb {toAPIClient.lastEmittedHeartbeatUUID}");
			try 
			{
				await toAPIClient.tcpClient.GetStream().WriteAsync(wBuff, 0, wBuff.Length, tcpListenerCancellationToken);
			}
			catch(Exception ex)
			{
				Logger.LogError($"There was an error while sending an heartbeat to the client: {ex.Message}");
				toAPIClient.Destroy();
			}
		}

		/// <summary>
		/// Sends an heartbeat to the supplied client.
		/// </summary>
		public async Task SendHeartbeat(TcpClient toClient)
		{
			byte[] wBuff = Encoding.UTF8.GetBytes($"hb {Guid.NewGuid().ToString()}");
			try 
			{
				await toAPIClient.tcpClient.GetStream().WriteAsync(wBuff, 0, wBuff.Length, tcpListenerCancellationToken);
			}
			catch(Exception ex)
			{
				Logger.LogError($"There was an error while sending a message to a client: {ex.Message}");
				toAPIClient.Destroy();
			}
		}

		/// <summary>
		/// Sends an heartbeat to the supplied client.
		/// </summary>
		public async Task SendHeartbeat(SocketAPIClient toAPIClient)
		{
			toAPIClient.lastEmittedHeartbeatUUID = System.Guid.NewGuid().ToString();

			byte[] wBuff = Encoding.UTF8.GetBytes($"hb {toAPIClient.lastEmittedHeartbeatUUID}");
			try 
			{
				await toAPIClient.tcpClient.GetStream().WriteAsync(wBuff, 0, wBuff.Length, tcpListenerCancellationToken);
			}
			catch(Exception ex)
			{
				Logger.LogError($"There was an error while sending an heartbeat to the client: {ex.Message}");
				toAPIClient.Destroy();
			}
		}

		/// <summary>
		/// Stops the execution of the server.
		/// </summary>
		public void Stop()
		{
			listener?.Server.Close();
			tcpListenerCancellationSource.Cancel();
		}

		/// <summary>
		/// Registers an API endpoint by its name.
		/// </summary>
		/// <param name="name">The name of the endpoint used to invoke the provided handler.</param>
		/// <param name="handler">The handler responsible for generating a response.</param>
		/// <returns></returns>
		private bool RegisterEndpoint(string name, Func<string, object?> handler)
		{
			if (apiEndpoints.ContainsKey(name))
				return false;

			apiEndpoints.Add(name, handler);

			return true;
		}

		/// <summary>
		/// Loads all the classes marked as `SocketAPIController` and respective `SocketAPIEndpoint`-marked methods.
		/// </summary>
		/// <remarks>
		/// The SocketAPIEndpoint marked methods 
		/// </remarks>
		/// <returns>The number of methods successfully registered.</returns>
		private int RegisterEndpoints()
		{
			var endpoints = AppDomain.CurrentDomain.GetAssemblies()
								.Where(a => a.FullName?.Contains("SysBot.ACNHOrders") ?? false)
								.SelectMany(a => a.GetTypes())
								.Where(t => t.IsClass && t.GetCustomAttributes(typeof(SocketAPIController), true).Count() > 0)
								.SelectMany(c => c.GetMethods())
								.Where(m => m.GetCustomAttributes(typeof(SocketAPIEndpoint), true).Count() > 0)
								.Where(m => m.GetParameters().Count() == 1 &&
											m.IsStatic &&
											m.GetParameters()[0].ParameterType == typeof(string) &&
											(m.ReturnType == typeof(object) || m.ReturnType == typeof(Task<object>)));

			foreach (var endpoint in endpoints)
				RegisterEndpoint(endpoint.Name, (Func<string, object?>)endpoint.CreateDelegate(typeof(Func<string, object?>)));

			return endpoints.Count();
		}

		/// <summary>
		/// Invokes the registered endpoint via endpoint name, providing it with JSON-encoded arguments.
		/// </summary>
		/// <param name="endpointName">The name of the registered endpoint. Case-sensitive!</param>
		/// <param name="jsonArgs">The arguments to provide to the endpoint, encoded in JSON format.</param>
		/// <returns>A JSON-formatted response. `null` if the endpoint was not found.</returns>
		private async Task<SocketAPIMessage?> InvokeEndpoint(string endpointName, string? jsonArgs)
		{
			if (!apiEndpoints.ContainsKey(endpointName))
				return SocketAPIMessage.FromError("The supplied endpoint was not found.");

			bool isEndpointAsync = apiEndpoints[endpointName]?.Method.ReturnType == typeof(Task<object>);

			try
			{
				var rawResponseInvocationResult = apiEndpoints[endpointName].Method.Invoke(null, new[] { jsonArgs });
				
				if (rawResponseInvocationResult == null)
					return SocketAPIMessage.FromValue(null);

				if (isEndpointAsync)
				{
					object? rawResponse = await (Task<object?>)rawResponseInvocationResult;
					return SocketAPIMessage.FromValue(rawResponse);
				}

				return SocketAPIMessage.FromValue(rawResponseInvocationResult);
			}
			catch(Exception ex)
			{
				string errorMessage = "A generic exception was thrown.";

				if (isEndpointAsync)
					errorMessage = ex.Message;
				else
					errorMessage = ex.InnerException != null ? ex.InnerException!.Message : errorMessage;

				return SocketAPIMessage.FromError(errorMessage);
			}
		}
	}
}