using SysBot.Base;

namespace SocketAPI
{
	public class Logger
	{
		/// <summary>
		/// Whether logs are enabled or not.
		/// </summary>
		private static bool logsEnabled = true;

		/// <summary>
		/// Whether verbose debug logs should be written to console.
		/// </summary>
		private static bool verboseDebugEnabled = false;
		
		public static void LogInfo(string message, bool ignoreDisabled = false)
		{
			if (!logsEnabled && !ignoreDisabled)
				return;

			LogUtil.LogInfo(message, nameof(SocketAPI));
		}

		public static void LogWarning(string message, bool ignoreDisabled = false)
		{
			if (!logsEnabled && !ignoreDisabled)
				return;

			LogUtil.LogInfo(message, nameof(SocketAPI) + "Warning");
		}

		public static void LogError(string message, bool ignoreDisabled = false)
		{
			if (!logsEnabled && !ignoreDisabled)
				return;

			LogUtil.LogError(message, nameof(SocketAPI));
		}

		public static void LogDebug(string message, bool ignoreDisabled = false)
		{
			if (!verboseDebugEnabled)
				return;
				
			if (!logsEnabled && !ignoreDisabled)
				return;

			LogUtil.LogError(message, nameof(SocketAPI));
		}

		public static void DisableLogs()
		{
			logsEnabled = false;
		}

		public static void EnableVerboseDebugLogs()
		{
			verboseDebugEnabled = true;
		}
	}
}