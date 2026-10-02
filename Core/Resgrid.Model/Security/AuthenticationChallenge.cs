using System;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// A single-use WebAuthn ceremony challenge (passkey plan section 5.2). The database row is the authority: a challenge
	/// is spent by one compare-and-set, never by a cache GET then DELETE, and nothing revives a spent challenge. It belongs
	/// to exactly one user, purpose, client, parent (session or transaction) and authentication generation.
	/// </summary>
	public class AuthenticationChallenge
	{
		/// <summary>Opaque request id handed to the client; knowing it alone cannot complete anything.</summary>
		public string AuthenticationChallengeId { get; set; }

		public string UserId { get; set; }

		public int Purpose { get; set; }

		public int ClientApplication { get; set; }

		public string RpId { get; set; }

		public int ParentKind { get; set; }

		public string ParentId { get; set; }

		public int? DepartmentId { get; set; }

		public long AuthenticationGeneration { get; set; }

		/// <summary>Shared-session lock version the challenge was issued at, when the parent is a shared session.</summary>
		public long? LockVersion { get; set; }

		/// <summary>The exact server options (including the random challenge) the ceremony must be verified against.</summary>
		public string OptionsJson { get; set; }

		public DateTime CreatedOnUtc { get; set; }

		public DateTime ExpiresOnUtc { get; set; }

		public int Attempts { get; set; }

		public int MaxAttempts { get; set; }

		public int State { get; set; }

		public DateTime? ConsumedOnUtc { get; set; }

		public AuthenticationChallengePurpose ChallengePurpose => (AuthenticationChallengePurpose)Purpose;

		public AuthenticationChallengeState ChallengeState => (AuthenticationChallengeState)State;
	}

	public enum AuthenticationChallengePurpose
	{
		PasskeyRegistration = 1,
		LoginSecondFactor = 2,
		AdpStepUp = 3,
		SensitiveOperation = 4,
		AccountReauthentication = 5,
		SharedDeviceUnlock = 6,
		ApprovalResponse = 7
	}

	public enum AuthenticationChallengeParentKind
	{
		Session = 1,
		LoginTransaction = 2,
		RecoveryTransaction = 3,
		ApprovalRequest = 4
	}

	public enum AuthenticationChallengeState
	{
		Pending = 0,
		Consumed = 1,
		/// <summary>Too many failed verifications; the ceremony must restart.</summary>
		Exhausted = 2,
		Canceled = 3
	}

	/// <summary>What the caller must match for a challenge to be usable: everything it was issued to.</summary>
	public sealed class AuthenticationChallengeBinding
	{
		public string UserId { get; set; }
		public AuthenticationChallengePurpose Purpose { get; set; }
		public UserSessionClientApplication ClientApplication { get; set; }
		public AuthenticationChallengeParentKind ParentKind { get; set; }
		public string ParentId { get; set; }
		public int? DepartmentId { get; set; }
		public long AuthenticationGeneration { get; set; }
		public long? LockVersion { get; set; }
	}

	public enum AuthenticationChallengeOutcome
	{
		Usable = 0,
		NotFound = 1,
		Expired = 2,
		AlreadyUsed = 3,
		BindingMismatch = 4,
		/// <summary>The account's authentication generation moved (password change, revocation) since it was issued.</summary>
		Stale = 5,
		TooManyAttempts = 6,
		Unavailable = 7
	}

	public sealed class AuthenticationChallengeResult
	{
		public AuthenticationChallengeOutcome Outcome { get; init; }
		public AuthenticationChallenge Challenge { get; init; }

		public bool IsUsable => Outcome == AuthenticationChallengeOutcome.Usable;

		public static AuthenticationChallengeResult Of(AuthenticationChallengeOutcome outcome, AuthenticationChallenge challenge = null)
			=> new() { Outcome = outcome, Challenge = challenge };
	}
}
