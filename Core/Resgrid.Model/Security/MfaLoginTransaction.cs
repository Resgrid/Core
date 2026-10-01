using System;
using System.Collections.Generic;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// A restricted login after a verified first factor (passkey plan section 5.2). It is not a session and authorizes
	/// nothing but completing its own second factor. The row is the authority: every state change is one compare-and-set,
	/// a completion is redeemed once, and nothing revives a spent transaction. Only SHA-256 hashes of the transaction
	/// secret and the completion code are stored; no password, code, assertion or token.
	/// </summary>
	public class MfaLoginTransaction
	{
		public string MfaLoginTransactionId { get; set; }

		/// <summary>SHA-256 of the high-entropy secret the client holds in memory.</summary>
		public byte[] SecretHash { get; set; }

		public string UserId { get; set; }

		/// <summary>The resolved login department, verified as an active membership before the transaction existed.</summary>
		public int? DepartmentId { get; set; }

		/// <summary>The <see cref="UserSessionClientApplication"/> that started it; completion and redemption must match.</summary>
		public int ClientApplication { get; set; }

		/// <summary>The OAuth client_id of the first-factor request, when one was sent.</summary>
		public string ClientId { get; set; }

		/// <summary>The first factor (<see cref="MfaEvidenceMethod.Password"/> or <see cref="MfaEvidenceMethod.Sso"/>).</summary>
		public int FirstFactorMethod { get; set; }

		/// <summary>When the first factor was verified; carried into the session's evidence unchanged.</summary>
		public DateTime FirstFactorVerifiedOnUtc { get; set; }

		public string DepartmentSsoConfigId { get; set; }

		public long AuthenticationGeneration { get; set; }

		/// <summary>The department's <c>MfaPolicyVersion</c> when the transaction began; a policy change voids it.</summary>
		public long MfaPolicyVersion { get; set; }

		/// <summary>Space-separated scopes granted by the first-factor request.</summary>
		public string Scopes { get; set; }

		/// <summary>
		/// Whether the session this sign-in creates will be shared (the department's requirement, or the installation's
		/// request while shared-device mode is on), decided at the first factor by the rule the session applies. What the
		/// member's Responder is shown when asked to approve; the session still decides for itself when it is created.
		/// </summary>
		public bool SharedMode { get; set; }

		/// <summary>The label the installation sent with the first factor (display text for the approver only; nothing trusts it).</summary>
		public string InstallationLabel { get; set; }

		public DateTime CreatedOnUtc { get; set; }

		public DateTime ExpiresOnUtc { get; set; }

		public int Attempts { get; set; }

		public int MaxAttempts { get; set; }

		public int State { get; set; }

		// Set by the one successful completion.
		public int? CompletionMethod { get; set; }
		public string CompletionFactorReference { get; set; }
		public DateTime? CompletionVerifiedOnUtc { get; set; }
		public bool IsRecovery { get; set; }
		public byte[] CompletionCodeHash { get; set; }
		public DateTime? CompletionExpiresOnUtc { get; set; }
		public DateTime? RedeemedOnUtc { get; set; }

		public MfaLoginTransactionState TransactionState => (MfaLoginTransactionState)State;
	}

	public enum MfaLoginTransactionState
	{
		Pending = 0,

		/// <summary>A second factor verified; a completion code was issued and awaits redemption.</summary>
		Completed = 1,

		/// <summary>The completion code was exchanged for tokens. Terminal.</summary>
		Redeemed = 2,

		/// <summary>Too many failed verifications. Terminal: the user signs in again.</summary>
		Exhausted = 3,

		Canceled = 4
	}

	/// <summary>What the client learns when a login needs a second factor (workbook section 7.1).</summary>
	public sealed class MfaLoginTransactionStart
	{
		/// <summary>The transaction secret, returned once; the server keeps only its hash.</summary>
		public string Secret { get; init; }

		public int ExpiresInSeconds { get; init; }

		public MfaMethodChoice Choice { get; init; }
	}

	/// <summary>Everything the first factor established, for a new transaction.</summary>
	public sealed class MfaLoginTransactionRequest
	{
		public string UserId { get; init; }
		public int? DepartmentId { get; init; }
		public UserSessionClientApplication ClientApplication { get; init; }
		public string ClientId { get; init; }
		public MfaEvidenceMethod FirstFactorMethod { get; init; }
		public DateTime FirstFactorVerifiedOnUtc { get; init; }
		public string DepartmentSsoConfigId { get; init; }
		public long AuthenticationGeneration { get; init; }
		public IReadOnlyCollection<string> Scopes { get; init; }
		public bool TotpEnrolled { get; init; }

		/// <summary>The first-factor request asked for a shared session (a shared installation or a Web shared workstation).</summary>
		public bool SharedModeRequested { get; init; }

		/// <summary>The label the first-factor request sent for its installation or workstation, as a session would record it.</summary>
		public string InstallationLabel { get; init; }
	}

	public enum MfaLoginTransactionOutcome
	{
		Usable = 0,

		/// <summary>No such transaction for this client (or a completion code that does not match).</summary>
		Invalid,

		Expired,

		/// <summary>Already completed, redeemed or canceled.</summary>
		AlreadyUsed,

		TooManyAttempts,

		/// <summary>The department's MFA policy changed since the first factor.</summary>
		PolicyChanged,

		/// <summary>The account's authentication generation moved (password change, revocation) or the user is gone.</summary>
		SessionRevoked,

		Unavailable
	}

	public sealed class MfaLoginTransactionResult
	{
		public MfaLoginTransactionOutcome Outcome { get; init; }
		public MfaLoginTransaction Transaction { get; init; }

		public bool IsUsable => Outcome == MfaLoginTransactionOutcome.Usable;

		public static MfaLoginTransactionResult Of(MfaLoginTransactionOutcome outcome, MfaLoginTransaction transaction = null) =>
			new() { Outcome = outcome, Transaction = transaction };
	}

	/// <summary>The one-use completion credential for the token endpoint (workbook section 7.1).</summary>
	public sealed class MfaLoginCompletion
	{
		public MfaLoginTransactionOutcome Outcome { get; init; }

		/// <summary>The transaction secret, set only when the transaction began already complete; redeemed with the code.</summary>
		public string Transaction { get; init; }

		public string CompletionCode { get; init; }
		public int ExpiresInSeconds { get; init; }

		public bool Succeeded => Outcome == MfaLoginTransactionOutcome.Usable && CompletionCode != null;
	}

	/// <summary>Names and codes shared by the token endpoint and the completion endpoints (workbook sections 7.1 and 7.6).</summary>
	public static class MfaLoginTransactions
	{
		public const string FlowParameter = "mfa_flow";
		public const string FlowValue = "transaction";
		public const string CompletionGrantType = "urn:resgrid:params:oauth:grant-type:mfa_completion";

		public static string ErrorCode(MfaLoginTransactionOutcome outcome) => outcome switch
		{
			MfaLoginTransactionOutcome.Invalid => "mfa_transaction_invalid",
			MfaLoginTransactionOutcome.Expired => "mfa_transaction_expired",
			MfaLoginTransactionOutcome.AlreadyUsed => "mfa_transaction_invalid",
			MfaLoginTransactionOutcome.TooManyAttempts => "too_many_attempts",
			MfaLoginTransactionOutcome.PolicyChanged => "policy_changed",
			MfaLoginTransactionOutcome.SessionRevoked => "session_revoked",
			MfaLoginTransactionOutcome.Unavailable => "service_unavailable",
			_ => null
		};
	}
}
