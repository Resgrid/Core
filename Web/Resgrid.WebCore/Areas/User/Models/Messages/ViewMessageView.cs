using System.Collections.Generic;
using Resgrid.Model;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Web.Areas.User.Models.Messages
{
	public class ViewMessageView: BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public Message Message { get; set; } 
		public int UnreadMessages { get; set; }
		public List<UserGroupRole> UserGroupsAndRoles { get; set; }
	}
}