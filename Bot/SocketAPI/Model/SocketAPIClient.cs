namespace SocketAPI
{
	/// <summary>
	///	A wrapper around `TcpClient` adding a unique identifier to each client, heartbeat management and disposal mechanisms.
	/// </summary>
	public sealed class SocketAPIClient
	{
		/// <summary>
		///	The unique ID associated with the `TcpClient`.
		/// </summary>
		public string uuid = System.Guid.NewGuid().ToString();

		/// <summary>
		///	The `TcpClient` instance.
		/// </summary>
		public System.Net.Sockets.TcpClient tcpClient;

		/// <summary>
		///	The heartbeat timer instance.
		/// </summary>
		private System.Timers.Timer heartbeatTimer = new();

		/// <summary>
		///	The heartbeat timeout instance. Used to race against client's heartbeat response and determine a flatline.
		/// </summary>
		private System.Timers.Timer heartbeatTimeout = new();

		/// <summary>
		///	Whether the client responded to the server heartbeat or not.
		/// Note: this is initially set to true to allow the first heartbeat to be correctly handled.
		/// </summary>
		private bool respondedToHeartbeat = true;

		/// <summary>
		///	The UUID of the last emitted heartbeat.
		/// </summary>
		public string lastEmittedHeartbeatUUID = "";

		/// <summary>
		///	The heartbeat interval in milliseconds.
		/// </summary>
		public int heartbeatInterval = 8000;

		/// <summary>
		///	The number of non-received responses that trigger a flatline.
		/// </summary>
		public int maxHeartbeatRetries = 3;

		public SocketAPIClient(System.Net.Sockets.TcpClient tcpClient)
		{
			this.tcpClient = tcpClient;
			this.heartbeatTimer.Interval = this.heartbeatInterval;
			this.heartbeatTimer.Elapsed += this.EmitHeartbeat;
		}

		/// <summary>
		///	Signals that the client has responded to the heartbeat.
		/// </summary>
		public void SignalHeartbeatResponse()
		{
			this.respondedToHeartbeat = true;
			this.heartbeatTimeout.Stop();
		}

		/// <summary>
		///	Starts emitting heartbeats to the client.
		/// </summary>
		public void StartEmittingHeartbeatsToClient()
		{
			this.heartbeatTimer.Start();
		}

		/// <summary>
		///	Handler responsible for responding to the heartbeat interval event.
		/// </summary>
		private void EmitHeartbeat(object source, System.Timers.ElapsedEventArgs e)
		{
			_ = SocketAPIServer.shared.SendHeartbeat(this);

			Logger.LogDebug($"Emitted heartbeat with UUID {this.lastEmittedHeartbeatUUID} to client {this.uuid}.");

			if (!this.respondedToHeartbeat)
				return;
			
			// Previous heartbeat received a response => emit a new one.
			this.respondedToHeartbeat = false;

			this.heartbeatTimeout = new();
			this.heartbeatTimeout.Interval = this.heartbeatInterval * this.maxHeartbeatRetries;
			this.heartbeatTimeout.AutoReset = false;
			this.heartbeatTimeout.Elapsed += (object source, System.Timers.ElapsedEventArgs e) => {
				if (this.respondedToHeartbeat)
					return;
				
				Logger.LogError($"Heartbeat flatlined after {this.maxHeartbeatRetries} retries ({this.heartbeatInterval * this.maxHeartbeatRetries}ms) for client {this.uuid}. Destroying client.");
				this.Destroy();
			};
			this.heartbeatTimeout.Start();
		}

		/// <summary>
		///	Disposes the `TcpClient` instance, requests the underlying TCP connection to be closed and disposes the heartbeat timer.
		/// </summary>
		public void Destroy()
		{
			this.heartbeatTimer.Stop();
			this.heartbeatTimeout.Stop();
			this.tcpClient.Close();
		}
	}
}