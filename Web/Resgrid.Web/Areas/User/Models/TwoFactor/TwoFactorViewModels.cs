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

		/// <summary>True when this form replaces an enrolled authenticator rather than setting up the first one.</summary>
		public bool IsReplacement { get; set; }
	}

	public class ShowRecoveryCodesViewModel
	{
		public IEnumerable<string> RecoveryCodes { get; set; }

		/// <summary>True after a factor change that signed every session out; the page offers sign-in instead of the MFA index.</summary>
		public bool SignInAgainRequired { get; set; }
	}

	public class Disable2FAViewModel
	{
		[Required]
		[StringLength(7, MinimumLength = 6)]
		[DataType(DataType.Text)]
		[Display(Name = "Verification Code")]
		public string Code { get; set; }

		/// <summary>Set by the server: TOTP cannot be turned off while the user has passkeys.</summary>
		public bool BlockedByPasskeys { get; set; }
	}

	public class TwoFactorIndexViewModel
	{
		public bool HasAuthenticator { get; set; }
		public bool Is2FAEnabled { get; set; }
		public int RecoveryCodesLeft { get; set; }
		public bool RecoveryCodeWarning { get; set; }

		/// <summary>The user's passkeys in every app (passkey plan section 6.5); any of them can be renamed or removed here.</summary>
		public System.Collections.Generic.List<PasskeyRowView> Passkeys { get; set; } = new();

		/// <summary>Whether a passkey for the web can be added on this deployment now.</summary>
		public bool WebPasskeyRegistrationAvailable { get; set; }

		/// <summary>The outcome of the last passkey change on this page: added, renamed or removed.</summary>
		public string PasskeyStatus { get; set; }
	}

	public class PasskeyRowView
	{
		public string Id { get; set; }
		public string DisplayName { get; set; }

		/// <summary>The localization key naming the app the passkey works in.</summary>
		public string AppLabelKey { get; set; }

		public System.DateTime CreatedOn { get; set; }
		public System.DateTime? LastUsedOn { get; set; }
		public bool CreatedOnSharedInstallation { get; set; }
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

		/// <summary>Offer "Use a passkey": the user has one for the web and the guarded action's scope accepts it here.</summary>
		public bool PasskeyAvailable { get; set; }

		/// <summary>Offer "Approve with Responder": the user has an eligible Responder and the scope accepts approval here.</summary>
		public bool ApprovalAvailable { get; set; }

		/// <summary>Offer provider step-up: the account signs in through the department's provider and the scope accepts its MFA here.</summary>
		public bool FederatedAvailable { get; set; }

		/// <summary>The guarded action's scope, as the guard named it (a display hint; every command checks it again).</summary>
		public string Scope { get; set; }

		/// <summary>The department being entered, when this verification is for entering it (plan section 7.6 row 5).</summary>
		public int? EntryDepartmentId { get; set; }

		/// <summary>The guard could not hold what the user submitted; after verifying they must submit it again.</summary>
		public bool SubmissionNotHeld { get; set; }

		/// <summary>The guard is holding what the user submitted; verifying finishes it.</summary>
		public bool HoldingSubmission =>
			ReturnUrl != null && ReturnUrl.Contains(Resgrid.Web.Helpers.StepUpFormReplay.ResumePath + "?", System.StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>The page that posts a held submission back after step-up (<see cref="Resgrid.Web.Helpers.StepUpFormReplay"/>).</summary>
	public class StepUpResumeViewModel
	{
		/// <summary>The held submission; null when it has expired, was already sent, or is not this session's.</summary>
		public string Id { get; set; }

		/// <summary>The address it was made to.</summary>
		public string Target { get; set; }

		/// <summary>It was a script call: replay it in the background, then return to <see cref="BackUrl"/>.</summary>
		public bool Script { get; set; }

		public string BackUrl { get; set; }
	}
}

