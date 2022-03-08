namespace SocketAPI
{
	/// <summary>
	/// Describes the connection to console configuration parameters.
	/// </summary>
	public class SocketAPIConsoleConnectionConfig: SysBot.Base.IWirelessConnectionConfig {
		private string _ip = "";
		private int port;

		public string IP 
		{
			get { return this._ip; }
			set { this._ip = value; }
		}
		public int Port
		{
			get { return this.port; }
			set { this.port = value; }
		}
	}
}