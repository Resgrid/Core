using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Personnel
{
	public class EditRoleModel : BaseUserModel
	{
		public PersonnelRole Role { get; set; }
		public List<IdentityUser> Users { get; set; }
	}
}
