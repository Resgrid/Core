using System;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// Who is asking, from the session that request validation just accepted (passkey plan section 6.1). Nothing here is
	/// taken from the client: a ceremony is bound to this user, session, client, generation and lock version.
	/// </summary>
	public sealed class PasskeyCaller
	{
		public string UserId { get; init; }

		/// <summary>The account name shown in the platform's passkey prompt.</summary>
		public string UserName { get; init; }

		public string SessionId { get; init; }

		/// <summary>Set instead of <see cref="SessionId"/> for a second factor during sign-in (plan section 7.2).</summary>
		public string LoginTransactionId { get; init; }

		/// <summary>
		/// For a Responder approval (plan section 7.9): the request the approver's assertion answers. The ceremony is bound
		/// to it; the approver's own session still identifies who approved.
		/// </summary>
		public string ApprovalRequestId { get; init; }

		public UserSessionClientApplication ClientApplication { get; init; }

		public long AuthenticationGeneration { get; init; }

		public long? SessionLockVersion { get; init; }

		public int? DepartmentId { get; init; }

		/// <summary>True for a shared-installation session: registration then asks for a roaming authenticator (plan section 6.5).</summary>
		public bool SharedMode { get; init; }

		public SystemAuditSystems AuditSystem { get; init; }

		public string IpAddress { get; init; }

		public string SessionKey => MfaEvidence.TrackedSessionKey(SessionId);

		/// <summary>
		/// What a ceremony's challenge is bound to: the login transaction during sign-in, the approval request for an
		/// approval, otherwise the session.
		/// </summary>
		public AuthenticationChallengeParentKind ChallengeParentKind =>
			!string.IsNullOrWhiteSpace(LoginTransactionId) ? AuthenticationChallengeParentKind.LoginTransaction
			: !string.IsNullOrWhiteSpace(ApprovalRequestId) ? AuthenticationChallengeParentKind.ApprovalRequest
			: AuthenticationChallengeParentKind.Session;

		public string ChallengeParentId =>
			!string.IsNullOrWhiteSpace(LoginTransactionId) ? LoginTransactionId
			: !string.IsNullOrWhiteSpace(ApprovalRequestId) ? ApprovalRequestId
			: string.IsNullOrWhiteSpace(SessionId) ? null : SessionId;

		/// <summary>The same approver, answering one approval request.</summary>
		public PasskeyCaller ForApprovalRequest(string approvalRequestId) => new()
		{
			UserId = UserId,
			UserName = UserName,
			SessionId = SessionId,
			ApprovalRequestId = approvalRequestId,
			ClientApplication = ClientApplication,
			AuthenticationGeneration = AuthenticationGeneration,
			SessionLockVersion = SessionLockVersion,
			DepartmentId = DepartmentId,
			SharedMode = SharedMode,
			AuditSystem = AuditSystem,
			IpAddress = IpAddress
		};

		/// <summary>The caller for a passkey second factor inside a login transaction; there is no session yet.</summary>
		public static PasskeyCaller ForLoginTransaction(MfaLoginTransaction transaction, string userName, SystemAuditSystems auditSystem, string ipAddress) =>
			transaction == null
				? null
				: new PasskeyCaller
				{
					UserId = transaction.UserId,
					UserName = userName,
					LoginTransactionId = transaction.MfaLoginTransactionId,
					ClientApplication = (UserSessionClientApplication)transaction.ClientApplication,
					AuthenticationGeneration = transaction.AuthenticationGeneration,
					DepartmentId = transaction.DepartmentId,
					AuditSystem = auditSystem,
					IpAddress = ipAddress
				};

		/// <summary>The caller for a validated session; null when the request has no tracked session.</summary>
		public static PasskeyCaller From(ProtectedGrantSessionContext session, string userId, string userName, int? departmentId,
			SystemAuditSystems auditSystem, string ipAddress) =>
			session == null || string.IsNullOrWhiteSpace(session.SessionId) || string.IsNullOrWhiteSpace(userId)
				? null
				: new PasskeyCaller
				{
					UserId = userId,
					UserName = userName,
					SessionId = session.SessionId,
					ClientApplication = (UserSessionClientApplication)session.ClientApplication,
					AuthenticationGeneration = session.AuthenticationGeneration,
					SessionLockVersion = session.SessionLockVersion,
					DepartmentId = departmentId,
					SharedMode = session.SharedMode,
					AuditSystem = auditSystem,
					IpAddress = ipAddress
				};
	}

	public enum PasskeyOutcome
	{
		Succeeded = 0,

		/// <summary>Passkeys are off on this deployment, or the client has no relying party.</summary>
		Unavailable,

		/// <summary>The request has no tracked session to bind a ceremony or evidence to.</summary>
		SessionRequired,

		/// <summary>No password or SSO verification for this session within the window.</summary>
		ReauthenticationRequired,

		/// <summary>No accepted second factor for this session within the window.</summary>
		StepUpRequired,

		/// <summary>TOTP and usable recovery codes must be set up before a passkey (plan section 6.1 item 2).</summary>
		EnrollmentRequired,

		LimitReached,
		TooManyRequests,
		ChallengeExpired,
		ChallengeConsumed,
		TooManyAttempts,
		VerificationFailed,

		/// <summary>The user has no usable passkey bound to the calling client.</summary>
		NotRegisteredForClient,

		/// <summary>No active passkey with that id belongs to the caller.</summary>
		NotFound,

		InvalidRequest,
		ServiceUnavailable
	}

	/// <summary>The machine-readable codes of the shared error vocabulary (workbook section 7.6) for each outcome.</summary>
	public static class PasskeyOutcomes
	{
		public static string ErrorCode(PasskeyOutcome outcome) => outcome switch
		{
			PasskeyOutcome.Unavailable => "passkeys_unavailable",
			PasskeyOutcome.SessionRequired => "session_required",
			PasskeyOutcome.ReauthenticationRequired => "reauthentication_required",
			PasskeyOutcome.StepUpRequired => "step_up_required",
			PasskeyOutcome.EnrollmentRequired => "mfa_enrollment_required",
			PasskeyOutcome.LimitReached => "passkey_limit_reached",
			PasskeyOutcome.TooManyRequests => "too_many_attempts",
			PasskeyOutcome.ChallengeExpired => "challenge_expired",
			PasskeyOutcome.ChallengeConsumed => "challenge_consumed",
			PasskeyOutcome.TooManyAttempts => "too_many_attempts",
			PasskeyOutcome.VerificationFailed => "passkey_verification_failed",
			PasskeyOutcome.NotRegisteredForClient => "passkey_not_registered_for_client",
			PasskeyOutcome.NotFound => "passkey_not_found",
			PasskeyOutcome.InvalidRequest => "invalid_request",
			PasskeyOutcome.ServiceUnavailable => "service_unavailable",
			_ => null
		};
	}

	/// <summary>A started ceremony: the opaque request id and the WebAuthn options JSON for the client.</summary>
	public sealed class PasskeyCeremonyStart
	{
		public PasskeyOutcome Outcome { get; init; }
		public string RequestId { get; init; }
		public string OptionsJson { get; init; }

		public bool Succeeded => Outcome == PasskeyOutcome.Succeeded;

		public static PasskeyCeremonyStart Of(PasskeyOutcome outcome) => new() { Outcome = outcome };
	}

	public sealed class PasskeyRegistrationResult
	{
		public PasskeyOutcome Outcome { get; init; }
		public UserPasskey Passkey { get; init; }

		public bool Succeeded => Outcome == PasskeyOutcome.Succeeded;

		public static PasskeyRegistrationResult Of(PasskeyOutcome outcome) => new() { Outcome = outcome };
	}

	/// <summary>A verified assertion: which passkey, and when. The caller records it as evidence for its session.</summary>
	public sealed class PasskeyAssertionResult
	{
		public PasskeyOutcome Outcome { get; init; }
		public UserPasskey Passkey { get; init; }
		public DateTime VerifiedOnUtc { get; init; }

		public bool Succeeded => Outcome == PasskeyOutcome.Succeeded;

		public static PasskeyAssertionResult Of(PasskeyOutcome outcome) => new() { Outcome = outcome };
	}

	/// <summary>
	/// What a removal did (plan sections 6.1 item 8 and 6.4): how many passkeys, and how many sessions that signed in with
	/// them were ended, including whether the caller's own session was one.
	/// </summary>
	public sealed class PasskeyRevocationResult
	{
		public PasskeyOutcome Outcome { get; init; }
		public int Revoked { get; init; }
		public int SessionsEnded { get; init; }
		public bool CurrentSessionEnded { get; init; }

		public static PasskeyRevocationResult Of(PasskeyOutcome outcome) => new() { Outcome = outcome };
	}
}
