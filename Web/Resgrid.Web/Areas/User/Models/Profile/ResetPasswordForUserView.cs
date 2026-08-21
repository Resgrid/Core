using System;
using System.ComponentModel.DataAnnotations;
using Resgrid.Framework;

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
		public bool IsSsoManaged { get; set; }
		public bool UseEmailResetLink { get; set; }

		/// <summary>Effective minimum password length from the department policy (≥ 8). Shown as a hint in the view.</summary>
		public int MinPasswordLength { get; set; } = 8;

		[Required]
		[StringLength(100, ErrorMessage = "The {0} must be at least 8 characters long.", MinimumLength = 8)]
		[PasswordComplexity(MinLength = 8, RequireUppercase = true, RequireLowercase = true, RequireDigit = true, RequireSpecialChar = false)]
		[DataType(DataType.Password)]
		[Display(Name = "Password")]
		public string Password { get; set; }

		[DataType(DataType.Password)]
		[Display(Name = "Confirm password")]
		[System.ComponentModel.DataAnnotations.Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
		public string ConfirmPassword { get; set; }

		public bool MustChangePasswordOnLogin { get; set; }
	}
}
