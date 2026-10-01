using System;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// One verification on the account, successful or denied, for the account's "Recent MFA activity" (plan section 6.5):
	/// when, which method, which app and installation asked, why, and whether it was a shared installation. Kept for
	/// <c>TwoFactorConfig.MfaActivityRetentionDays</c>. Never holds a code, assertion or anything that verifies again.
	/// </summary>
	public class MfaActivity
	{
		public string MfaActivityId { get; set; }
		public string UserId { get; set; }
		public DateTime OccurredOnUtc { get; set; }

		/// <summary><see cref="MfaEvidenceMethod"/>.</summary>
		public int Method { get; set; }

		/// <summary><see cref="MfaEvidencePurpose"/>.</summary>
		public int Purpose { get; set; }

		public bool Successful { get; set; }
		public int ClientApplication { get; set; }
		public string InstallationLabel { get; set; }
		public bool SharedMode { get; set; }
		public int? DepartmentId { get; set; }

		/// <summary>The session the verification was for, when there was one; a sign-in in progress has none yet.</summary>
		public string SessionId { get; set; }

		/// <summary>For a Responder approval: the Responder session that approved or denied it.</summary>
		public string ApproverSessionId { get; set; }

		/// <summary>When the user reported it as not theirs.</summary>
		public DateTime? ReportedOnUtc { get; set; }
	}

	/// <summary>What a caller knows about a verification. The service fills the installation and shared flag from the session.</summary>
	public sealed class MfaActivityEntry
	{
		public string UserId { get; init; }
		public MfaEvidenceMethod Method { get; init; }
		public MfaEvidencePurpose Purpose { get; init; }
		public bool Successful { get; init; }
		public UserSessionClientApplication ClientApplication { get; init; }
		public string InstallationLabel { get; init; }
		public bool SharedMode { get; init; }
		public int? DepartmentId { get; init; }
		public string SessionId { get; init; }
		public string ApproverSessionId { get; init; }
	}

	public enum MfaActivityReportOutcome
	{
		Reported = 0,

		/// <summary>No such activity on this account, or it is past retention.</summary>
		NotFound,

		/// <summary>It was already reported.</summary>
		AlreadyReported
	}

	public sealed class MfaActivityReport
	{
		public MfaActivityReportOutcome Outcome { get; init; }

		/// <summary>The session the verification opened or served was ended.</summary>
		public bool SessionEnded { get; init; }
	}
}
