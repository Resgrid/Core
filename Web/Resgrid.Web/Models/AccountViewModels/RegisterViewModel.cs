using Resgrid.WebCore.Models;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Models.AccountViewModels
{
	public class RegisterViewModel: GoogleReCaptchaModelBase
	{
		public string CaptchaId { get; set; }
		public string UserEnteredCaptchaCode { get; set; }

		public string SiteKey { get; set; }

		[Required]
		[Display(Name = "FirstName")]
		public string FirstName { get; set; }

		[Required]
		[Display(Name = "LastName")]
		public string LastName { get; set; }

		[Required]
		[Display(Name = "Username")]
		public string Username { get; set; }
		
		[Required]
		[Display(Name = "Department Name")]
		public string DepartmentName { get; set; }

		[Display(Name = "Department Type")]
		public string DepartmentType { get; set; }

		[Required]
		[EmailAddress]
		[Display(Name = "Email")]
		public string Email { get; set; }

		[Required]
		[StringLength(100, ErrorMessage = "The passowrd must be at least 8 characters long, include a number (digit) and an uppercase letter", MinimumLength = 8)]
		[DataType(DataType.Password)]
		[Display(Name = "Password")]
		public string Password { get; set; }

		[DataType(DataType.Password)]
		[Display(Name = "Confirm password")]
		[Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
		public string ConfirmPassword { get; set; }

		public IEnumerable<string> DepartmentTypes = new List<string>() { "Volunteer Fire", "Career Fire", "Search and Rescue", "HAZMAT", "EMS", "CERT", "Public Safety", "Disaster Response", "Relief Org", "Security", "Repair Services", "Delivery Services", "Oil and Gas", "Power", "Chemical", "Nuclear", "Other Industrial", "Other Private", "Other Public", "Other" };
	}
}
