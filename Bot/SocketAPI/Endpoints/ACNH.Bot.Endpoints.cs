using SysBot.ACNHOrders;

namespace SocketAPI 
{
	[SocketAPIController]
	class ACNHBotEndpoints 
	{
		[SocketAPIEndpoint]
		public static object? Visitors(string args)
		{
			if (Globals.Bot == null)
				throw new System.Exception("The Globals.Bot instance is null.");

			return Globals.Bot.VisitorList.UniqueVisitors;
		}

		[SocketAPIEndpoint]
		public static object? RequestRestart(string args)
		{
			if (Globals.Bot == null)
				throw new System.Exception("The Globals.Bot instance is null.");

			Globals.Bot.RestoreRestartRequested = true;

			return null;
		}
	}
}