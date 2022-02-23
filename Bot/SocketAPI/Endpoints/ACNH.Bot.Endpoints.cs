using System.Threading.Tasks;
using SysBot.ACNHOrders;

namespace SocketAPI 
{
	[SocketAPIController]
	class ACNHBotEndpoints 
	{
		[SocketAPIEndpoint]
		public async static Task<object?> NumberOfVisitors(string args)
		{
			return await Globals.Bot.VisitorList.FetchVisitors(new());
		}
	}
}