namespace Resgrid.Web.Services.Models.v4.DataProtection
{
	/// <summary>
	/// Step-up verification payload: the user's current authenticator (TOTP) code. Never logged.
	/// </summary>
	public class VerifyStepUpInput
	{
		public string Code { get; set; }
	}

	/// <summary>A passkey assertion for <c>DataProtection/VerifyPasskey</c> (passkey plan section 8.1).</summary>
	public class AdpPasskeyStepUpInput
	{
		/// <summary>The request id from <c>DataProtection/PasskeyOptions</c>.</summary>
		public string RequestId { get; set; }

		/// <summary>The platform's assertion (PublicKeyCredential.toJSON()), as an object or a JSON string.</summary>
		public Newtonsoft.Json.Linq.JToken Credential { get; set; }
	}

	/// <summary>An approved <c>MfaApproval/Request</c> (purpose <c>adp</c>) to use once for a grant.</summary>
	public class AdpApprovalStepUpInput
	{
		public string ApprovalRequestId { get; set; }
	}

	/// <summary>A brokered provider step-up (<c>Sso/Begin</c>, purpose <c>adp_step_up</c>) to redeem once for a grant.</summary>
	public class AdpFederatedStepUpInput
	{
		public string SsoTransactionId { get; set; }
		public string SsoCode { get; set; }
		public string CodeVerifier { get; set; }
	}

	/// <summary>
	/// Enrollment Wizard final-confirmation payload (ADP plan section 18.1 step 8). Everything here is
	/// re-validated server-side: caller must be the managing member, addon and global gate are
	/// re-checked, and the durable state must be Disabled.
	/// </summary>
	public class QueueEnrollmentInput
	{
		/// <summary>
		/// Versioned acknowledgement record (section 12 disclosure items), validated server-side against
		/// AdpEnrollmentAcknowledgements: <c>version</c> equal to Capabilities' AcknowledgementVersion,
		/// <c>acknowledgedItems</c> containing every AcknowledgementItems key, and <c>lockConsent</c> true.
		/// At most 64 KB.
		/// </summary>
		public string AcknowledgementsJson { get; set; }

		/// <summary>Department-local overnight window start, "HH:mm" (default 22:00).</summary>
		public string WindowStartLocal { get; set; }

		/// <summary>Department-local overnight window end, "HH:mm" (default 06:00).</summary>
		public string WindowEndLocal { get; set; }

		/// <summary>Time zone id the window is evaluated in.</summary>
		public string WindowTimeZone { get; set; }
	}
}
