using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>A filtered read of the ledger (transcript, reports, bulk categorize). Always department scoped.</summary>
	public class LearningRecordQuery
	{
		public int DepartmentId { get; set; }

		/// <summary>Only these members (case-insensitive); null or empty for every member.</summary>
		public List<string> UserIds { get; set; }

		/// <summary>CompletedOn at or after (UTC).</summary>
		public DateTime? From { get; set; }

		/// <summary>CompletedOn before (UTC).</summary>
		public DateTime? To { get; set; }

		public List<int> CategoryIds { get; set; }

		/// <summary>Only records without a category.</summary>
		public bool UncategorizedOnly { get; set; }

		public List<int> SourceKinds { get; set; }

		/// <summary>Statuses to return; null returns every status except Voided (unless <see cref="IncludeVoided"/>).</summary>
		public List<int> Statuses { get; set; }

		public bool IncludeVoided { get; set; }

		/// <summary>
		/// The user ids external learners (Training T5) carry: a reserved prefix no member id has. A department-wide query (no
		/// <see cref="UserIds"/>) leaves their records out, so members' reports and sweeps never count people who are not members.
		/// </summary>
		public const string ExternalLearnerUserIdPrefix = "xl-";

		/// <summary>Title or course code contains (case-insensitive).</summary>
		public string Search { get; set; }

		public string CourseCode { get; set; }

		public bool BackfillOnly { get; set; }

		public bool UnresolvedDuplicatesOnly { get; set; }

		/// <summary>Round 9B: only member submissions in these review states (TrainingSubmissionReviewStates); AwaitingReview (0) also matches a T1 submission without one.</summary>
		public List<int> ReviewStates { get; set; }

		/// <summary>Round 9B: oldest (re)submission first instead of newest completion first (the reviewer inbox).</summary>
		public bool OldestSubmittedFirst { get; set; }

		public int Skip { get; set; }

		/// <summary>At most this many rows (capped by the repository).</summary>
		public int Take { get; set; } = 1000;
	}

	/// <summary>One record a source wants on the transcript; the writer compares it with what is stored (idempotent, R-T1, R-T4).</summary>
	public class LearningRecordDraft
	{
		public string UserId { get; set; }
		public string ComponentKey { get; set; } = string.Empty;
		public string SessionKey { get; set; }
		public string Title { get; set; }
		public string CourseCode { get; set; }
		public DateTime CompletedOn { get; set; }
		public DateTime? StartedOn { get; set; }
		public DateTime? EndedOn { get; set; }
		public decimal Hours { get; set; }
		public int? CategoryId { get; set; }
		public int? MethodOfTraining { get; set; }
		public string Objectives { get; set; }
		public string Location { get; set; }
		public string CeProvider { get; set; }
		public string CeCourseNumber { get; set; }
		public decimal? CeHours { get; set; }
		public string InstructorUserIds { get; set; }
		public string InstructorNames { get; set; }
		public string Tags { get; set; }
		public string ExternalId { get; set; }
		public string Notes { get; set; }
		/// <summary>Pending (check-in without check-out, external submission) or Verified.</summary>
		public LearningRecordStatuses Status { get; set; } = LearningRecordStatuses.Verified;
		public bool IsBackfill { get; set; }
		public int? EvidenceFileId { get; set; }
		public string VerifiedByUserId { get; set; }
		/// <summary>Round 7B: <see cref="LearningCompletionMethods"/> and, for a recorded completion, who recorded it, when and its recording row.</summary>
		public int CompletionMethod { get; set; }
		public string RecordedByUserId { get; set; }
		public DateTime? RecordedOn { get; set; }
		public int? RecordedCompletionId { get; set; }
		/// <summary>Round 9B: a member submission's TrainingSubmissionReviewStates and when it was handed to reviewers.</summary>
		public int? ReviewState { get; set; }
		public DateTime? SubmittedOn { get; set; }
	}

	/// <summary>What one write of a source did.</summary>
	/// <summary>
	/// Round 9A: what one write changed on the transcript, told to the <see cref="Services.ILearningRecordObserver"/>s after it is stored:
	/// records that now count (written verified, or verified by an officer) and records that were voided.
	/// </summary>
	public sealed class LearningRecordChange
	{
		public int DepartmentId { get; set; }

		/// <summary>Who caused it (the officer, the recorder, the member); null or "system" for a worker.</summary>
		public string ActingUserId { get; set; }

		public List<LearningRecord> Written { get; set; } = new List<LearningRecord>();

		public List<LearningRecord> Voided { get; set; } = new List<LearningRecord>();

		public bool IsEmpty => (Written == null || Written.Count == 0) && (Voided == null || Voided.Count == 0);
	}

	public class LearningRecordWriteResult
	{
		public int Written { get; set; }
		public int Voided { get; set; }
		public int Unchanged { get; set; }
		public int CreditsAdded { get; set; }
		public int CreditsRemoved { get; set; }
		public List<LearningRecord> Records { get; set; } = new List<LearningRecord>();

		public void Add(LearningRecordWriteResult other)
		{
			if (other == null)
				return;
			Written += other.Written;
			Voided += other.Voided;
			Unchanged += other.Unchanged;
			CreditsAdded += other.CreditsAdded;
			CreditsRemoved += other.CreditsRemoved;
			Records.AddRange(other.Records);
		}
	}

	/// <summary>Uncategorized records grouped for the bulk-categorize screen (by course name and code).</summary>
	public class LearningRecordGroup
	{
		public string Title { get; set; }
		public string CourseCode { get; set; }
		public int Count { get; set; }
		public decimal Hours { get; set; }
		public DateTime FirstOn { get; set; }
		public DateTime LastOn { get; set; }
	}

	/// <summary>How the hours report groups its rows.</summary>
	public enum TrainingHoursGroupings
	{
		Category = 0,
		Member = 1,
		Month = 2
	}

	public class TrainingHoursReportQuery
	{
		public int DepartmentId { get; set; }
		public DateTime From { get; set; }
		public DateTime To { get; set; }
		public TrainingHoursGroupings GroupBy { get; set; }
		public List<int> CategoryIds { get; set; }
		public List<string> UserIds { get; set; }

		/// <summary>R-T13: include deleted, disabled and hidden members (audits covering a period when they served).</summary>
		public bool IncludeFormerMembers { get; set; }

		/// <summary>Only members on duty now (Shifts, §13a "On duty now").</summary>
		public bool OnDutyNow { get; set; }
	}

	public class TrainingHoursReportRow
	{
		/// <summary>Category id, user id or yyyy-MM, per grouping; empty for Uncategorized.</summary>
		public string Key { get; set; }
		public string Label { get; set; }
		public int Records { get; set; }
		public int Members { get; set; }
		public decimal Hours { get; set; }
		public decimal CeHours { get; set; }
		/// <summary>A category's target per member, when it has one.</summary>
		public decimal? TargetHours { get; set; }
		public bool IsFormerMember { get; set; }
	}

	/// <summary>The hours by category, member or period report (free, HTML/CSV/PDF).</summary>
	public class TrainingHoursReport
	{
		public TrainingHoursReportQuery Query { get; set; }
		public List<TrainingHoursReportRow> Rows { get; set; } = new List<TrainingHoursReportRow>();
		public decimal TotalHours { get; set; }
		public int TotalRecords { get; set; }
		public int Members { get; set; }
		/// <summary>Possible duplicates no officer has resolved yet, shown beside the total (R-T2).</summary>
		public int UnresolvedDuplicates { get; set; }
		public int PendingRecords { get; set; }
		public DateTime GeneratedOn { get; set; }
	}

	/// <summary>A learning record that "apply history to credits" would turn into a certification credit (R-T3).</summary>
	public class HistoryCreditProposal
	{
		public int LearningRecordId { get; set; }
		public string UserId { get; set; }
		public string Title { get; set; }
		public DateTime CompletedOn { get; set; }
		public decimal Hours { get; set; }
		public int? CategoryId { get; set; }
		public int PersonnelCertificationId { get; set; }
		public DateTime? CycleStart { get; set; }
		public DateTime? CycleEnd { get; set; }
		public bool IsBackfill { get; set; }
	}

	/// <summary>One certification cycle in the preview: the proposed credits beside the manual credits already in it.</summary>
	public class HistoryCreditCycle
	{
		public int PersonnelCertificationId { get; set; }
		public string UserId { get; set; }
		public DateTime? CycleStart { get; set; }
		public DateTime? CycleEnd { get; set; }
		public decimal? RequiredHours { get; set; }
		public List<HistoryCreditProposal> Proposed { get; set; } = new List<HistoryCreditProposal>();
		public List<PersonnelCertificationCredit> ExistingCredits { get; set; } = new List<PersonnelCertificationCredit>();
		public decimal ProposedHours => Proposed.Sum(p => p.Hours);
		public decimal ManualHours => ExistingCredits.Where(c => !c.IsDerived).Sum(c => c.Hours);
		public decimal DerivedHours => ExistingCredits.Where(c => c.IsDerived).Sum(c => c.Hours);
	}

	/// <summary>"Apply history to credits" for one certification type: nothing is written until the officer confirms.</summary>
	public class HistoryCreditPreview
	{
		public int DepartmentCertificationTypeId { get; set; }
		public List<HistoryCreditCycle> Cycles { get; set; } = new List<HistoryCreditCycle>();
		/// <summary>Records in a mapped category whose date falls in no certification cycle: they stay on the transcript only (R-T5).</summary>
		public int OutsideEveryCycle { get; set; }
		public int ProposedCount => Cycles.Sum(c => c.Proposed.Count);
		public decimal ProposedHours => Cycles.Sum(c => c.ProposedHours);
	}

	/// <summary>Hours toward renewal of one certification record, from its credits and the ledger (T1 item 4).</summary>
	public class CertificationRenewalHours
	{
		public int PersonnelCertificationId { get; set; }
		public decimal CreditedHours { get; set; }
		public decimal DerivedHours { get; set; }
		/// <summary>Hours in the type's mapped categories inside the cycle that are not credited yet (history waiting to be applied).</summary>
		public decimal UncreditedLedgerHours { get; set; }
	}

	/// <summary>How an import row found its member.</summary>
	public enum TrainingImportMatchMethods
	{
		None = 0,
		Email = 1,
		MemberId = 2,
		ExternalId = 3,
		Name = 4,
		/// <summary>The officer chose the member on the review screen.</summary>
		Manual = 5
	}

	/// <summary>One parsed row of a training history CSV (R-C1).</summary>
	public class TrainingHistoryImportRow
	{
		public int RowNumber { get; set; }
		public string Email { get; set; }
		public string MemberId { get; set; }
		public string MemberExternalId { get; set; }
		public string MemberName { get; set; }
		public string Course { get; set; }
		public string CourseCode { get; set; }
		public DateTime? CompletedOn { get; set; }
		public decimal? Hours { get; set; }
		public string Category { get; set; }
		public string CeProvider { get; set; }
		public string CeCourseNumber { get; set; }
		public decimal? CeHours { get; set; }
		public string Instructor { get; set; }
		public string Method { get; set; }
		public string RecordId { get; set; }
		public string Notes { get; set; }
		public string CertificationType { get; set; }
		public string CertificationNumber { get; set; }
		public DateTime? CertificationIssuedOn { get; set; }
		public DateTime? CertificationExpiresOn { get; set; }

		public string MatchedUserId { get; set; }
		public string MatchedName { get; set; }
		public TrainingImportMatchMethods MatchMethod { get; set; }
		public int? MatchedCategoryId { get; set; }
		public int? MatchedCertificationTypeId { get; set; }
		/// <summary>Resource keys of the problems that stop the row (missing date, unreadable hours, no member).</summary>
		public List<string> Errors { get; set; } = new List<string>();
		/// <summary>Resource keys of notes that do not stop the row (unknown category: lands in Uncategorized).</summary>
		public List<string> Warnings { get; set; } = new List<string>();
		/// <summary>Already on the transcript (the same row imported before).</summary>
		public bool AlreadyImported { get; set; }
		public bool IsImportable => Errors.Count == 0 && !string.IsNullOrWhiteSpace(MatchedUserId) && !AlreadyImported;
	}

	/// <summary>The dry run (or the result) of a training history import.</summary>
	public class TrainingHistoryImportResult
	{
		public bool DryRun { get; set; }
		public List<TrainingHistoryImportRow> Rows { get; set; } = new List<TrainingHistoryImportRow>();
		public List<string> IgnoredColumns { get; set; } = new List<string>();
		public string FileError { get; set; }
		public int Matched => Rows.Count(r => !string.IsNullOrWhiteSpace(r.MatchedUserId));
		public int Unmatched => Rows.Count(r => string.IsNullOrWhiteSpace(r.MatchedUserId));
		public int Importable => Rows.Count(r => r.IsImportable);
		public int RecordsWritten { get; set; }
		public int RecordsSkipped { get; set; }
		public int CertificationsCreated { get; set; }
		public int CreditsCreated { get; set; }
	}

	/// <summary>LMS (dark): one member's hours in each ISO FSRS category against its target, for the formal report.</summary>
	public class IsoFsrsReportRow
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public Dictionary<string, decimal> HoursByCode { get; set; } = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

		/// <summary>The member holds no licensed LMS seat (§17.4 seat rules): shown as not licensed; the hours are their own evidence.</summary>
		public bool NotLicensed { get; set; }
	}

	public class IsoFsrsReport
	{
		public DateTime From { get; set; }
		public DateTime To { get; set; }
		public List<TrainingCategory> Categories { get; set; } = new List<TrainingCategory>();
		public List<IsoFsrsReportRow> Rows { get; set; } = new List<IsoFsrsReportRow>();
	}

	/// <summary>LMS (dark): one member's NREMT NCCP progress in their current cycle.</summary>
	public class NccpProgressRow
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public string CertificationName { get; set; }
		public int PersonnelCertificationId { get; set; }
		public DateTime? CycleStart { get; set; }
		public DateTime? CycleEnd { get; set; }
		public NccpLevelTarget Target { get; set; }
		public decimal National { get; set; }
		public decimal Local { get; set; }
		public decimal Individual { get; set; }

		/// <summary>
		/// The member holds no licensed LMS seat (§17.4 seat rules): the row shows as not licensed, never as on track or behind, so a
		/// gap in licensing cannot hide as a gap in training.
		/// </summary>
		public bool NotLicensed { get; set; }
	}

	public class NccpProgressReport
	{
		public List<NccpProgressRow> Rows { get; set; } = new List<NccpProgressRow>();
		/// <summary>True when the department has the three NCCP categories applied.</summary>
		public bool CategoriesApplied { get; set; }
	}

	/// <summary>The public verification answer (name, course, date, status only, lapse rule 9).</summary>
	public class CertificateVerification
	{
		public string MemberName { get; set; }
		public string Course { get; set; }
		public DateTime CompletedOn { get; set; }
		public string DepartmentName { get; set; }
		/// <summary>Verified, Voided or Rejected as text.</summary>
		public LearningRecordStatuses Status { get; set; }
	}
}
