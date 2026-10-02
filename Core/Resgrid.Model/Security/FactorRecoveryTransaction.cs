using System;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// A restricted factor recovery (passkey plan sections 5.4 and 6.3): a verified first factor and a spent recovery code
	/// open it; it permits only status, replacement-factor setup, completion and cancellation, and grants no ordinary or ADP
	/// access. Completion replaces the authenticator, rotates the recovery codes, removes the lost passkeys the user chose,
	/// and ends every session. The row is the authority and every change is one compare-and-set; only a SHA-256 hash of its
	/// secret is stored.
	/// </summary>
	public class FactorRecoveryTransaction
	{
		public string FactorRecoveryTransactionId { get; set; }

		public byte[] SecretHash { get; set; }

		public string UserId { get; set; }

		/// <summary>The client that started it; only that client may continue it.</summary>
		public int ClientApplication { get; set; }

		/// <summary>The first factor that opened it: <see cref="MfaEvidenceMethod.Password"/> or <see cref="MfaEvidenceMethod.Sso"/>.</summary>
		public int FirstFactorMethod { get; set; }

		public DateTime FirstFactorVerifiedOnUtc { get; set; }

		/// <summary>For an SSO first factor, the configuration that verified it.</summary>
		public string DepartmentSsoConfigId { get; set; }

		public int? DepartmentId { get; set; }

		/// <summary>The account's generation when it opened; any change voids it.</summary>
		public long AuthenticationGeneration { get; set; }

		public DateTime CreatedOnUtc { get; set; }

		/// <summary>Ten minutes after creation, never extended.</summary>
		public DateTime ExpiresOnUtc { get; set; }

		public int Attempts { get; set; }

		public int MaxAttempts { get; set; }

		public int State { get; set; }

		public DateTime? CompletedOnUtc { get; set; }

		public FactorRecoveryState RecoveryState => (FactorRecoveryState)State;
	}

	public enum FactorRecoveryState
	{
		Pending = 0,

		/// <summary>The replacement committed. Terminal: the user signs in normally.</summary>
		Completed = 1,

		/// <summary>The user canceled. Terminal; the recovery code that opened it stays spent.</summary>
		Canceled = 2,

		/// <summary>Too many wrong codes for the new authenticator. Terminal.</summary>
		Exhausted = 3
	}

	public enum FactorRecoveryOutcome
	{
		Usable = 0,
		Invalid,
		Expired,
		AlreadyUsed,
		TooManyAttempts,

		/// <summary>The account's sign-in state changed since the recovery began.</summary>
		SessionRevoked,

		Unavailable
	}

	public static class FactorRecoveryOutcomes
	{
		public static string ErrorCode(FactorRecoveryOutcome outcome) => outcome switch
		{
			FactorRecoveryOutcome.Invalid => "recovery_transaction_invalid",
			FactorRecoveryOutcome.Expired => "recovery_transaction_expired",
			FactorRecoveryOutcome.AlreadyUsed => "recovery_transaction_invalid",
			FactorRecoveryOutcome.TooManyAttempts => "too_many_attempts",
			FactorRecoveryOutcome.SessionRevoked => "session_revoked",
			FactorRecoveryOutcome.Unavailable => "service_unavailable",
			_ => null
		};
	}

	public sealed class FactorRecoveryStart
	{
		public string Secret { get; init; }
		public int ExpiresInSeconds { get; init; }
		public FactorRecoveryTransaction Transaction { get; init; }
	}

	public sealed class FactorRecoveryResult
	{
		public FactorRecoveryOutcome Outcome { get; init; }
		public FactorRecoveryTransaction Transaction { get; init; }

		public bool IsUsable => Outcome == FactorRecoveryOutcome.Usable;

		public static FactorRecoveryResult Of(FactorRecoveryOutcome outcome, FactorRecoveryTransaction transaction = null) =>
			new() { Outcome = outcome, Transaction = transaction };
	}
}
