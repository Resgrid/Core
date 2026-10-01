using System.Collections.Generic;
using Resgrid.Web.Services.Models.v4.Passkeys;

namespace Resgrid.Web.Services.Models.v4.AccountSecurity
{
	/// <summary>
	/// The signed-in user's own sign-in methods (plan section 6.5): the same view in every app.
	/// </summary>
	public class AccountMethodsResult : StandardApiResponseV4Base
	{
		public AccountMethodsResultData Data { get; set; }
	}

	public class AccountMethodsResultData
	{
		/// <summary>The app this request came from: web, responder, unit, dispatch or ic.</summary>
		public string CurrentClient { get; set; }

		public TotpMethodData Totp { get; set; }

		/// <summary>One group per app a passkey can be bound to, in display order.</summary>
		public List<PasskeyClientGroupData> PasskeyGroups { get; set; }

		/// <summary>Responder installations that can receive approval requests; stop them through <c>MfaApproval/Installations/Disable</c>.</summary>
		public List<ApprovalInstallationData> ApprovalInstallations { get; set; }

		/// <summary>Department identity-provider links. Informational: the department and SCIM manage them.</summary>
		public List<LinkedIdentityData> LinkedIdentities { get; set; }

		/// <summary>Verifications in the last <c>TwoFactorConfig.MfaActivityRetentionDays</c> days, newest first (at most 100).</summary>
		public List<MfaActivityData> RecentActivity { get; set; }
	}

	public class ApprovalInstallationData
	{
		/// <summary>Pass to <c>MfaApproval/Installations/Disable</c>.</summary>
		public string InstallationId { get; set; }

		public string Label { get; set; }
		public string Platform { get; set; }

		/// <summary>This request came from this installation.</summary>
		public bool IsCurrent { get; set; }

		/// <summary>Requests reach it now: approvals are not stopped here and a Responder passkey can approve.</summary>
		public bool ApprovalsOn { get; set; }

		public string StoppedOn { get; set; }
		public string LastDecisionOn { get; set; }

		/// <summary>approved or denied; null when it has decided nothing recently.</summary>
		public string LastDecision { get; set; }
	}

	public class LinkedIdentityData
	{
		public int DepartmentId { get; set; }
		public string DepartmentName { get; set; }

		/// <summary>saml2 or oidc.</summary>
		public string ProviderType { get; set; }

		public string LinkedOn { get; set; }

		/// <summary>The department accepts provider step-up and its mapping is tested, so this identity can verify there.</summary>
		public bool AcceptsProviderStepUp { get; set; }

		public string LastProviderStepUpOn { get; set; }
	}

	public class MfaActivityData
	{
		/// <summary>Pass to <c>AccountSecurity/ReportActivity</c>.</summary>
		public string ActivityId { get; set; }

		public string OccurredOn { get; set; }

		/// <summary>totp, passkey, passkey_approval, federated or recovery_code.</summary>
		public string Method { get; set; }

		/// <summary>login, reauthentication, step_up, adp_step_up or shared_unlock.</summary>
		public string Purpose { get; set; }

		/// <summary>False for a denied verification: a wrong code, a failed passkey or a denied approval.</summary>
		public bool Successful { get; set; }

		public string Client { get; set; }
		public string Installation { get; set; }
		public bool SharedInstallation { get; set; }

		/// <summary>It was for the session making this request; reporting it does not end this session.</summary>
		public bool IsCurrentSession { get; set; }

		public string ReportedOn { get; set; }
	}

	public class ReportActivityInput
	{
		public string ActivityId { get; set; }
	}

	public class ReportActivityResult : StandardApiResponseV4Base
	{
		public ReportActivityResultData Data { get; set; }
	}

	public class ReportActivityResultData
	{
		/// <summary>The session the verification opened or served was ended.</summary>
		public bool SessionEnded { get; set; }

		/// <summary>What the app should offer next: change_password, review_methods.</summary>
		public List<string> NextSteps { get; set; }
	}

	/// <summary>Resgrid cannot see where copies of an authenticator's setup key exist; replacing it is the remedy.</summary>
	public class TotpMethodData
	{
		public bool Enrolled { get; set; }
		public string EnrolledOn { get; set; }
		public string LastUsedOn { get; set; }
		public int RecoveryCodesRemaining { get; set; }
		public bool RecoveryCodeWarning { get; set; }

