using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Chat
{
	public class ResponderChatResult
	{
		public string UserId { get; set; }

		public List<ResponderChatGroupInfo> Groups { get; set; }

		public ResponderChatResult()
		{
			Groups = new List<ResponderChatGroupInfo>();
		}
	}

	public class ResponderChatGroupInfo
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public int Type { get; set; } // Department 1, Station Group 2, Orginizational Group 3
		public int Count { get; set; }
	}
}
