using System.Threading.Tasks;
using System.Linq;
using SysBot.ACNHOrders;

namespace SocketAPI 
{
	[SocketAPIController]
	class ACNHBotEndpoints 
	{
		[SocketAPIEndpoint]
		public async static Task<object?> NumberOfVisitors(string args)
		{
			return (await Globals.Bot.VisitorList.FetchVisitors(new())).Where(visitor => !string.IsNullOrWhiteSpace(visitor)).Count();
		}
	}
}