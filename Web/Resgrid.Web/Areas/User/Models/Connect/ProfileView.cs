using System.ComponentModel.DataAnnotations;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Connect
{
	public class ProfileView
	{
		public string Message { get; set; }

		public DepartmentProfile Profile { get; set; }

		public string ImageUrl { get; set; }
		
		public Department Department { get; set; }

		public string ApiUrl { get; set; }

		[StringLength(500, ErrorMessage = "Street address cannot exceed 500 characters.")]
		public string Address1 { get; set; }

		[StringLength(150, ErrorMessage = "City cannot exceed 150 characters.")]
		public string City { get; set; }

		[StringLength(100, ErrorMessage = "State/Province cannot exceed 100 characters.")]
		public string State { get; set; }

		[StringLength(32, ErrorMessage = "Postal code cannot exceed 32 characters.")]
		public string PostalCode { get; set; }

		[StringLength(100, ErrorMessage = "Country cannot exceed 100 characters.")]
		public string Country { get; set; }
	}
}