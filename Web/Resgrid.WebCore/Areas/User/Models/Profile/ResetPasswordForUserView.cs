using System;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models.Profile
{
	public class ResetPasswordForUserView
	{
		public string Message { get; set; }
		public string Name { get; set; }
		public string Email { get; set; }
		public string Username { get; set; }
		public string UserId { get; set; }
		public bool EmailUser { get; set; }

		[Required]
		[StringLength(100, ErrorMessage = "The {0} must be at least 8 characters long.", MinimumLength = 8)]
		[DataType(DataType.Password)]
		[Display(Name = "Password")]
		public string Password { get; set; }

		[DataType(DataType.Password)]
		[Display(Name = "Confirm password")]
		[System.ComponentModel.DataAnnotations.Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
		public string ConfirmPassword { get; set; }
	}
}
