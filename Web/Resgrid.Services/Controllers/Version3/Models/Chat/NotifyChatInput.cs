using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Chat
{
	public class NotifyChatInput
	{
		public string Id { get; set; }
		public string GroupName { get; set; }
		public string SendingUserId { get; set; }
		public List<string> RecipientUserIds { get; set; }
		public string Message { get; set; }
		public int Type { get; set; }
	}
}
