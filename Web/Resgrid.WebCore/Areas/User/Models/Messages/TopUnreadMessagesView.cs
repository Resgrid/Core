using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Messages
{
	public class TopUnreadMessagesView
	{
		public List<Message> UnreadMessages { get; set; }
		public Department Department { get; set; }
	}
}