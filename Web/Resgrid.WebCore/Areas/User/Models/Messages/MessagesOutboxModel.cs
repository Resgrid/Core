using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Messages
{
	public class MessagesOutboxModel : BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public List<Message> Messages { get; set; } 
	}
}
