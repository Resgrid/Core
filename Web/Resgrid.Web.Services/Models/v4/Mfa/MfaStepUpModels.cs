using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Resgrid.Web.Services.Models.v4.Passkeys;

namespace Resgrid.Web.Services.Models.v4.Mfa
{
	/// <summary>
	/// The second-factor choice for a named operation (passkey workbook section 7.2). Advisory: <c>VerifyStepUp</c>
	/// re-checks everything.
	/// </summary>
	public class StepUpOptionsResult : StandardApiResponseV4Base
	{
		public StepUpOptionsResultData Data { get; set; }
	}

	public class StepUpOptionsResultData
	{
		/// <summary>Methods the user can verify with now: enrolled and allowed.</summary>
		public List<string> Methods { get; set; }

		public List<string> EnrolledMethods { get; set; }

		public List<string> AllowedMethods { get; set; }

		/// <summary>The method to show first; null when nothing is usable.</summary>
		public string Preferred { get; set; }

		/// <summary>True when the user must enroll a second factor before this operation.</summary>
		public bool EnrollmentRequired { get; set; }

		/// <summary>How long one verification serves this operation.</summary>
		public int WindowMinutes { get; set; }

		/// <summary>
		/// Assertion options for this app's passkeys, when a passkey is usable for the operation; send its request id and
		/// the platform's response to <c>VerifyStepUp</c> with method <c>passkey</c>.
		/// </summary>
		public PasskeyCeremonyResultData Passkey { get; set; }
	}

	public class VerifyStepUpInput
	{
		/// <summary>One of <c>security_change</c>, <c>adp_management</c>, <c>chat_export</c> or <c>account_security</c>.</summary>
		public string Operation { get; set; }

		/// <summary><c>totp</c> (the default), <c>passkey</c>, <c>passkey_approval</c> (Responder approval) or <c>federated</c> (provider step-up).</summary>
		public string Method { get; set; }

		/// <summary>The current authenticator code, for <c>totp</c>.</summary>
		public string Code { get; set; }

		/// <summary>The request id from <c>StepUpOptions</c>, for <c>passkey</c>.</summary>
		public string RequestId { get; set; }

		/// <summary>The platform's assertion (PublicKeyCredential.toJSON()), for <c>passkey</c>.</summary>
		public JToken Credential { get; set; }

		/// <summary>For <c>passkey_approval</c>: the approved request from <c>MfaApproval/Request</c> (purpose <c>step_up</c>, same operation).</summary>
		public string ApprovalRequestId { get; set; }

		/// <summary>For <c>federated</c>: the transaction from <c>Sso/Begin</c> (purpose <c>step_up</c>, same operation).</summary>
		public string SsoTransactionId { get; set; }

		/// <summary>For <c>federated</c>: the one-time <c>sso_code</c> the return target received.</summary>
		public string SsoCode { get; set; }

		/// <summary>For <c>federated</c>: the PKCE verifier whose challenge began the step-up.</summary>
		public string CodeVerifier { get; set; }
	}

	/// <summary>Evidence stays on the server; no token is returned (workbook section 7.2).</summary>
	public class VerifyStepUpResult : StandardApiResponseV4Base
	{
		public VerifyStepUpResultData Data { get; set; }
	}

	public class VerifyStepUpResultData
	{
		/// <summary>When the second factor was verified (ISO 8601 UTC).</summary>
		public string VerifiedAt { get; set; }

		/// <summary>When this verification stops serving the operation (ISO 8601 UTC).</summary>
		public string ExpiresAt { get; set; }
	}
}
