using System;

namespace Resgrid.Model.Events
{
	// Round 7C (2026-11-25, product owner: "workflow and events emitted for every part of these changes that make sense to expose"):
	// the Protocols, Training/LMS, signing and knowledge state changes a department automates on. Every payload here carries ids,
	// names of the content, dates, statuses and counts only: never a member's or outside party's name, e-mail, phone or address,
	// never protected (ADP) values, wellness or health data, answer keys, free-text comments or reasons. The workflow resolves a
	// member from a user id under its own rules (the common user.* block). Property names avoid the protected-field catalog's
	// column names (Name, Title, Number, Reason, Error...) so a protected department's safe projection does not blank them.
	// The template variables, samples and trigger names come from ContentWorkflowTriggers.

	/// <summary>What happened to a library protocol (<see cref="ProtocolWorkflowEvent"/>).</summary>
	public enum ProtocolWorkflowChanges
	{
		Published = 1,
		Retired = 2,
		Expired = 3,
		ReviewDue = 4,
		ReviewOverdue = 5,
		BulletinIssued = 6,
		BulletinWithdrawn = 7,
		TranslationApproved = 8,
		PublicLinkChanged = 9
	}

	/// <summary>
	/// A protocol version went into force, a protocol was retired or expired, its review fell due or overdue, a priority bulletin was
	/// issued or withdrawn, a translation was approved, or a public link was created or revoked (P1, P2, P3, P5; triggers 230-235,
	/// 248-250). Raised by ProtocolLibraryService (web, API, worker 84), ProtocolAcknowledgmentService, ProtocolTranslationService and
	/// ProtocolPublicPageService after the change is stored.
	/// </summary>
	public class ProtocolWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public ProtocolWorkflowChanges Change { get; set; }

		/// <summary>The protocol; 0 for a category public link.</summary>
		public int ProtocolId { get; set; }

		public int? ProtocolVersionId { get; set; }

		public string ProtocolCode { get; set; }

		public string ProtocolNumber { get; set; }

		/// <summary>The protocol's name (a category public link: the category's name).</summary>
		public string ProtocolName { get; set; }

		/// <summary><see cref="ProtocolKinds"/> as its name ("Sog", "Bulletin").</summary>
		public string ProtocolKind { get; set; }

		public int? CategoryId { get; set; }

		public string VersionLabel { get; set; }

		public bool IsMajorVersion { get; set; }

		public bool RequiresAcknowledgment { get; set; }

		public DateTime? EffectiveOn { get; set; }

		public DateTime? ExpiresOn { get; set; }

		public DateTime? NextReviewOn { get; set; }

		/// <summary>Review reminders: ThirtyDays, SevenDays or Overdue.</summary>
		public string ReviewStage { get; set; }

		/// <summary>Translation approved: the translation's locale ("es").</summary>
		public string Locale { get; set; }

		public int? TranslationId { get; set; }

		public int? PublicLinkId { get; set; }

		/// <summary>Protocol or Category.</summary>
		public string PublicLinkKind { get; set; }

		/// <summary>Created or Revoked.</summary>
		public string PublicLinkAction { get; set; }

		public bool EmbedEnabled { get; set; }

		/// <summary>The protocol's owner (the member who keeps it current).</summary>
		public string OwnerUserId { get; set; }

