using System.Collections.Generic;
using Resgrid.Model;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Web.Areas.User.Models.Personnel
{
	public class AddToRoleModel : BaseUserModel
	{
		public PersonnelRole Role { get; set; }
		public List<UserToRole> Users { get; set; } 
	}

	public class UserToRole
	{
		public IdentityUser User { get; set; }
		public string Name { get; set; }
	}
}