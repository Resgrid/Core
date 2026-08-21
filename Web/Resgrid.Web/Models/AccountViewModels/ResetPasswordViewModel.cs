using System.ComponentModel.DataAnnotations;

using Resgrid.Framework;

namespace Resgrid.Web.Models.AccountViewModels
{
    public class ResetPasswordViewModel
    {
        [Required]
		[StringLength(100, MinimumLength = 8)]
		[PasswordComplexity(MinLength = 8, RequireUppercase = true, RequireLowercase = true, RequireDigit = true, RequireSpecialChar = false)]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; }

		public int MinPasswordLength { get; set; } = 8;
		public bool InvalidOrExpired { get; set; }
    }
}