		/// <summary>The member who acted; null when worker 84 did (scheduled activation, expiry, review reminders).</summary>
		public string UserId { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case ProtocolWorkflowChanges.Retired: return WorkflowTriggerEventType.ProtocolRetired;
					case ProtocolWorkflowChanges.Expired: return WorkflowTriggerEventType.ProtocolVersionExpired;
					case ProtocolWorkflowChanges.ReviewDue: return WorkflowTriggerEventType.ProtocolReviewDue;
					case ProtocolWorkflowChanges.ReviewOverdue: return WorkflowTriggerEventType.ProtocolReviewOverdue;
					case ProtocolWorkflowChanges.BulletinIssued: return WorkflowTriggerEventType.ProtocolBulletinIssued;
					case ProtocolWorkflowChanges.BulletinWithdrawn: return WorkflowTriggerEventType.ProtocolBulletinWithdrawn;
					case ProtocolWorkflowChanges.TranslationApproved: return WorkflowTriggerEventType.ProtocolTranslationApproved;
					case ProtocolWorkflowChanges.PublicLinkChanged: return WorkflowTriggerEventType.ProtocolPublicLinkChanged;
					default: return WorkflowTriggerEventType.ProtocolVersionPublished;
				}
			}
		}

		/// <summary>The protocol and (optionally) version fields every protocol event carries.</summary>
		public static ProtocolWorkflowEvent For(DispatchProtocol protocol, ProtocolVersion version, ProtocolWorkflowChanges change, string userId, DateTime occurredOn)
		{
			var evt = new ProtocolWorkflowEvent
			{
				DepartmentId = protocol?.DepartmentId ?? version?.DepartmentId ?? 0,
				Change = change,
				ProtocolId = protocol?.DispatchProtocolId ?? version?.DispatchProtocolId ?? 0,
				ProtocolCode = protocol?.Code,
				ProtocolNumber = protocol?.Number,
				ProtocolName = protocol?.Name,
				ProtocolKind = protocol == null ? null : Enum.IsDefined(typeof(ProtocolKinds), protocol.Kind) ? ((ProtocolKinds)protocol.Kind).ToString() : protocol.Kind.ToString(),
				CategoryId = protocol?.CategoryId,
				NextReviewOn = protocol?.NextReviewOn,
				OwnerUserId = protocol?.OwnerUserId,
				UserId = userId,
				OccurredOn = occurredOn
			};

			if (version != null)
			{
				evt.ProtocolVersionId = version.ProtocolVersionId;
				evt.VersionLabel = version.Label;
				evt.IsMajorVersion = version.Major > 0 && version.Minor == 0;
				evt.RequiresAcknowledgment = version.RequiresAcknowledgment;
				evt.EffectiveOn = version.EffectiveOn;
				evt.ExpiresOn = version.ExpiresOn;
			}

			return evt;
		}
	}

	/// <summary>What happened to a member's protocol acknowledgment (<see cref="ProtocolAcknowledgmentWorkflowEvent"/>).</summary>
	public enum ProtocolAcknowledgmentWorkflowChanges
	{
		Assigned = 1,
		Completed = 2,
		Overdue = 3
	}

	/// <summary>
	/// A member was asked to read and sign a protocol version, signed it (and passed its quiz), or became overdue (P2 read-and-sign;
	/// triggers 251-253). Raised by ProtocolAcknowledgmentService (web, API, workers 83 and 86) once per acknowledgment row and change.
	/// Only active members are assigned and reminded (the inactive-member policy).
	/// </summary>
	public class ProtocolAcknowledgmentWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public ProtocolAcknowledgmentWorkflowChanges Change { get; set; }

		public int AcknowledgmentId { get; set; }

		public int ProtocolId { get; set; }

		public int ProtocolVersionId { get; set; }

		public string ProtocolCode { get; set; }

		public string ProtocolName { get; set; }

		public string VersionLabel { get; set; }

		public bool IsBulletin { get; set; }

		/// <summary>The member who owes (or signed) it.</summary>
		public string UserId { get; set; }

		/// <summary><see cref="ProtocolAcknowledgmentSources"/> as its name: Publish, AudienceEntry, Bulletin.</summary>
		public string AssignmentSource { get; set; }

		public DateTime? AssignedOn { get; set; }

		public DateTime? DueOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		/// <summary>Overdue: Overdue, or Escalated when the supervisor was told too.</summary>
		public string ReminderStage { get; set; }

		public int? DaysOverdue { get; set; }

		public bool QuizRequired { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case ProtocolAcknowledgmentWorkflowChanges.Completed: return WorkflowTriggerEventType.ProtocolAcknowledgmentCompleted;
					case ProtocolAcknowledgmentWorkflowChanges.Overdue: return WorkflowTriggerEventType.ProtocolAcknowledgmentOverdue;
					default: return WorkflowTriggerEventType.ProtocolAcknowledgmentAssigned;
				}
			}
		}

		public static ProtocolAcknowledgmentWorkflowEvent For(ProtocolAcknowledgment row, DispatchProtocol protocol, ProtocolVersion version,
			ProtocolAcknowledgmentWorkflowChanges change, DateTime occurredOn)
		{
			return new ProtocolAcknowledgmentWorkflowEvent
			{
				DepartmentId = row?.DepartmentId ?? protocol?.DepartmentId ?? 0,
				Change = change,
				AcknowledgmentId = row?.ProtocolAcknowledgmentId ?? 0,
				ProtocolId = row?.DispatchProtocolId ?? protocol?.DispatchProtocolId ?? 0,
				ProtocolVersionId = row?.ProtocolVersionId ?? version?.ProtocolVersionId ?? 0,
				ProtocolCode = protocol?.Code,
				ProtocolName = protocol?.Name,
				VersionLabel = version?.Label,
				IsBulletin = protocol != null && protocol.Kind == (int)ProtocolKinds.Bulletin,
				UserId = row?.UserId,
				AssignmentSource = row == null ? null : Enum.IsDefined(typeof(ProtocolAcknowledgmentSources), row.Source) ? ((ProtocolAcknowledgmentSources)row.Source).ToString() : null,
				AssignedOn = row?.AssignedOn,
				DueOn = row?.DueOn,
				CompletedOn = row?.CompletedOn,
				QuizRequired = row?.QuizTrainingId != null,
				OccurredOn = occurredOn
			};
		}
	}

	/// <summary>What happened on a dispatch case (<see cref="DispatchCaseWorkflowEvent"/>).</summary>
	public enum DispatchCaseWorkflowChanges
	{
		DeterminantChanged = 1,
		ReviewSent = 2,
		ReviewAcknowledged = 3
	}

	/// <summary>
	/// A dispatcher overrode (or restored) the determinant a protocol case reached on a call (P4), or a dispatch case review was sent to
	/// the dispatcher or acknowledged by them (P5 QA; triggers 254-256). The case answers, the override reason and every comment stay
	/// out of the payload (they can hold what a caller said).
	/// </summary>
	public class DispatchCaseWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public DispatchCaseWorkflowChanges Change { get; set; }

		public int CallId { get; set; }

		public string CallNumber { get; set; }

		public int CallProtocolId { get; set; }

		public int ProtocolId { get; set; }

		public int? ProtocolVersionId { get; set; }

		public string ProtocolCode { get; set; }

		public string ProtocolName { get; set; }

		public string VersionLabel { get; set; }

		/// <summary>The determinant on the call now ("26-A-1").</summary>
		public string Determinant { get; set; }

		/// <summary>Determinant changed: the determinant before the change.</summary>
		public string PreviousDeterminant { get; set; }

		/// <summary>What the case flow reached on its own.</summary>
		public string ComputedDeterminant { get; set; }

		public bool Overridden { get; set; }

		public int? ReviewId { get; set; }

		/// <summary>The review's overall score (0-100).</summary>
		public int? ReviewScore { get; set; }

		public int QuestionCount { get; set; }

		public int QuestionPoints { get; set; }

		public int InstructionCount { get; set; }

		public int InstructionPoints { get; set; }

		/// <summary>The dispatcher: who changed the determinant, or whose case was reviewed.</summary>
		public string UserId { get; set; }

		public string ReviewerUserId { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case DispatchCaseWorkflowChanges.ReviewSent: return WorkflowTriggerEventType.DispatchCaseReviewSent;
					case DispatchCaseWorkflowChanges.ReviewAcknowledged: return WorkflowTriggerEventType.DispatchCaseReviewAcknowledged;
					default: return WorkflowTriggerEventType.CallDeterminantChanged;
				}
			}
		}
	}

	/// <summary>
	/// A Resgrid reviewer listed, rejected or removed one of the department's own community exchange submissions (P5; trigger 257).
	/// The review note stays out of the payload.
	/// </summary>
	public class ExchangeListingWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public int ListingId { get; set; }

		/// <summary>Protocol, Training, SkillSheet or LessonPlan.</summary>
		public string ItemKind { get; set; }

		public string ListingTitle { get; set; }

		public string VersionLabel { get; set; }

		/// <summary>Listed, Rejected or Removed.</summary>
		public string Decision { get; set; }

		/// <summary>The member who submitted it.</summary>
		public string UserId { get; set; }

		public DateTime SubmittedOn { get; set; }

		public DateTime? ReviewedOn { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger => WorkflowTriggerEventType.ExchangeListingReviewed;
	}

	/// <summary>
	/// The knowledge assistant could not answer a question (or the asker marked an answer unhelpful) and a gap was recorded (K1; trigger
	/// 258). The question text, the asker and the nearest sources stay out of the payload: ids, the reason, where it was asked and a count.
	/// </summary>
	public class KnowledgeGapWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public int KnowledgeGapId { get; set; }

		/// <summary>NotCovered or ThumbsDown.</summary>
		public string GapReason { get; set; }

		/// <summary>Library, Protocol, Training, Assistant, Call, Api, Help or Course.</summary>
		public string Surface { get; set; }

		public int NearestSourceCount { get; set; }

		public DateTime AskedOn { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger => WorkflowTriggerEventType.KnowledgeGapRecorded;
	}

	/// <summary>What happened to a sealed document (<see cref="DocumentSealingWorkflowEvent"/>).</summary>
	public enum DocumentSealingWorkflowChanges
	{
		Sealed = 1,
		Failed = 2
	}

	/// <summary>
	/// A document was sealed (PAdES, S1) or sealing failed for good after its retries (triggers 218-219), so an operator or admin can
	/// act. Records documents and disclosures are not raised here: the Records module owns their events and they can belong to
	/// restricted records. The raw error text stays out of the payload (an error code only).
	/// </summary>
	public class DocumentSealingWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public DocumentSealingWorkflowChanges Change { get; set; }

		public int SealedDocumentId { get; set; }

		/// <summary><see cref="SignatureSubjectKinds"/> as its name ("ProtocolVersion", "TimeReport").</summary>
		public string SubjectKind { get; set; }

		public string SubjectId { get; set; }

		public string DocumentTitle { get; set; }

		/// <summary>Sealed, ProviderCompleted, ProviderAuditCertificate or SealedExternalPdf.</summary>
		public string DocumentRole { get; set; }

		public int? SignatureRequestId { get; set; }

		/// <summary>Sealed or Failed.</summary>
		public string Status { get; set; }

		/// <summary>BaselineT, BaselineLT or BaselineLTA.</summary>
		public string PadesLevel { get; set; }

		public int Attempts { get; set; }

		/// <summary>Failed: a short error code ("sealing_input_missing"); never the raw error text.</summary>
		public string ErrorCode { get; set; }

		public DateTime? SealedOn { get; set; }

		/// <summary>The member who asked for the document; null for the system.</summary>
		public string UserId { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger => Change == DocumentSealingWorkflowChanges.Failed ? WorkflowTriggerEventType.DocumentSealingFailed : WorkflowTriggerEventType.DocumentSealed;
	}

	/// <summary>What happened to an LMS course (<see cref="CourseWorkflowEvent"/>).</summary>
	public enum CourseWorkflowChanges
	{
		VersionPublished = 1,
		Completed = 2,
		AssessmentGraded = 3,
		Assigned = 4,
		AwaitingGrading = 5,
		Retired = 6
	}

	/// <summary>
	/// An LMS course version was published, a member completed a course, or an assessment needing a grader was graded (T2; triggers
	/// 259-261); an officer assigned a course directly, an attempt waits for a grader, or a course was retired (271-273). A direct
	/// assignment is Courses → Assign or an Academy lesson: requirements and paths raise TrainingAssigned (224) instead. Course completion fires for every completed enrollment, requirement or not (TrainingCompleted 225 stays the requirement
	/// and path completion); external learners are not members and raise nothing here. Never an answer, a key or the grader's notes.
	/// </summary>
	public class CourseWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public CourseWorkflowChanges Change { get; set; }

		public int CourseId { get; set; }

		public int? CourseVersionId { get; set; }

		public string CourseCode { get; set; }

		public string CourseTitle { get; set; }

		public string VersionLabel { get; set; }

		public bool IsMajorVersion { get; set; }

		/// <summary>Resgrid Academy content (system published).</summary>
		public bool IsAcademy { get; set; }

		public decimal? Hours { get; set; }

		/// <summary>Assigned: the due date the officer set, when there is one.</summary>
		public DateTime? DueOn { get; set; }

		/// <summary>The learner; null for a version published.</summary>
		public string UserId { get; set; }

		/// <summary>The member who published or graded it.</summary>
		public string ActorUserId { get; set; }

		public int? EnrollmentId { get; set; }

		public int? AttemptId { get; set; }

		public int? LessonId { get; set; }

		public string LessonTitle { get; set; }

		public bool? Passed { get; set; }

		/// <summary>The graded attempt's score (percent).</summary>
		public decimal? Score { get; set; }

		public bool TestedOut { get; set; }

		/// <summary>The certification type a completion issues or renews.</summary>
		public int? CertificationTypeId { get; set; }

		public DateTime? CompletedOn { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case CourseWorkflowChanges.Completed: return WorkflowTriggerEventType.CourseCompleted;
					case CourseWorkflowChanges.AssessmentGraded: return WorkflowTriggerEventType.AssessmentGraded;
					case CourseWorkflowChanges.Assigned: return WorkflowTriggerEventType.CourseAssigned;
					case CourseWorkflowChanges.AwaitingGrading: return WorkflowTriggerEventType.AssessmentAwaitingGrading;
					case CourseWorkflowChanges.Retired: return WorkflowTriggerEventType.CourseRetired;
					default: return WorkflowTriggerEventType.CourseVersionPublished;
				}
			}
		}
	}

	/// <summary>What happened to a skill evaluation or task book (<see cref="SkillWorkflowEvent"/>).</summary>
	public enum SkillWorkflowChanges
	{
		EvaluationCompleted = 1,
		TaskBookClosed = 2
	}

	/// <summary>
	/// A skill evaluation was completed (signed off, passed or failed; one event per evaluated member) or a position task book closed
	/// (certified, expired or cancelled) (T4; triggers 262-263). Never the evaluator's notes, the answers or a cancellation reason.
	/// </summary>
	public class SkillWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public SkillWorkflowChanges Change { get; set; }

		public int? EvaluationId { get; set; }

		public int? SkillSheetId { get; set; }

		public int? TaskBookId { get; set; }

		/// <summary>The skill sheet's code, or the task book's position code.</summary>
		public string SkillCode { get; set; }

		/// <summary>The evaluation's title, or the task book's name.</summary>
		public string SkillTitle { get; set; }

		/// <summary>The member evaluated, or the task book's trainee.</summary>
		public string UserId { get; set; }

		/// <summary>The evaluator, or the certifying official (null for an expiry).</summary>
		public string ActorUserId { get; set; }

		/// <summary>Passed or Failed (evaluation); Certified, Expired or Cancelled (task book).</summary>
		public string Result { get; set; }

		public decimal? Score { get; set; }

		public bool CriticalFailed { get; set; }

		/// <summary>Members evaluated together (crew evaluation).</summary>
		public int MemberCount { get; set; }

		public int? CertificationTypeId { get; set; }

		public DateTime? CompletedOn { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger => Change == SkillWorkflowChanges.TaskBookClosed ? WorkflowTriggerEventType.TaskBookClosed : WorkflowTriggerEventType.SkillEvaluationCompleted;
	}

	/// <summary>
	/// A learning requirement's enforcement (the certification it expired) was undone: the member completed it, the requirement no
	/// longer applies, the add-on lapsed, or an officer undid it (T3; trigger 264). RequirementEnforced (229) is the enforcement itself.
	/// </summary>
	public class RequirementEnforcementWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public int EnforcementId { get; set; }

		public int RequirementId { get; set; }

		public string RequirementName { get; set; }

		/// <summary>The member whose certification it restored.</summary>
		public string UserId { get; set; }

		public int CertificationId { get; set; }

		public int CertificationTypeId { get; set; }

		/// <summary>Completed, Lapse, NoLongerApplies or Manual.</summary>
		public string UndoReason { get; set; }

		public int RestoredRoleCount { get; set; }

		public DateTime EnforcedOn { get; set; }

		/// <summary>The officer who undid it; null when the system did.</summary>
		public string ActorUserId { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger => WorkflowTriggerEventType.RequirementEnforcementUndone;
	}

	/// <summary>What happened to a training request (<see cref="TrainingRequestWorkflowEvent"/>).</summary>
	public enum TrainingRequestWorkflowChanges
	{
		Submitted = 1,
		Decided = 2
	}

	/// <summary>
	/// A member submitted a request to attend outside training, or its approval chain reached a final decision (T3 programs; triggers
	/// 265-266). The justification and the approvers' notes stay out of the payload.
	/// </summary>
	public class TrainingRequestWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public TrainingRequestWorkflowChanges Change { get; set; }

		public int RequestId { get; set; }

		/// <summary>The requesting member.</summary>
		public string UserId { get; set; }

		public string RequestTitle { get; set; }

		public string Provider { get; set; }

		public string CourseCode { get; set; }

		public int? CategoryId { get; set; }

		public DateTime? StartsOn { get; set; }

		public DateTime? EndsOn { get; set; }

		public decimal Hours { get; set; }

		public decimal TotalCost { get; set; }

		public string Currency { get; set; }

		/// <summary>Submitted, Approved or Rejected.</summary>
		public string Status { get; set; }

		/// <summary>Submitted: the step that decides next (Supervisor or TrainingOfficer).</summary>
		public string AwaitingStep { get; set; }

		public string DecidedByUserId { get; set; }

		public DateTime SubmittedOn { get; set; }

		public DateTime? DecidedOn { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger => Change == TrainingRequestWorkflowChanges.Decided ? WorkflowTriggerEventType.TrainingRequestDecided : WorkflowTriggerEventType.TrainingRequestSubmitted;
	}

	/// <summary>What happened to the department's LMS add-on or training data (<see cref="TrainingAdminWorkflowEvent"/>).</summary>
	public enum TrainingAdminWorkflowChanges
	{
		AddonLapsed = 1,
		AddonReactivated = 2,
		DataExportReady = 3,
		SeatsChanged = 4
	}

	/// <summary>
	/// Department-level LMS administration: the add-on lapsed or came back (T1/F lapse rules), or a training data export is ready to
	/// download (T5 data exit; triggers 267-269), or seats were enabled or disabled or the packs changed (274; counts only). Raised once: the
	/// lapse row opened or closed by this call, the export claimed by worker 99, the seat rows written, the packs compare-and-set.
	/// </summary>
	public class TrainingAdminWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public TrainingAdminWorkflowChanges Change { get; set; }

		/// <summary>Lapsed: RenewalCancelled, PaymentFailed, Revoked or Unknown.</summary>
		public string LapseReason { get; set; }

		public DateTime? LapsedSince { get; set; }

		public DateTime? PaidThrough { get; set; }

		public int Packs { get; set; }

		/// <summary>Seats changed: the packs before a renewal changed them (null for a seat change by an officer).</summary>
		public int? PreviousPacks { get; set; }

		/// <summary>Seats changed: member seats enabled and disabled by this change (counts only, never who).</summary>
		public int SeatsAdded { get; set; }

		public int SeatsRemoved { get; set; }

		/// <summary>Seats changed: member seats in use after the change, when read.</summary>
		public int? EnabledSeats { get; set; }

		public int? ExportId { get; set; }

		/// <summary>Full or RecordsOnly.</summary>
		public string ExportScope { get; set; }

		public bool IncludesMedia { get; set; }

		public long SizeBytes { get; set; }

		public DateTime? ExpiresOn { get; set; }

		/// <summary>The member who asked for the export, or who changed the seats (null for a renewal).</summary>
		public string UserId { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case TrainingAdminWorkflowChanges.AddonReactivated: return WorkflowTriggerEventType.LmsAddonReactivated;
					case TrainingAdminWorkflowChanges.DataExportReady: return WorkflowTriggerEventType.LmsDataExportReady;
					case TrainingAdminWorkflowChanges.SeatsChanged: return WorkflowTriggerEventType.LmsSeatsChanged;
					default: return WorkflowTriggerEventType.LmsAddonLapsed;
				}
			}
		}
	}

	/// <summary>
	/// A member's step of a multi-signer request became their turn to sign (S3 "your turn"; trigger 270), once per step when its turn
	/// starts. Member signers only: an outside signer's name, e-mail and phone never enter a workflow (220-223 cover the request itself).
	/// </summary>
	public class SignatureStepWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public int SignatureRequestId { get; set; }

		public int SignerId { get; set; }

		/// <summary><see cref="SignatureSubjectKinds"/> as its name ("SkillEvaluation", "TimeReport").</summary>
		public string SubjectKind { get; set; }

		public string SubjectId { get; set; }

		public string RequestTitle { get; set; }

		public int StepOrder { get; set; }

		/// <summary>Member, CertifyingOfficial, Evaluator, Vendor or Recipient.</summary>
		public string SignerRole { get; set; }

		/// <summary>The member whose turn it is.</summary>
		public string UserId { get; set; }

		public string RequestedByUserId { get; set; }

		public DateTime ExpiresOn { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger => WorkflowTriggerEventType.SignatureStepAssigned;
	}

	/// <summary>What happened to a member's certification through training (<see cref="CertificationTrainingWorkflowEvent"/>).</summary>
	public enum CertificationTrainingWorkflowChanges
	{
		Issued = 1,
		Renewed = 2,
		RevokedByVoid = 3,
		RequirementsDue = 4
	}

	/// <summary>
	/// Round 9A (triggers 275-278): a certification issued or renewed by the training engine (a completed requirement set, an LMS course or
	/// path that issues it, a cycle's renewal hours), taken back because a completion behind it was voided (returned to its earlier state or
	/// withdrawn), or due: the holder's requirement set is still open as the expiry nears. Ids, the type's name and code, dates and counts
	/// only: never the certification number, its document, the member's name or contact details, or the void's reason text.
	/// </summary>
	public class CertificationTrainingWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public CertificationTrainingWorkflowChanges Change { get; set; }

		/// <summary>The member's certification record (0 when a revoked issue withdrew it and no record remains readable).</summary>
		public int CertificationId { get; set; }

		public int CertificationTypeId { get; set; }

		public string CertificationTypeName { get; set; }

		public string CertificationTypeCode { get; set; }

		/// <summary>The holder.</summary>
		public string UserId { get; set; }

		/// <summary>CourseCompletion, PathCompletion, RequirementSet or CycleAutoRenew.</summary>
		public string Source { get; set; }

		/// <summary>Issued, Renewed, NextCycle; for a void Withdrawn or Restored.</summary>
		public string Outcome { get; set; }

		public DateTime? ExpiresOn { get; set; }

		public DateTime? PreviousExpiresOn { get; set; }

		public int ItemsDone { get; set; }

		public int ItemsTotal { get; set; }

		/// <summary>Due: days until the expiry (negative inside the grace period).</summary>
		public int DaysUntilExpiry { get; set; }

		public int IssuanceId { get; set; }

		/// <summary>Who caused it (the member, the recorder, the officer who voided); empty for the worker.</summary>
		public string ActorUserId { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger => Change switch
		{
			CertificationTrainingWorkflowChanges.Renewed => WorkflowTriggerEventType.CertificationRenewedByTraining,
			CertificationTrainingWorkflowChanges.RevokedByVoid => WorkflowTriggerEventType.CertificationRevokedByVoid,
			CertificationTrainingWorkflowChanges.RequirementsDue => WorkflowTriggerEventType.CertificationRequirementsDue,
			_ => WorkflowTriggerEventType.CertificationIssuedByTraining
		};
	}

	/// <summary>What happened to a member's hours submission (<see cref="TrainingSubmissionWorkflowEvent"/>, round 9B).</summary>
	public enum TrainingSubmissionWorkflowChanges
	{
		Received = 1,
		Approved = 2,
		Denied = 3,
		InfoRequested = 4,
		Resubmitted = 5
	}

	/// <summary>
	/// A member's training hours submission (CE / CPD / training hours with evidence) was received (waiting for a reviewer, or accepted as
	/// self-reported), approved, denied, sent back for more information or resubmitted (round 9B, triggers 280-284). Ids, the title, category,
	/// hours, states and counts only: the member's notes, the reviewer's reason, file names and contents stay out.
	/// </summary>
	public class TrainingSubmissionWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public TrainingSubmissionWorkflowChanges Change { get; set; }

		public int LearningRecordId { get; set; }

		/// <summary>The member the submission belongs to.</summary>
		public string UserId { get; set; }

		/// <summary>The submission's title ("Title" is a protected column name elsewhere, so the payload says RecordTitle).</summary>
		public string RecordTitle { get; set; }

		public string CourseCode { get; set; }

		public int? CategoryId { get; set; }

		public decimal Hours { get; set; }

		public decimal? CeHours { get; set; }

		/// <summary>AwaitingReview, InfoRequested, Approved, Denied or SelfReported.</summary>
		public string ReviewState { get; set; }

		/// <summary>Received: true when it waits for a reviewer, false when it was accepted as self-reported.</summary>
		public bool ReviewRequired { get; set; }

		/// <summary>Evidence files on the submission (a count, never the names).</summary>
		public int EvidenceFiles { get; set; }

		/// <summary>The reviewer who approved, denied or asked for more information (empty otherwise).</summary>
		public string ReviewerUserId { get; set; }

		public DateTime CompletedOn { get; set; }

		public DateTime? SubmittedOn { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case TrainingSubmissionWorkflowChanges.Approved: return WorkflowTriggerEventType.TrainingSubmissionApproved;
					case TrainingSubmissionWorkflowChanges.Denied: return WorkflowTriggerEventType.TrainingSubmissionDenied;
					case TrainingSubmissionWorkflowChanges.InfoRequested: return WorkflowTriggerEventType.TrainingSubmissionInfoRequested;
					case TrainingSubmissionWorkflowChanges.Resubmitted: return WorkflowTriggerEventType.TrainingSubmissionResubmitted;
					default: return WorkflowTriggerEventType.TrainingSubmissionReceived;
				}
			}
		}
	}

	/// <summary>
	/// A completion an instructor recorded for a member (permission 135, trigger 244) was voided (round 9B, trigger 285). Ids and the target's
	/// name only: the void reason stays out.
	/// </summary>
	public class TrainingCompletionVoidedWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public int RecordingId { get; set; }

		/// <summary>The member whose recorded completion was voided.</summary>
		public string UserId { get; set; }

		public string VoidedByUserId { get; set; }

		/// <summary>"Training" or "Course".</summary>
		public string TargetKind { get; set; }

		public int TargetId { get; set; }

		public string TargetName { get; set; }

		public int? LearningRecordId { get; set; }

		public DateTime CompletedOn { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger => WorkflowTriggerEventType.TrainingCompletionRecordingVoided;
	}

	/// <summary>What a content set workflow event says happened (round 10 A3, triggers 286-290).</summary>
	public enum ContentSetWorkflowChanges
	{
		UpdateArrived = 0,
		UpdateApplied = 1,
		Subscribed = 2,
		ProposalReceived = 3,
		ProposalAnswered = 4
	}

	/// <summary>
	/// A protocol or course set event (round 10 A3, triggers 286-290), raised in the department it concerns: the subscriber for an update or a
	/// subscription, the publisher for a proposal received, the proposing department for an answer. Ids, the set's and item's names, the
	/// version label and states only: no change summary, proposal text or member name.
	/// </summary>
	public class ContentSetWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public ContentSetWorkflowChanges Change { get; set; }

		public int SetId { get; set; }

		public string SetName { get; set; }

		/// <summary>Protocols or Training.</summary>
		public string SetKind { get; set; }

		/// <summary>Protocol, LmsCourse or Training (empty for a subscription).</summary>
		public string ItemKind { get; set; }

		public string ItemTitle { get; set; }

		public int PublisherDepartmentId { get; set; }

		public int SubscriberDepartmentId { get; set; }

		public string VersionLabel { get; set; }

		/// <summary>Updates: when it goes (or went) into force; otherwise empty.</summary>
		public DateTime? EffectiveOn { get; set; }

		/// <summary>Updates: NewVersion or NewItem; proposals: Open, Accepted or Declined; subscriptions: Active.</summary>
		public string State { get; set; }

		/// <summary>The member who caused it (empty for the scheduled worker or an automatic acceptance).</summary>
		public string ActorUserId { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case ContentSetWorkflowChanges.UpdateApplied: return WorkflowTriggerEventType.ContentSetUpdateApplied;
					case ContentSetWorkflowChanges.Subscribed: return WorkflowTriggerEventType.ContentSetSubscribed;
					case ContentSetWorkflowChanges.ProposalReceived: return WorkflowTriggerEventType.ContentSetProposalReceived;
					case ContentSetWorkflowChanges.ProposalAnswered: return WorkflowTriggerEventType.ContentSetProposalAnswered;
					default: return WorkflowTriggerEventType.ContentSetUpdateArrived;
				}
			}
		}
	}

	/// <summary>What an LMS sponsorship workflow event says happened (triggers 291-292).</summary>
	public enum LmsSponsorshipWorkflowChanges
	{
		Started = 0,
		Ended = 1
	}

	/// <summary>An LMS sponsorship started or ended (round 10 A3, triggers 291-292), raised in both parties' departments. Ids, department names and states only.</summary>
	public class LmsSponsorshipWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public LmsSponsorshipWorkflowChanges Change { get; set; }

		public int SponsorshipId { get; set; }

		public int SponsorDepartmentId { get; set; }

		public int SponsoredDepartmentId { get; set; }

		/// <summary>The other party's department name.</summary>
		public string OtherDepartment { get; set; }

		/// <summary>The seat limit (0 when none).</summary>
		public int SeatLimit { get; set; }

		/// <summary>Ended: EndedBySponsor, DeclinedBySponsored or LinkRevoked.</summary>
		public string EndReason { get; set; }

		public string ActorUserId { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger => Change == LmsSponsorshipWorkflowChanges.Ended ? WorkflowTriggerEventType.LmsSponsorshipEnded : WorkflowTriggerEventType.LmsSponsorshipStarted;
	}

	/// <summary>What a mutual-aid countersignature workflow event says happened (triggers 293-295).</summary>
	public enum MutualAidCountersignWorkflowChanges
	{
		Requested = 0,
		Countersigned = 1,
		Declined = 2
	}

	/// <summary>
	/// A mutual-aid agreement's partner countersignature over a Department Link (round 10 A3, triggers 293-295), raised in both departments. Ids,
	/// the agreement's title and states only (never its terms).
	/// </summary>
	public class MutualAidCountersignWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public MutualAidCountersignWorkflowChanges Change { get; set; }

		public int MutualAidAgreementId { get; set; }

		public string AgreementTitle { get; set; }

		public int OriginDepartmentId { get; set; }

		public int PartnerDepartmentId { get; set; }

		public int DepartmentLinkId { get; set; }

		/// <summary>The partner's signature request (0 when none).</summary>
		public int SignatureRequestId { get; set; }

		/// <summary>Requested, Countersigned, Declined or Lapsed.</summary>
		public string State { get; set; }

		public string ActorUserId { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case MutualAidCountersignWorkflowChanges.Countersigned: return WorkflowTriggerEventType.MutualAidAgreementCountersigned;
					case MutualAidCountersignWorkflowChanges.Declined: return WorkflowTriggerEventType.MutualAidCountersignDeclined;
					default: return WorkflowTriggerEventType.MutualAidCountersignRequested;
				}
			}
		}
	}
	/// <summary>Requested (trigger 296) or Decided (297: State says Signed, Declined or Lapsed).</summary>
	public enum RecordDeploymentPartnerChanges
	{
		Requested = 0,
		Decided = 1
	}

	/// <summary>
	/// A Records deployment fill's acceptance by the linked partner department (round 10 A3, triggers 296-297), raised in both departments.
	/// Order and request numbers, the resource type and states only (never the roster, travel or notes).
	/// </summary>
	public class RecordDeploymentPartnerWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public RecordDeploymentPartnerChanges Change { get; set; }

		public int OriginDepartmentId { get; set; }

		public int PartnerDepartmentId { get; set; }

		public string OrderNumber { get; set; }

		public string RequestNumber { get; set; }

		public string ResourceType { get; set; }

		/// <summary>The partner's signature request (0 when none).</summary>
		public int SignatureRequestId { get; set; }

		/// <summary>Requested, Signed, Declined or Lapsed.</summary>
		public string State { get; set; }

		public string ActorUserId { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger =>
			Change == RecordDeploymentPartnerChanges.Requested ? WorkflowTriggerEventType.RecordDeploymentPartnerSignRequested : WorkflowTriggerEventType.RecordDeploymentPartnerSignDecided;
	}
}
