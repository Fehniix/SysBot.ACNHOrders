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
		public System.Net.Sockets.TcpClient client;

		/// <summary>
		///	The heartbeat timer instance.
		/// </summary>
		private System.Timers.Timer heartbeatTimer = new();

		/// <summary>
		///	Whether the client responded to the server heartbeat or not.
		/// </summary>
		private bool respondedToHeartbeat = false;

		public SocketAPIClient(System.Net.Sockets.TcpClient client)
		{
			this.client = client;
			this.heartbeatTimer.Interval = 2000;
			this.heartbeatTimer.Elapsed += this.EmitHeartbeat;
		}

		/// <summary>
		///	Starts emitting heartbeats to the client.
		/// </summary>
		public void StartHeartbeat()
		{
			this.heartbeatTimer.Enabled = true;
		}

		/// <summary>
		///	Handler responsible for responding to the heartbeat interval event.
		/// </summary>
		private void EmitHeartbeat(object source, System.Timers.ElapsedEventArgs e)
		{
			_ = SocketAPIServer.shared.SendHeartbeat(this.client);
			
			System.Timers.Timer heartbeatTimeout = new();
			heartbeatTimeout.Interval = 2000 * 3;
			heartbeatTimeout.AutoReset = false;
			heartbeatTimeout.Elapsed += (object source, System.Timers.ElapsedEventArgs e) => {
				if (this.respondedToHeartbeat)
					return;
				
				Logger.LogError($"Heartbeat flatlined for client {this.uuid}. Destroying client.");
				this.Destroy();
			};
			heartbeatTimeout.Start();

			Logger.LogInfo($"Emitted heartbeat to client {this.uuid}.");
		}

		/// <summary>
		///	Disposes the `TcpClient` instance, requests the underlying TCP connection to be closed and disposes the heartbeat timer.
		/// </summary>
		public void Destroy()
		{
			this.heartbeatTimer.Stop();
			this.client.Close();
		}
	}
}