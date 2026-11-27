using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Resgrid.Web.Services.Models.v4.Passkeys;

namespace Resgrid.Web.Services.Models.v4.StepUp
{
	/// <summary>
	/// <c>GET StepUp/Options?area=</c>: the department's rule for the area and what this member can use for it, the same facts a
	/// <c>step_up_required</c> refusal carries. Advisory: <c>StepUp/Verify</c> rechecks everything.
	/// </summary>
	public class StepUpOptionsV4Result : StandardApiResponseV4Base
	{
		public StepUpOptionsV4ResultData Data { get; set; }
	}

	public class StepUpOptionsV4ResultData
	{
		/// <summary><c>signatures</c>, <c>acknowledgments</c>, <c>approvals</c> or <c>protected_data</c>.</summary>
		public string Area { get; set; }

		/// <summary>False when the department made step-up optional for the area (the action goes ahead without it).</summary>
		public bool Required { get; set; }

		/// <summary>The kinds of proof the department allows: <c>mfa</c> always, and <c>sso</c> / <c>password</c> where it chose to.</summary>
		public List<string> AllowedMethods { get; set; }

		/// <summary>The allowed kinds this member has (an authenticator, a linked provider account, a password they may sign in with).</summary>
		public List<string> UsableMethods { get; set; }

		/// <summary>The member's usable second factors: <c>totp</c>, <c>passkey</c> (this app), <c>passkey_approval</c>, <c>federated</c>.</summary>
		public List<string> MfaMethods { get; set; }

		/// <summary>How long one verification serves the area (protected data: the department's ADP grant window).</summary>
		public int WindowMinutes { get; set; }

		/// <summary>False when nothing allowed is usable: the member must set up two-factor authentication (on the web or in the app).</summary>
		public bool CanComplete { get; set; }

		/// <summary>This session already satisfies the area (not for protected data, whose proof is the grant).</summary>
		public bool Satisfied { get; set; }

		/// <summary>When the current verification stops serving the area (ISO 8601 UTC), when <see cref="Satisfied"/>.</summary>
		public string SatisfiedUntil { get; set; }

		/// <summary>Assertion options for this app's passkeys when <c>passkey</c> is usable: send its request id and the platform's response to Verify.</summary>
		public PasskeyCeremonyResultData Passkey { get; set; }
	}

	/// <summary><c>POST StepUp/Verify</c>: one verification for one area.</summary>
	public class StepUpVerifyInput
	{
		/// <summary><c>signatures</c>, <c>acknowledgments</c>, <c>approvals</c> or <c>protected_data</c>.</summary>
		public string Area { get; set; }

		/// <summary><c>totp</c>, <c>passkey</c>, <c>passkey_approval</c>, <c>federated</c> (the MFA kind), <c>password</c> or <c>sso</c>.</summary>
		public string Method { get; set; }

		/// <summary>For <c>totp</c>: the current authenticator code.</summary>
		public string Code { get; set; }

		/// <summary>For <c>password</c>: the account password. Never logged or stored.</summary>
		public string Password { get; set; }

		/// <summary>For <c>passkey</c>: the request id from <c>StepUp/Options</c>.</summary>
		public string RequestId { get; set; }

		/// <summary>For <c>passkey</c>: the platform's assertion (PublicKeyCredential.toJSON()).</summary>
		public JToken Credential { get; set; }

		/// <summary>
		/// For <c>passkey_approval</c>: the approved request from <c>MfaApproval/Request</c> — purpose <c>step_up</c> with operation
		/// <c>sensitive_operation</c>, or purpose <c>adp</c> for <c>protected_data</c>.
		/// </summary>
		public string ApprovalRequestId { get; set; }

		/// <summary>
		/// For <c>sso</c> and <c>federated</c>: the transaction from <c>Sso/Begin</c> — purpose <c>reauth</c> for <c>sso</c>; purpose <c>step_up</c>
		/// with operation <c>sensitive_operation</c> (or <c>adp_step_up</c> for <c>protected_data</c>) for <c>federated</c>.
		/// </summary>
		public string SsoTransactionId { get; set; }

		/// <summary>For <c>sso</c> and <c>federated</c>: the one-time <c>sso_code</c> the app's return target received.</summary>
		public string SsoCode { get; set; }

		/// <summary>For <c>sso</c> and <c>federated</c>: the PKCE verifier whose S256 challenge began the round trip.</summary>
		public string CodeVerifier { get; set; }
	}

	public class StepUpVerifyResult : StandardApiResponseV4Base
	{
		public StepUpVerifyResultData Data { get; set; }
	}

	public class StepUpVerifyResultData
	{
		public string Area { get; set; }

		/// <summary>The method verified (as sent).</summary>
		public string Method { get; set; }

		/// <summary><c>mfa</c>, <c>sso</c> or <c>password</c>.</summary>
		public string MethodClass { get; set; }

		/// <summary>When the verification happened (ISO 8601 UTC; for <c>sso</c>, the provider's own authentication time).</summary>
		public string VerifiedAt { get; set; }

		/// <summary>When it stops serving the area (ISO 8601 UTC). Retry the refused request before then.</summary>
		public string ExpiresAt { get; set; }

		/// <summary>
		/// For <c>protected_data</c> only: the Protected Data Grant to send as <c>X-Resgrid-Protected-Grant</c>, exactly as
		/// <c>DataProtection/VerifyStepUp</c> returns one. Null for the other areas, whose grant is held on the server for this session.
		/// </summary>
		public string GrantId { get; set; }

		public string GrantToken { get; set; }

		/// <summary>The grant's absolute expiry (ISO 8601 UTC); clients conceal protected values at this time.</summary>
		public string GrantExpiresAt { get; set; }
	}
}
