using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Sso
{
	/// <summary>
	/// Starts a server-brokered SSO sign-in (workbook section 7.3). Name the department with one of the three
	/// identifiers. The client keeps its PKCE verifier in memory and sends only the S256 challenge.
	/// </summary>
	public class SsoBeginInput
	{
		public string DepartmentToken { get; set; }
		public string DepartmentCode { get; set; }
		public string Username { get; set; }

		/// <summary>web, responder, unit, dispatch or ic; defaults to the <c>X-Resgrid-Client</c> header.</summary>
		public string ClientApp { get; set; }

		/// <summary>ios, android, web or electron; a label only.</summary>
		public string Platform { get; set; }

		/// <summary>Where the one-time code is sent: a return target registered for this client, matched exactly.</summary>
		public string ReturnTarget { get; set; }

		/// <summary>The client's CSRF value, echoed back unchanged on return.</summary>
		public string State { get; set; }

		public string CodeChallenge { get; set; }

		/// <summary>Only <c>S256</c>.</summary>
		public string CodeChallengeMethod { get; set; }

		/// <summary>
		/// <c>login</c> (default), <c>reauth</c> (fresh proof for the signed-in session), or <c>step_up</c> (provider MFA:
		/// for the signed-in session's <see cref="Operation"/>, or to complete the password sign-in in <see cref="Transaction"/>).
		/// </summary>
		public string Purpose { get; set; }

		/// <summary>For a session <c>step_up</c>: the operation it serves (<c>security_change</c>, <c>adp_management</c>, <c>chat_export</c>).</summary>
		public string Operation { get; set; }

		/// <summary>
		/// For a <c>step_up</c> that completes a password sign-in: the login MFA transaction secret. Redeem the step-up's code
		/// at <c>Authentication/CompleteFederated</c>.
		/// </summary>
		public string Transaction { get; set; }
	}

	public class SsoBeginResult : StandardApiResponseV4Base
	{
		public SsoBeginResultData Data { get; set; }
	}

	public class SsoBeginResultData
	{
		/// <summary>Open this in the platform's authentication session, the system browser, or a top-level redirect.</summary>
		public string AuthorizeUrl { get; set; }

		public string SsoTransactionId { get; set; }

		public int ExpiresIn { get; set; }
	}

	public class SsoRedeemInput
	{
		public string SsoTransactionId { get; set; }

		/// <summary>The one-time <c>sso_code</c> from the return target.</summary>
		public string SsoCode { get; set; }

		/// <summary>The PKCE verifier whose S256 challenge began the sign-in.</summary>
		public string CodeVerifier { get; set; }

		/// <summary>Optional OAuth client_id, as the password grant takes it (refresh-token lifetime).</summary>
		public string ClientId { get; set; }
	}

	public class SsoRedeemResult : StandardApiResponseV4Base
	{
		public SsoRedeemResultData Data { get; set; }
	}

	/// <summary>
	/// <c>mfa_required</c>: complete the second factor through <c>Authentication/*</c> with <see cref="Transaction"/>.
	/// <c>completed</c>: no second factor is required; redeem <see cref="CompletionCode"/> and <see cref="Transaction"/>
	/// with the <c>mfa_completion</c> grant. <c>reauthenticated</c>: the session has fresh SSO proof.
	/// </summary>
	public class SsoRedeemResultData
	{
		public string Outcome { get; set; }

		/// <summary>The login MFA transaction secret (for <c>mfa_required</c> and <c>completed</c>).</summary>
		public string Transaction { get; set; }

		public int ExpiresIn { get; set; }

		public List<string> MfaMethods { get; set; }

		public List<string> MfaEnrolled { get; set; }

		public string MfaPreferred { get; set; }

		public string CompletionCode { get; set; }

		/// <summary>For <c>completed</c>: <c>federated</c> when the provider's MFA satisfied the sign-in; null when none was needed.</summary>
		public string MfaSatisfiedBy { get; set; }

		public string VerifiedAt { get; set; }
	}
}
