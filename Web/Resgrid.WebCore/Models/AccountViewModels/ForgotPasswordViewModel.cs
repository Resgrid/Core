using Resgrid.WebCore.Models;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Models.AccountViewModels
{
    public class ForgotPasswordViewModel: GoogleReCaptchaModelBase
	{
        [Required]
        [EmailAddress]
        public string Email { get; set; }

		public string SiteKey { get; set; }

	}
}
