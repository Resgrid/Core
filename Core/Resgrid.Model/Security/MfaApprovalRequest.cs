using System;
using System.Security.Cryptography;
using System.Text;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// A Responder approval request (passkey plan sections 5.6 and 7.9): the user approves another app's sign-in or step-up
	/// from their own Responder with Responder's passkey, after typing the number shown on the requesting screen. The row
	/// is the authority and every change is one compare-and-set; the number is stored only as a hash and never pushed.
	/// </summary>
	public class MfaApprovalRequest
	{
		/// <summary>The public <c>approval_request_id</c>. Knowing it approves nothing.</summary>
		public string MfaApprovalRequestId { get; set; }

		public string UserId { get; set; }

		// ── The requesting context ────────────────────────────────────────────────

		/// <summary><see cref="MfaApprovalRequesterKind"/>: a login transaction or a signed-in session.</summary>
		public int RequesterKind { get; set; }

		/// <summary>The login transaction's row id or the session id; only that requester can read or consume the result.</summary>
		public string RequesterId { get; set; }

		/// <summary>The requesting <see cref="UserSessionClientApplication"/>; never Responder.</summary>
		public int ClientApplication { get; set; }

		/// <summary>The requesting installation's label from the server's session record, shown to the approver.</summary>
		public string InstallationLabel { get; set; }

		public bool SharedMode { get; set; }

		public int? DepartmentId { get; set; }

		/// <summary><see cref="MfaApprovalPurpose"/>.</summary>
		public int Purpose { get; set; }

		/// <summary>For a step-up: the operation (<see cref="MfaStepUpOperations"/>).</summary>
		public string Operation { get; set; }

		/// <summary>For a shared session: the lock version it must still have when completed (slice 13).</summary>
		public long? LockVersion { get; set; }

		/// <summary>The account's authentication generation when requested; any change voids the request.</summary>
		public long AuthenticationGeneration { get; set; }

		/// <summary>SHA-256 of the request id and the two-digit number; the number itself is never stored or pushed.</summary>
		public byte[] MatchNumberHash { get; set; }

		/// <summary>Coarse, server-observed origin (region and country only), for display.</summary>
		public string OriginRegion { get; set; }

		// ── Lifecycle ─────────────────────────────────────────────────────────────

		public int State { get; set; }

		/// <summary>Advanced by every change, so each transition is one compare-and-set.</summary>
		public long Version { get; set; }

		/// <summary>Wrong numbers entered; reaching <see cref="MaxAttempts"/> denies the request.</summary>
		public int Attempts { get; set; }

		public int MaxAttempts { get; set; }

		public DateTime CreatedOnUtc { get; set; }

		/// <summary>Two minutes after creation, never extended.</summary>
		public DateTime ExpiresOnUtc { get; set; }

		public DateTime? DecidedOnUtc { get; set; }

		public DateTime? ConsumedOnUtc { get; set; }

		/// <summary><see cref="MfaApprovalEndReason"/> for a denied or canceled request.</summary>
		public int? EndReason { get; set; }

		// ── The approver ──────────────────────────────────────────────────────────

		public string ApproverSessionId { get; set; }

		public string ApproverPasskeyId { get; set; }

		public MfaApprovalRequestState RequestState => (MfaApprovalRequestState)State;
		public MfaApprovalPurpose RequestPurpose => (MfaApprovalPurpose)Purpose;
		public MfaApprovalRequesterKind Requester => (MfaApprovalRequesterKind)RequesterKind;

		/// <summary>The state as it reads now: a pending request past its expiry is expired, whether or not a row says so yet.</summary>
		public MfaApprovalRequestState EffectiveState(DateTime utcNow) =>
			RequestState == MfaApprovalRequestState.Pending && ExpiresOnUtc <= utcNow ? MfaApprovalRequestState.Expired : RequestState;

		/// <summary>
		/// The evidence factor reference for an approval: the approving Responder passkey and session. Evidence and sign-ins
		/// with it stop counting when either is revoked or approval is turned off (plan section 7.9 revocation).
		/// </summary>
		public static string FactorReferenceFor(string approverPasskeyId, string approverSessionId) =>
			$"{FactorReferencePrefix}{approverPasskeyId}:{approverSessionId}";

		public const string FactorReferencePrefix = "approval:";

		/// <summary>The prefix every approval by one passkey carries, whichever Responder session approved.</summary>
		public static string FactorReferencePrefixFor(string approverPasskeyId) => $"{FactorReferencePrefix}{approverPasskeyId}:";

		public static bool TryParseFactorReference(string factorReference, out string approverPasskeyId, out string approverSessionId)
		{
			approverPasskeyId = approverSessionId = null;
			if (string.IsNullOrWhiteSpace(factorReference) || !factorReference.StartsWith(FactorReferencePrefix, StringComparison.Ordinal))
				return false;

			var parts = factorReference[FactorReferencePrefix.Length..].Split(':');
			if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
				return false;

			approverPasskeyId = parts[0];
			approverSessionId = parts[1];
			return true;
		}

		/// <summary>The stored form of a match number: bound to its request, compared in constant time.</summary>
		public static byte[] HashMatchNumber(string approvalRequestId, string matchNumber) =>
			SHA256.HashData(Encoding.UTF8.GetBytes($"mfa-approval:{approvalRequestId}:{matchNumber?.Trim()}"));
	}

	public enum MfaApprovalRequesterKind
	{
		LoginTransaction = 1,
		Session = 2
	}

	public enum MfaApprovalPurpose
	{
		Login = 1,
		StepUp = 2,

		/// <summary>Protected Data Grants; the approval grant issuer arrives in Phase 2.</summary>
		Adp = 3,

		/// <summary>Shared-session unlock (slice 13).</summary>
		Unlock = 4,

		/// <summary>Web department entry (Phase 3).</summary>
		DepartmentEntry = 5
	}

	public enum MfaApprovalRequestState
	{
		Pending = 0,
		Approved = 1,
		Denied = 2,
		Expired = 3,
		Canceled = 4,

		/// <summary>The requester used the approval once. Terminal.</summary>
		Consumed = 5
	}

	public enum MfaApprovalEndReason
	{
		/// <summary>The approver pressed Deny.</summary>
		Declined = 1,

		/// <summary>"Deny — I didn't request this": also ends the requester's sign-in and suspends approval requests.</summary>
		NotMe = 2,

		/// <summary>Too many wrong numbers.</summary>
		TooManyAttempts = 3,

		/// <summary>The requester canceled it.</summary>
		CanceledByRequester = 4,

		/// <summary>A newer request for the same user replaced it.</summary>
		Superseded = 5,

		/// <summary>The approving passkey was revoked, or approval was turned off.</summary>
		ApproverRevoked = 6
	}

	public enum MfaApprovalOutcome
	{
		Succeeded = 0,

		/// <summary>Approval is off, not allowed here, or the user has no eligible Responder.</summary>
		Unavailable,

		/// <summary>Two denials or expiries in a row, or a "not me" denial, within 15 minutes.</summary>
		Suspended,

		/// <summary>More than the per-user request limit, or a concurrent request won.</summary>
		TooManyRequests,

		/// <summary>No such request for this caller (or it belongs to another requester or account state).</summary>
		NotFound,

		Pending,
		Denied,
		Expired,

		/// <summary>The number typed in Responder is not the one on the requesting screen.</summary>
		NumberMismatch,

		/// <summary>The request needs a Responder session (approver) or a requesting session or transaction.</summary>
		SessionRequired,

		InvalidRequest,
		ServiceUnavailable
	}

	public static class MfaApprovalOutcomes
	{
		public static string ErrorCode(MfaApprovalOutcome outcome) => outcome switch
		{
			MfaApprovalOutcome.Unavailable => "approval_unavailable",
			MfaApprovalOutcome.Suspended => "approval_suspended",
			MfaApprovalOutcome.TooManyRequests => "too_many_attempts",
			MfaApprovalOutcome.NotFound => "approval_expired",
			MfaApprovalOutcome.Pending => "approval_pending",
			MfaApprovalOutcome.Denied => "approval_denied",
			MfaApprovalOutcome.Expired => "approval_expired",
			MfaApprovalOutcome.NumberMismatch => "approval_number_mismatch",
			MfaApprovalOutcome.SessionRequired => "session_required",
			MfaApprovalOutcome.InvalidRequest => "invalid_request",
			MfaApprovalOutcome.ServiceUnavailable => "service_unavailable",
			_ => null
		};

		/// <summary>The wire state names (workbook section 7.4).</summary>
		public static string StateName(MfaApprovalRequestState state) => state switch
		{
			MfaApprovalRequestState.Pending => "pending",
			MfaApprovalRequestState.Approved => "approved",
			MfaApprovalRequestState.Denied => "denied",
			MfaApprovalRequestState.Expired => "expired",
			MfaApprovalRequestState.Canceled => "canceled",
			MfaApprovalRequestState.Consumed => "consumed",
			_ => null
		};

		public static string PurposeName(MfaApprovalPurpose purpose) => purpose switch
		{
			MfaApprovalPurpose.Login => "login",
			MfaApprovalPurpose.StepUp => "step_up",
			MfaApprovalPurpose.Adp => "adp",
			MfaApprovalPurpose.Unlock => "unlock",
			MfaApprovalPurpose.DepartmentEntry => "department_entry",
			_ => null
		};
	}

	/// <summary>Who is asking for approval, from the validated login transaction or session; nothing here comes from the client.</summary>
	public sealed class MfaApprovalRequester
	{
		public string UserId { get; init; }
		public MfaApprovalRequesterKind Kind { get; init; }
		public string RequesterId { get; init; }
		public UserSessionClientApplication ClientApplication { get; init; }
		public long AuthenticationGeneration { get; init; }
		public int? DepartmentId { get; init; }
		public MfaApprovalPurpose Purpose { get; init; }
		public string Operation { get; init; }
		public string InstallationLabel { get; init; }
		public bool SharedMode { get; init; }
		public long? LockVersion { get; init; }
		public string IpAddress { get; init; }
		public string UserName { get; init; }
		public SystemAuditSystems AuditSystem { get; init; }

		/// <summary>The department switches that decide whether approval counts here (plan section 7.6).</summary>
		public MfaMethodScope Scope => Purpose == MfaApprovalPurpose.StepUp ? MfaStepUpOperations.ScopeFor(Operation) : Purpose == MfaApprovalPurpose.Adp
			? MfaMethodScope.Adp
			: MfaMethodScope.Login;

		public static MfaApprovalRequester ForLoginTransaction(MfaLoginTransaction transaction, string userName, string ipAddress,
			SystemAuditSystems auditSystem = SystemAuditSystems.Api) =>
			new()
			{
				UserId = transaction.UserId,
				Kind = MfaApprovalRequesterKind.LoginTransaction,
				RequesterId = transaction.MfaLoginTransactionId,
				ClientApplication = (UserSessionClientApplication)transaction.ClientApplication,
				AuthenticationGeneration = transaction.AuthenticationGeneration,
				DepartmentId = transaction.DepartmentId,
				Purpose = MfaApprovalPurpose.Login,
				// What the first factor recorded: the approver sees a shared workstation's sign-in as one, with its label.
				SharedMode = transaction.SharedMode,
				InstallationLabel = transaction.InstallationLabel,
				IpAddress = ipAddress,
				UserName = userName,
				AuditSystem = auditSystem
			};
	}

	/// <summary>A new request: its id and the number to show on the requesting screen only.</summary>
	public sealed class MfaApprovalStart
	{
		public MfaApprovalOutcome Outcome { get; init; }
		public string ApprovalRequestId { get; init; }
		public string MatchNumber { get; init; }
		public int ExpiresInSeconds { get; init; }

		public bool Succeeded => Outcome == MfaApprovalOutcome.Succeeded;

		public static MfaApprovalStart Of(MfaApprovalOutcome outcome) => new() { Outcome = outcome };
	}

	public sealed class MfaApprovalResult
	{
		public MfaApprovalOutcome Outcome { get; init; }
		public MfaApprovalRequest Request { get; init; }

		public bool Succeeded => Outcome == MfaApprovalOutcome.Succeeded;

		public static MfaApprovalResult Of(MfaApprovalOutcome outcome, MfaApprovalRequest request = null) => new() { Outcome = outcome, Request = request };
	}
}