		/// <summary>The current authenticator was set up on a shared installation, so its setup key may have been seen there.</summary>
		public bool SetUpOnSharedInstallation { get; set; }

		/// <summary>The app and installation the current authenticator was set up or last replaced from, when recorded.</summary>
		public string EnrolledClient { get; set; }

		public string EnrolledInstallation { get; set; }

		/// <summary>Where its last successful use in the recent-activity window came from.</summary>
		public string LastUsedClient { get; set; }

		public string LastUsedInstallation { get; set; }

		/// <summary>False while any passkey exists: turning TOTP off is blocked in this release (plan section 7.5 rule 7).</summary>
		public bool CanTurnOff { get; set; }
	}

	public class PasskeyClientGroupData
	{
		public string Client { get; set; }

		/// <summary>Whether a passkey for this app can be registered on this deployment now (from that app).</summary>
		public bool RegistrationAvailable { get; set; }

		public List<PasskeyResultData> Passkeys { get; set; }
	}

	public class ReauthenticateInput
	{
		public string Password { get; set; }
	}

	public class ReauthenticateResult : StandardApiResponseV4Base
	{
		public ReauthenticateResultData Data { get; set; }
	}

	public class ReauthenticateResultData
	{
		public string VerifiedAt { get; set; }
	}

	/// <summary>A new authenticator key, staged; add it to an authenticator app, then send a code from it to <c>ReplaceTotp</c>.</summary>
	public class ReplaceTotpOptionsResult : StandardApiResponseV4Base
	{
		public ReplaceTotpOptionsResultData Data { get; set; }
	}

	public class ReplaceTotpOptionsResultData
	{
		/// <summary>The key to type into an authenticator app, in groups of four.</summary>
		public string SharedKey { get; set; }

		/// <summary>The otpauth:// URI to show as a QR code.</summary>
		public string AuthenticatorUri { get; set; }

		public int ExpiresIn { get; set; }
	}

	public class ReplaceTotpInput
	{
		/// <summary>A code from the new authenticator.</summary>
		public string Code { get; set; }
	}

	public class ReplaceTotpResult : StandardApiResponseV4Base
	{
		public ReplaceTotpResultData Data { get; set; }
	}

	public class ReplaceTotpResultData
	{
		/// <summary>The new recovery codes, shown once; every earlier code no longer works.</summary>
		public List<string> RecoveryCodes { get; set; }

		/// <summary>Always true: every session, this one too, has ended.</summary>
		public bool SignInAgain { get; set; }
	}

	/// <summary>Starts "I lost my authenticator": the sign-in in progress (<c>mfa_transaction</c>) and one recovery code.</summary>
	public class BeginFactorRecoveryInput
	{
		public string Transaction { get; set; }

		public string Code { get; set; }
	}

	/// <summary>The recovery secret from <c>BeginFactorRecovery</c>; keep it in memory only.</summary>
	public class FactorRecoveryInput
	{
		public string Transaction { get; set; }
	}

	public class CompleteFactorRecoveryInput : FactorRecoveryInput
	{
		/// <summary>A code from the new authenticator staged by <c>PrepareReplacement</c>.</summary>
		public string Code { get; set; }

		/// <summary>Lost passkeys to remove, from the recovery status; others are kept.</summary>
		public List<string> RemovePasskeyIds { get; set; }
	}

	public class FactorRecoveryStatusResult : StandardApiResponseV4Base
	{
		public FactorRecoveryStatusResultData Data { get; set; }
	}

	public class FactorRecoveryStatusResultData
	{
		/// <summary>The recovery secret, returned once by <c>BeginFactorRecovery</c>; null otherwise.</summary>
		public string Transaction { get; set; }

		/// <summary><c>pending</c> or <c>canceled</c>.</summary>
		public string State { get; set; }

		public int ExpiresIn { get; set; }

		/// <summary><c>prepare_replacement</c>, <c>complete</c> and <c>cancel</c>.</summary>
		public List<string> NextActions { get; set; }

		/// <summary>The account's passkeys, to choose any that were lost.</summary>
		public List<PasskeyResultData> Passkeys { get; set; }
	}
}
