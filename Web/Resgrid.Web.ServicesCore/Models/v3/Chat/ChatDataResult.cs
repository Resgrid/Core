using System.Collections.Generic;
using Resgrid.Web.Services.Controllers.Version3.Models.Groups;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Chat
{
	public class ChatDataResult
	{
		public string Token { get; set; }
		public int Did { get; set; }
		public string Name { get; set; }
		public List<GroupInfoResult> Groups { get; set; }
		public List<ChannelData> Channels { get; set; }
	}
}
