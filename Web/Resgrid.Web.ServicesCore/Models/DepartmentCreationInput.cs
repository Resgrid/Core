using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models
{
	public class DepartmentCreationInput
	{
		[Required]
		public string Username { get; set; }

		[Required]
		public string FullName { get; set; }

		[Required]
		public string DepartmentName { get; set; }

		[Required]
		public string Email { get; set; }

		[Required]
		[StringLength(100, ErrorMessage = "The {0} must be at least {2} characters long.", MinimumLength = 6)]
		public string Password { get; set; }

		[Required]
		public string DepartmentType { get; set; }

		//public SelectList DepartmentTypes = new SelectList(new List<string>() { "Volunteer Fire", "Career Fire", "Search and Rescue", "HAZMAT", "EMS", "Private", "Other" });
	}
}
