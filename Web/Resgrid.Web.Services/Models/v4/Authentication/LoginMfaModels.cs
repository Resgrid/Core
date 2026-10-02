using Newtonsoft.Json.Linq;

namespace Resgrid.Web.Services.Models.v4.Authentication
{
	/// <summary>
	/// The login MFA transaction a password grant returned as <c>mfa_transaction</c> (workbook section 7.1). The secret is
	/// the only authority these endpoints accept; keep it in memory and send it only in request bodies.
	/// </summary>
	public class LoginTransactionInput
	{
		public string Transaction { get; set; }
	}

	/// <summary>A TOTP code or a recovery code for the transaction.</summary>
	public class CompleteLoginCodeInput : LoginTransactionInput
	{
		public string Code { get; set; }
	}

	public class CompleteLoginPasskeyInput : LoginTransactionInput
	{
		/// <summary>The request id from <c>Authentication/PasskeyOptions</c>.</summary>
		public string RequestId { get; set; }

		/// <summary>The platform's assertion (PublicKeyCredential.toJSON()), as an object or a JSON string.</summary>
		public JToken Credential { get; set; }
	}

	/// <summary>A new authenticator key staged for the setup transaction; add it to an authenticator app, then send a code.</summary>
	public class TotpSetupResult : StandardApiResponseV4Base
	{
		public TotpSetupResultData Data { get; set; }
	}

	public class TotpSetupResultData
	{
		/// <summary>The key to type into an authenticator app, in groups of four.</summary>
		public string SharedKey { get; set; }

		/// <summary>The otpauth:// URI to show as a QR code.</summary>
		public string AuthenticatorUri { get; set; }

		public int ExpiresIn { get; set; }
	}

	/// <summary>A Responder approval requested for the transaction (<c>MfaApproval/Request</c> with purpose <c>login</c>) and approved.</summary>
	public class CompleteLoginApprovalInput : LoginTransactionInput
	{
		public string ApprovalRequestId { get; set; }
	}

	/// <summary>Provider step-up for the transaction: the round trip from <c>Sso/Begin</c> (purpose <c>step_up</c>, same transaction).</summary>
	public class CompleteLoginFederatedInput : LoginTransactionInput
	{
		public string SsoTransactionId { get; set; }

		/// <summary>The one-time <c>sso_code</c> the return target received.</summary>
		public string SsoCode { get; set; }

		/// <summary>The PKCE verifier whose S256 challenge began the step-up.</summary>
		public string CodeVerifier { get; set; }
	}

	/// <summary>
	/// The one-use completion code. Redeem it at <c>/api/v4/connect/token</c> with
	/// <c>grant_type=urn:resgrid:params:oauth:grant-type:mfa_completion</c>, <c>completion_code</c> and <c>transaction</c>.
	/// </summary>
	public class LoginCompletionResult : StandardApiResponseV4Base
	{
		public LoginCompletionResultData Data { get; set; }
	}

	public class LoginCompletionResultData
	{
		public string CompletionCode { get; set; }

		public int ExpiresIn { get; set; }

		/// <summary>True when a recovery code completed the sign-in: the session is recovery-classified; replace the lost factor.</summary>
		public bool Recovery { get; set; }

		/// <summary>After <c>CompleteTotpSetup</c>: the new recovery codes, shown once. Null otherwise.</summary>
		public System.Collections.Generic.List<string> RecoveryCodes { get; set; }
	}
}
