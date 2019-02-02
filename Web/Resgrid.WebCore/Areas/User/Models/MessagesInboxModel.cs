using System.Collections.Generic;
using Microsoft.AspNet.Identity.EntityFramework6;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models
{
	public class MessagesInboxModel: BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public List<Message> Messages { get; set; }
		public int UnreadMessages { get; set; }
	}
}