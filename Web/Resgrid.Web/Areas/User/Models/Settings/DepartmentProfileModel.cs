using System.ComponentModel.DataAnnotations;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Settings
{
	/// <summary>
	/// Department Profile page (RMS plan section 4.10.1). Only the identity fields are bound from the form; the
	/// profile row itself is loaded server-side so Connect-era columns can never be posted.
	/// </summary>
	public class DepartmentProfileModel : BaseUserModel
	{
		public string Message { get; set; }
		public string ErrorMessage { get; set; }

		public Department Department { get; set; }
		public DepartmentProfile Profile { get; set; }
		public DepartmentBranding Branding { get; set; }
		public string PublicMastheadUrl { get; set; }

		[MaxLength(200)]
		public string Name { get; set; }

		[MaxLength(50)]
		public string ShortName { get; set; }

		[MaxLength(50)]
		public string Code { get; set; }

		[MaxLength(2000)]
		public string Description { get; set; }

		[MaxLength(50)]
		public string PhoneNumber { get; set; }

		[MaxLength(500)]
		public string Website { get; set; }

		[MaxLength(500)]
		public string Facebook { get; set; }

		[MaxLength(500)]
		public string Twitter { get; set; }

		[MaxLength(500)]
		public string Instagram { get; set; }

		[MaxLength(500)]
		public string YouTube { get; set; }

		[MaxLength(500)]
		public string LinkedIn { get; set; }

		public bool UseDepartmentBrandingInEmails { get; set; }
	}
}
