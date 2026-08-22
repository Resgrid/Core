using System.ComponentModel.DataAnnotations;
using Resgrid.Framework;

namespace Resgrid.Web.Areas.User.Models.Security
{
	public class ChangeUsernameView
	{
		public string CurrentUsername { get; set; }
		public bool IsSsoManaged { get; set; }

		[Required, MaxLength(256)]
		[Display(Name = "New username")]
		public string NewUsername { get; set; }

		[Required, DataType(DataType.Password)]
		[Display(Name = "Current password")]
		public string CurrentPassword { get; set; }
	}

	public class ChangePasswordView
	{
		public bool IsSsoManaged { get; set; }
		public int MinPasswordLength { get; set; } = 8;

		[Required, DataType(DataType.Password)]
		[Display(Name = "Current password")]
		public string CurrentPassword { get; set; }

		[Required]
		[StringLength(100, MinimumLength = 8)]
		[PasswordComplexity(MinLength = 8, RequireUppercase = true, RequireLowercase = true, RequireDigit = true, RequireSpecialChar = false)]
		[DataType(DataType.Password)]
		[Display(Name = "New password")]
		public string NewPassword { get; set; }

		[Required, DataType(DataType.Password)]
		[Compare(nameof(NewPassword), ErrorMessage = "The new password and confirmation password do not match.")]
		[Display(Name = "Confirm new password")]
		public string ConfirmPassword { get; set; }
	}
}
