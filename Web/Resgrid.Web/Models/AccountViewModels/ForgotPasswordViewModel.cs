using Resgrid.WebCore.Models;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Models.AccountViewModels
{
    public class ForgotPasswordViewModel: GoogleReCaptchaModelBase
	{
		public string CaptchaId { get; set; }
		public string UserEnteredCaptchaCode { get; set; }

		public string SiteKey { get; set; }

		[Required]
        [EmailAddress]
        public string Email { get; set; }

	}
}
