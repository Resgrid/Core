using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models.TwoFactor
{
	public class EnableAuthenticatorViewModel
	{
		/// <summary>Base32-encoded authenticator key for manual entry.</summary>
		public string SharedKey { get; set; }

		/// <summary>Full otpauth:// URI for QR code generation.</summary>
		public string AuthenticatorUri { get; set; }

		/// <summary>QR code as a base64-encoded PNG data URL.</summary>
		public string QrCodeDataUrl { get; set; }

		[Required]
		[StringLength(7, ErrorMessage = "The {0} must be at least {2} and at most {1} characters long.", MinimumLength = 6)]
		[DataType(DataType.Text)]
		[Display(Name = "Verification Code")]
		public string Code { get; set; }
	}

	public class ShowRecoveryCodesViewModel
	{
		public IEnumerable<string> RecoveryCodes { get; set; }
	}

	public class Disable2FAViewModel
	{
		[Required]
		[StringLength(7, MinimumLength = 6)]
		[DataType(DataType.Text)]
		[Display(Name = "Verification Code")]
		public string Code { get; set; }
	}

	public class TwoFactorIndexViewModel
	{
		public bool HasAuthenticator { get; set; }
		public bool Is2FAEnabled { get; set; }
		public int RecoveryCodesLeft { get; set; }
		public bool RecoveryCodeWarning { get; set; }
	}

	public class VerifyRecoveryCodeViewModel
	{
		[Required]
		[DataType(DataType.Text)]
		[Display(Name = "Recovery Code")]
		public string Code { get; set; }
	}

	public class StepUpVerifyViewModel
	{
		[Required]
		[StringLength(7, MinimumLength = 6)]
		[DataType(DataType.Text)]
		[Display(Name = "Verification Code")]
		public string Code { get; set; }

		public string ReturnUrl { get; set; }
	}
}

