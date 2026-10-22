using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Personnel
{
	public class EditRoleModel : BaseUserModel
	{
		public PersonnelRole Role { get; set; }
		public List<IdentityUser> Users { get; set; }

		/// <summary>Optional new map icon for the role's members (read with TypeIconUploads, never bound onto the role).</summary>
		public IFormFile IconFile { get; set; }

		/// <summary>Removes the role's uploaded icon; its members fall back to their next role's icon or the default marker.</summary>
		public bool RemoveCustomIcon { get; set; }
	}
}
