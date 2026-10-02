using System;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// Who is asking for a Protected Data Grant (passkey plan sections 8.1 and 9): the authenticated caller in one department,
	/// with the session its validation just loaded and checked. Nothing here comes from the client.
	/// </summary>
	public sealed class AdpStepUpCaller
	{
		public string UserId { get; init; }
		public string UserName { get; init; }
		public int DepartmentId { get; init; }

		/// <summary>
		/// The validated session. Null for an untracked legacy credential, which can obtain only a version 1 grant after a
		/// fresh authenticator code; every other method is bound to a tracked session.
		/// </summary>
		public ProtectedGrantSessionContext Session { get; init; }

		/// <summary>The sid claim of an untracked credential, carried into a version 1 grant only.</summary>
		public string LegacySessionId { get; init; }

		/// <summary>The calling client when there is no validated session (version 1 only).</summary>
		public UserSessionClientApplication ClientApplication { get; init; }

		/// <summary>The account's current authentication generation, for the evidence of an untracked credential.</summary>
		public long AccountAuthenticationGeneration { get; init; }

		/// <summary>
		/// Where the verification is recorded as evidence. Defaults to the tracked session; Web passes its own key, which also
		/// covers an untracked Web session.
		/// </summary>
		public string EvidenceSessionKey { get; init; }

		/// <summary>The evidence key actually used: the explicit one, or the tracked session's.</summary>
		public string EvidenceKey => EvidenceSessionKey ?? MfaEvidence.TrackedSessionKey(Session?.SessionId);

		public string IpAddress { get; init; }
		public SystemAuditSystems AuditSystem { get; init; }

		/// <summary>The client the grant is for: the validated session's, or the caller's own for a version 1 grant.</summary>
		public UserSessionClientApplication Client =>
			Session != null ? (UserSessionClientApplication)Session.ClientApplication : ClientApplication;

		/// <summary>The passkey ceremony caller for this request: the same session, client and department.</summary>
		public PasskeyCaller ToPasskeyCaller() => new()
		{
			UserId = UserId,
			UserName = UserName,
			SessionId = Session?.SessionId,
			ClientApplication = Client,
			AuthenticationGeneration = Session?.AuthenticationGeneration ?? 0,
			SessionLockVersion = Session?.SessionLockVersion,
			DepartmentId = DepartmentId,
			SharedMode = Session?.SharedMode == true,
			AuditSystem = AuditSystem,
			IpAddress = IpAddress
		};
	}

	public enum AdpGrantOutcome
	{
		Issued = 0,

		/// <summary>No signing material on this host: the operation is unavailable, never a token-less success (plan section 8.1).</summary>
		NotConfigured,

		/// <summary>No tracked session: a passkey, approval or provider step-up grant is always bound to one.</summary>
		SessionRequired,

		/// <summary>The department, the deployment or this client does not accept the method for protected data now.</summary>
		MethodNotAllowed,

		/// <summary>The verification is too old for the department's window, or the session ends first: verify again.</summary>
		StepUpRequired,

		/// <summary>The factor did not verify.</summary>
		VerificationFailed,

		/// <summary>The approval has not been decided yet.</summary>
		ApprovalPending,

		/// <summary>The approval was denied, expired, already used, or was for something else.</summary>
		ApprovalUnavailable,

		/// <summary>The credential behind the verification was revoked or changed while it was being used.</summary>
		CredentialRevoked,

		InvalidRequest,

		ServiceUnavailable
	}

	/// <summary>A grant issued from a verification, or why none was (value-free).</summary>
	public sealed class AdpGrantIssue
	{
		public AdpGrantOutcome Outcome { get; init; }
		public string GrantId { get; init; }
		public string Token { get; init; }

		/// <summary>The grant's absolute expiry (plan section 9.2). Clients conceal at this time; they never add the window themselves.</summary>
		public DateTime ExpiresOnUtc { get; init; }

		/// <summary>The department's configured window, for display only.</summary>
		public int WindowMinutes { get; init; }

		/// <summary>A more specific value-free code than the outcome's, when there is one (a passkey or approval outcome).</summary>
		public string Error { get; init; }

		public bool Succeeded => Outcome == AdpGrantOutcome.Issued;

		public string ErrorCode => Succeeded ? null : Error ?? AdpGrantOutcomes.ErrorCode(Outcome);

		public static AdpGrantIssue Of(AdpGrantOutcome outcome, string error = null) => new() { Outcome = outcome, Error = error };
	}

	public static class AdpGrantOutcomes
	{
		public static string ErrorCode(AdpGrantOutcome outcome) => outcome switch
		{
			AdpGrantOutcome.Issued => null,
			AdpGrantOutcome.NotConfigured => "grants_not_configured",
			AdpGrantOutcome.SessionRequired => "session_required",
			AdpGrantOutcome.MethodNotAllowed => "mfa_method_not_allowed",
			AdpGrantOutcome.StepUpRequired => "step_up_required",
			AdpGrantOutcome.VerificationFailed => "mfa_verification_failed",
			AdpGrantOutcome.ApprovalPending => "approval_pending",
			AdpGrantOutcome.ApprovalUnavailable => "approval_expired",
			AdpGrantOutcome.CredentialRevoked => "grant_revoked",
			AdpGrantOutcome.InvalidRequest => "invalid_request",
			_ => "service_unavailable"
		};

		/// <summary>The HTTP status an API or Web JSON endpoint answers with.</summary>
		public static int StatusFor(AdpGrantOutcome outcome) => outcome switch
		{
			AdpGrantOutcome.Issued => 200,
			AdpGrantOutcome.NotConfigured or AdpGrantOutcome.ServiceUnavailable => 503,
			AdpGrantOutcome.SessionRequired or AdpGrantOutcome.ApprovalPending => 409,
			AdpGrantOutcome.StepUpRequired or AdpGrantOutcome.VerificationFailed or AdpGrantOutcome.CredentialRevoked => 401,
			_ => 400
		};
	}

	/// <summary>
	/// The credential or evidence behind a verification, as a version 2 grant names it (plan section 8.2): an opaque
	/// reference and the state version it had. Revoking or changing the credential changes one or the other.
	/// </summary>
	public sealed class MfaCredentialSnapshot
	{
		public string CredentialId { get; init; }
		public long StateVersion { get; init; }
	}
}
