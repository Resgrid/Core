using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Models.AccountViewModels
{
    public class LoginViewModel
    {
        [Required]
		[StringLength(250, ErrorMessage = "The username must be at least 2 characters long and contain only alphanumeric characters.", MinimumLength = 2)]
		public string Username { get; set; }

        [Required]
		[StringLength(100, ErrorMessage = "The password must be at least 8 characters long, include a number (digit) and an uppercase letter", MinimumLength = 4)]
		[DataType(DataType.Password)]
        public string Password { get; set; }

        [Display(Name = "Remember me?")]
        public bool RememberMe { get; set; }
    }
}
