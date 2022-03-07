using System;
using System.Net;
using System.Collections.Generic;
using System.Threading;

namespace SocketAPI
{
	class DedicatedConnection
	{
		/// <summary>
		/// A communication channel used solely by the `SocketAPIServer`.
		/// </summary>
		private SysBot.Base.SwitchSocketAsync? dedicatedConnection;

		/// <summary>
		/// Configuration used for local development.
		/// </summary>
		private SocketAPIConsoleConnectionConfig? devConfigs;

		/// <summary>
		/// You can instantiate this class if you so wish to create and maintain your own dedicated connection reference.
		/// </summary>
		public DedicatedConnection() {}

		private static DedicatedConnection? _instance;

		/// <summary>
		/// Connection singleton instance.
		/// </summary>
		public static DedicatedConnection connection 
		{
			get
			{
				if (DedicatedConnection._instance == null)
					DedicatedConnection._instance = new();
				return DedicatedConnection._instance;
			}
		}

		/// <summary>
		/// Connects to the console.
		/// </summary>
		public void Start(SocketAPIConsoleConnectionConfig config)
		{
			this.dedicatedConnection = new(this.devConfigs ?? config);
			this.dedicatedConnection.Connect();
		}

		/// <summary>
		/// Connects to the console.
		/// </summary>
		public void Start(string ip, int port)
		{
			SocketAPIConsoleConnectionConfig config = new();
			config.IP = ip;
			config.Port = port;

			this.Start(config);
		}

		/// <summary>
		/// Returns the `SwitchSocketAsync` instance.
		/// </summary>
		public SysBot.Base.SwitchSocketAsync? GetSocket()
		{
			return this.dedicatedConnection;
		}

		/// <summary>
		/// Loads development configuration from local .env file (case-insensitive).
		/// </summary>
		public bool LoadDevConfigs()
		{
			Dictionary<string, object>? envEntries = EnvParser.ParseFile(".env");

			if (envEntries == null)
				return false;
			
			if (envEntries.Count < 2)
				return false;

			if (envEntries["ip"] == null || envEntries["port"] == null)
				return false;

			this.devConfigs = new();
			this.devConfigs.IP = (string)envEntries["ip"];
			this.devConfigs.Port = System.Int32.Parse((string)envEntries["port"]);

			if (!IPAddress.TryParse(this.devConfigs.IP, out _))
				this.devConfigs.IP = Dns.GetHostEntry(this.devConfigs.IP).AddressList[0]?.MapToIPv4().ToString() ?? IPAddress.Loopback.MapToIPv4().ToString();

			return true;
		}
	}
}