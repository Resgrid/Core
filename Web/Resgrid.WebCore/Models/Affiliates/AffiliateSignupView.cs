using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Models.Affiliates
{
	public class AffiliateSignupView
	{
		public string Result { get; set; }

		[Required]
		[Display(Name = "First name")]
		public string FirstName { get; set; }

		[Required]
		[Display(Name = "Last name")]
		public string LastName { get; set; }

		[Required]
		[Display(Name = "Email Addres")]
		public string EmailAddress { get; set; }

		public string CompanyOrDepartment { get; set; }

		public string Country { get; set; }

		public string Region { get; set; }

		[Required]
		[Display(Name = "Your Experiance")]
		public string Experiance { get; set; }

		[Required]
		[Display(Name = "Your Qualifications")]
		public string Qualifications { get; set; }
	}
}