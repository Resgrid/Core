using System;
using System.Linq;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// A security notice to the account holder (passkey plan section 6.4): a factor was added, replaced, removed or used
	/// for recovery, or someone else tried to use the account. The row is a user-level outbox entry (workbook section 8.3
	/// rule 7): it is written when the change commits, delivered at once where possible and retried until it is sent or
	/// fails for good. It holds only what the notice says, never a credential, code, assertion, grant or protected data.
	/// </summary>
	public class SecurityNotice
	{
		public string SecurityNoticeId { get; set; }

		public string UserId { get; set; }

		/// <summary><see cref="SecurityNoticeKind"/>.</summary>
		public int Kind { get; set; }

		public DateTime OccurredOnUtc { get; set; }

		/// <summary>The app where it happened, when known (<see cref="UserSessionClientApplication"/>).</summary>
		public int? ClientApplication { get; set; }

		/// <summary>The installation label from the server's session record, when known.</summary>
		public string InstallationLabel { get; set; }

		/// <summary>Coarse origin (region and country), when known.</summary>
		public string Region { get; set; }

		public int State { get; set; }

		public int Attempts { get; set; }

		public DateTime NextAttemptOnUtc { get; set; }

		/// <summary>The sender currently holding the notice; another sender skips it until the lease ends.</summary>
		public string LeaseOwner { get; set; }

		public DateTime? LeaseUntilUtc { get; set; }

		public DateTime? SentOnUtc { get; set; }

		/// <summary>A value-free reason for the last failed delivery.</summary>
		public string LastFailure { get; set; }

		public DateTime CreatedOnUtc { get; set; }

		public SecurityNoticeKind NoticeKind => (SecurityNoticeKind)Kind;
		public SecurityNoticeState NoticeState => (SecurityNoticeState)State;
	}

	public enum SecurityNoticeKind
	{
		TotpEnabled = 1,
		TotpReplaced = 2,
		TotpDisabled = 3,
		RecoveryCodesRegenerated = 4,

		/// <summary>A recovery code signed the user in or started factor recovery.</summary>
		RecoveryCodeUsed = 5,

		PasskeyRegistered = 6,
		PasskeyRemoved = 7,
		ApprovalTurnedOn = 8,
		ApprovalTurnedOff = 9,

		/// <summary>The user answered a Responder approval request with "I didn't request this".</summary>
		ApprovalNotMe = 10,

		/// <summary>Approval requests were paused after repeated denials or expiries.</summary>
		ApprovalSuspended = 11,

		FactorRecoveryCompleted = 12,

		/// <summary>An authenticator app or passkey was set up in a shared vehicle or workstation session (plan section 6.5).</summary>
		SharedInstallationFactor = 13,

		/// <summary>The user reported a verification on their account as not theirs (plan section 6.5 "This wasn't me").</summary>
		ActivityReported = 14
	}

	public enum SecurityNoticeState
	{
		Pending = 0,
		Sent = 1,

		/// <summary>Every attempt failed, or there is nowhere to send it. Logged and audited; the change it reports stands.</summary>
		Failed = 2
	}

	public static class SecurityNotices
	{
		/// <summary>A coarse origin for a notice: region and country only, never the city.</summary>
		public static string Region(string region, string country) =>
			string.Join(", ", new[] { region, country }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim())) is { Length: > 0 } text
				? text
				: null;
	}

	/// <summary>What to tell the account holder, from the server's own record of the change.</summary>
	public sealed class SecurityNoticeRequest
	{
		public string UserId { get; init; }
		public SecurityNoticeKind Kind { get; init; }
		public DateTime? OccurredOnUtc { get; init; }
		public UserSessionClientApplication? ClientApplication { get; init; }
		public string InstallationLabel { get; init; }
		public string Region { get; init; }
	}
}
