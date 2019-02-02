using System.Collections.Generic;
using Resgrid.Model;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Web.Areas.User.Models.Messages
{
	public class MessagesOutboxModel : BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public List<Message> Messages { get; set; } 
	}
}