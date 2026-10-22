using Microsoft.AspNetCore.Http;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Personnel
{
	public class AddRoleModel : BaseUserModel
	{
		public PersonnelRole Role { get; set; }

		/// <summary>Optional map icon for the role's members (read with TypeIconUploads, never bound onto the role).</summary>
		public IFormFile IconFile { get; set; }
	}
}