using Newtonsoft.Json.Linq;

namespace Resgrid.Web.Services.Models.v4.MfaApproval
{
	/// <summary>
	/// Asks the user's Responder to approve (workbook section 7.4). Send only after the user chose "Approve with Responder";
	/// never automatically.
	/// </summary>
	public class MfaApprovalRequestInput
	{
		/// <summary><c>login</c> (with <see cref="Transaction"/>) or <c>step_up</c> (the signed-in session, with <see cref="Operation"/>).</summary>
		public string Purpose { get; set; }

		/// <summary>For <c>step_up</c>: <c>adp_management</c> or <c>chat_export</c>. Security changes and account factors never accept approval.</summary>
		public string Operation { get; set; }

		/// <summary>For <c>login</c>: the login MFA transaction secret.</summary>
		public string Transaction { get; set; }
	}

	public class MfaApprovalRequestResult : StandardApiResponseV4Base
	{
		public MfaApprovalRequestResultData Data { get; set; }
	}

	public class MfaApprovalRequestResultData
	{
		public string ApprovalRequestId { get; set; }

		/// <summary>Show this on the requesting screen only; the user types it in Responder. It is never pushed.</summary>
		public string MatchNumber { get; set; }

		public int ExpiresIn { get; set; }
	}

	/// <summary>A request the caller made: by its login transaction secret, or by the signed-in session when absent.</summary>
	public class MfaApprovalReferenceInput
	{
		public string ApprovalRequestId { get; set; }

		public string Transaction { get; set; }
	}

	public class MfaApprovalStatusResult : StandardApiResponseV4Base
	{
		public MfaApprovalStatusResultData Data { get; set; }
	}

	public class MfaApprovalStatusResultData
	{
		/// <summary><c>pending</c>, <c>approved</c>, <c>denied</c>, <c>expired</c>, <c>canceled</c> or <c>consumed</c>. Poll every 2 seconds.</summary>
		public string State { get; set; }

		public string ExpiresAt { get; set; }
	}

	public class MfaApprovalPendingResult : StandardApiResponseV4Base
	{
		/// <summary>Null when nothing is waiting.</summary>
		public MfaApprovalPendingResultData Data { get; set; }
	}

	/// <summary>What Responder shows before Approve and Deny. The number is not here: the user reads it from the requesting screen.</summary>
	public class MfaApprovalPendingResultData
	{
		public string ApprovalRequestId { get; set; }

		/// <summary>web, unit, dispatch or ic.</summary>
		public string RequestingApp { get; set; }

		public string InstallationLabel { get; set; }

		public bool Shared { get; set; }

		public string Department { get; set; }

		/// <summary><c>login</c> or <c>step_up</c>.</summary>
		public string Purpose { get; set; }

		public string Operation { get; set; }

		/// <summary>Region and country only.</summary>
		public string OriginRegion { get; set; }

		public string CreatedAt { get; set; }

		public string ExpiresAt { get; set; }

		public int AttemptsRemaining { get; set; }
	}

	public class MfaApprovalOptionsInput
	{
		public string ApprovalRequestId { get; set; }
	}

	public class MfaApprovalApproveInput
	{
		public string ApprovalRequestId { get; set; }

		/// <summary>The two digits the user read from the requesting screen.</summary>
		public string MatchNumber { get; set; }

		/// <summary>The request id from <c>MfaApproval/Options</c>.</summary>
		public string RequestId { get; set; }

		/// <summary>The Responder passkey assertion (PublicKeyCredential.toJSON()), as an object or a JSON string.</summary>
		public JToken Credential { get; set; }
	}

	public class DisableApprovalInstallationsInput
	{
		/// <summary>The installation to stop, from <c>AccountSecurity/Methods</c>. Leave empty with <see cref="All"/>.</summary>
		public string InstallationId { get; set; }

		/// <summary>Stop every installation and turn approval off on every Responder passkey.</summary>
		public bool All { get; set; }
	}

	public class DisableApprovalInstallationsResult : StandardApiResponseV4Base
	{
		public DisableApprovalInstallationsResultData Data { get; set; }
	}

	public class DisableApprovalInstallationsResultData
	{
		/// <summary>Zero when nothing was taking requests (already stopped, or not this account's installation).</summary>
		public int InstallationsStopped { get; set; }

		public int PasskeysStopped { get; set; }
	}

	public class MfaApprovalDenyInput
	{
		public string ApprovalRequestId { get; set; }

		/// <summary><c>declined</c>, or <c>not_me</c> ("I didn't request this": ends that sign-in and suspends approval requests).</summary>
		public string Reason { get; set; }
	}

	public class MfaApprovalDecisionResult : StandardApiResponseV4Base
	{
		public MfaApprovalDecisionResultData Data { get; set; }
	}

	public class MfaApprovalDecisionResultData
	{
		public string State { get; set; }
	}
}
