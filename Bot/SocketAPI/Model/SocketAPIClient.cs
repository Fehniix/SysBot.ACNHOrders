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
		///	The UUID of the last emitted heartbeat.
		/// </summary>
		public string lastEmittedHeartbeatUUID = "";

		/// <summary>
		///	Whether the client responded to the server heartbeat or not.
		/// </summary>
		private bool respondedToHeartbeat = false;

		public SocketAPIClient(System.Net.Sockets.TcpClient tcpClient)
		{
			this.tcpClient = tcpClient;
			this.heartbeatTimer.Interval = 2000;
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
			this.lastEmittedHeartbeatUUID = System.Guid.NewGuid().ToString();
			_ = SocketAPIServer.shared.SendHeartbeat(this.tcpClient, this.lastEmittedHeartbeatUUID);
			
			this.heartbeatTimeout = new();
			this.heartbeatTimeout.Interval = 2000 * 3;
			this.heartbeatTimeout.AutoReset = false;
			this.heartbeatTimeout.Elapsed += (object source, System.Timers.ElapsedEventArgs e) => {
				if (this.respondedToHeartbeat)
					return;
				
				Logger.LogError($"Heartbeat flatlined for client {this.uuid}. Destroying client.");
				this.Destroy();
			};
			this.heartbeatTimeout.Start();

			Logger.LogInfo($"Emitted heartbeat to client {this.uuid}.");
		}

		/// <summary>
		///	Disposes the `TcpClient` instance, requests the underlying TCP connection to be closed and disposes the heartbeat timer.
		/// </summary>
		public void Destroy()
		{
			this.heartbeatTimer.Stop();
			this.tcpClient.Close();
		}
	}
}