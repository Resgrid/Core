using System;

namespace Resgrid.Model.Security
{
	public enum SharedSessionOutcome
	{
		Succeeded = 0,

		/// <summary>The session is personal; lock, unlock and end shift apply only to shared sessions.</summary>
		NotShared,

		/// <summary>Unlock was asked for a session that is not locked.</summary>
		NotLocked,

		/// <summary>The lock version is not the one the caller expected: it locked again, or another unlock won.</summary>
		LockChanged,

		/// <summary>The session ended, expired, or belongs to someone else. The operator signs in normally.</summary>
		SessionEnded,

		/// <summary>The department now requires SSO and this session began with a password: sign in again with SSO.</summary>
		SsoReauthenticationRequired,

		ServiceUnavailable
	}

	/// <summary>Who is asking, for the audit; the operator is always the session's own user.</summary>
	public sealed class SharedSessionRequestInfo
	{
		public string UserName { get; init; }
		public string IpAddress { get; init; }
		public string CorrelationId { get; init; }

		/// <summary>The host the operator used: the API for the apps, the Website for Core Web.</summary>
		public SystemAuditSystems AuditSystem { get; init; } = SystemAuditSystems.Api;
	}

	/// <summary>A shared session's state as its locked or unlocked client may read it (plan section 12.5.5).</summary>
	public sealed class SharedSessionStatus
	{
		public bool Shared { get; init; }
		public bool Locked { get; init; }
		public long LockVersion { get; init; }
		public SharedSessionLockReason? LockReason { get; init; }
		public int IdleLockMinutes { get; init; }

		/// <summary>When it locks unless the operator does something first; null while locked or personal.</summary>
		public DateTime? IdleLocksOnUtc { get; init; }

		/// <summary>When the shift ends whatever the activity; null for a personal session.</summary>
		public DateTime? ShiftEndsOnUtc { get; init; }

		public UserSessionClientApplication ClientApplication { get; init; }
		public string InstallationLabel { get; init; }
	}

	public sealed class SharedSessionTransition
	{
		public SharedSessionOutcome Outcome { get; init; }

		/// <summary>The session's lock version after the transition.</summary>
		public long LockVersion { get; init; }

		public bool Succeeded => Outcome == SharedSessionOutcome.Succeeded;

		public static SharedSessionTransition Of(SharedSessionOutcome outcome, long lockVersion = 0) => new() { Outcome = outcome, LockVersion = lockVersion };
	}

	public static class SharedSessionOutcomes
	{
		/// <summary>The value-free error code for an outcome (workbook section 7.6); null on success.</summary>
		public static string ErrorCode(SharedSessionOutcome outcome) => outcome switch
		{
			SharedSessionOutcome.Succeeded => null,
			SharedSessionOutcome.NotShared => "not_shared_session",
			SharedSessionOutcome.NotLocked => "shared_session_not_locked",
			SharedSessionOutcome.LockChanged => "shared_session_lock_changed",
			SharedSessionOutcome.SessionEnded => "session_revoked",
			SharedSessionOutcome.SsoReauthenticationRequired => "sso_reauthentication_required",
			_ => "service_unavailable"
		};
	}
}
