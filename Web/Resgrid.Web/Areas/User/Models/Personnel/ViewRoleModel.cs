using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Personnel
{
	public class ViewRoleModel : BaseUserModel
	{
		public PersonnelRole Role { get; set; }
		public List<IdentityUser> UsersInRole { get; set; }
	}
}
