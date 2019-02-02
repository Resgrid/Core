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

		public string Address1 { get; set; }

		[MaxLength(100)]
		public string City { get; set; }

		[MaxLength(50)]
		public string State { get; set; }

		[MaxLength(50)]
		public string PostalCode { get; set; }

		[MaxLength(100)]
		public string Country { get; set; }
	}
}