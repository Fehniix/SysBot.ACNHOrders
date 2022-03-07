using System;

namespace SocketAPI
{
	[Serializable]
	public class SocketAPIServerConfig
	{
		/// <summary>
		/// Whether the socket API server should be enabled or not.
		/// </summary>
		public bool Enabled { get; set; } = false;

		/// <summary>
		/// Whether logs relative to the socket API server should be written out to console.
		/// </summary>
		public bool LogsEnabled { get; set; } = true;

		/// <summary>
		/// Whether verbose debug logs (such as connect/disconnect/heartbeat events) should be written to console.
		/// </summary>
		public bool OutputVerboseDebugInfo { get; set; } = false;

		/// <summary>
		/// Whether to enable or disable the dedicated connection.
		/// </summary>
		public bool EnableDedicatedConnection { get; set; } = false;

		/// <summary>
		/// The network port on which the socket server listens for incoming connections.
		/// </summary>
		public ushort Port { get; set; } = 5201;
	}
}