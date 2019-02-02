using System.ComponentModel.DataAnnotations;
using Resgrid.Web.Extensions;

namespace Resgrid.Web.Models
{
	public class ContactView
	{
		public string Result { get; set; }

		[Required]
		public string Name { get; set; }
		[Required, ValidateEmail(ErrorMessage = "Valid email is required.")]
		public string Email { get; set; }
		[Required]
		public string Message { get; set; }
	}
}