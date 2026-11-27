using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Trainings
{
	// Training T0 item 3 (protocols-training-knowledge-plan §13): the member and officer training endpoints Omni needs. Quiz
	// payloads carry questions and options only; the answer key never leaves the server (R-T11). All times are UTC.

	/// <summary>One of the member's assignments, with where they stand on it.</summary>
	public class TrainingAssignmentData
	{
		public int TrainingId { get; set; }
		public string Name { get; set; }
		/// <summary>The training's description as plain text.</summary>
		public string Summary { get; set; }
		/// <summary>TrainingAssignmentStates value.</summary>
		public int State { get; set; }
		/// <summary>TrainingAssignmentStates name (NotStarted, InProgress, Failed, Complete, Overdue, Exempt, OnLeave, Scheduled, Removed).</summary>
		public string StateName { get; set; }
		public DateTime? AssignedOn { get; set; }
		/// <summary>The due moment with leave applied (end of the department-local day).</summary>
		public DateTime? DueOn { get; set; }
		public bool Viewed { get; set; }
		public DateTime? ViewedOn { get; set; }
		public bool Complete { get; set; }
		public DateTime? CompletedOn { get; set; }
		public bool HasQuiz { get; set; }
		/// <summary>The latest attempt's score (0-100); 0 before any attempt.</summary>
		public double Score { get; set; }
		/// <summary>The latest attempt's stored result; null without a quiz or an attempt.</summary>
		public bool? Passed { get; set; }
		public int AttemptCount { get; set; }
		/// <summary>Attempts left in this cycle; null when unlimited.</summary>
		public int? AttemptsLeft { get; set; }
		public bool Exempt { get; set; }
		public DateTime? ExemptUntil { get; set; }
		public bool Extended { get; set; }
		public decimal? Hours { get; set; }
	}

	/// <summary>An open training the member is not assigned to (reference material, or one they may enrol in).</summary>
	public class TrainingCatalogData
	{
		public int TrainingId { get; set; }
		public string Name { get; set; }
		public string Summary { get; set; }
		public bool CanSelfEnroll { get; set; }
		public bool HasQuiz { get; set; }
		public decimal? Hours { get; set; }
		/// <summary>Relative due rule of an enrolment (days), when the training has one.</summary>
		public int? DueDays { get; set; }
		public DateTime CreatedOn { get; set; }
	}

	/// <summary>A certification of the member that is due, expired or pinned (My Training).</summary>
	public class TrainingCertificationData
	{
		public int CertificationId { get; set; }
		public string Name { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public bool Pinned { get; set; }
		public bool IsExpired { get; set; }
		public bool DueSoon { get; set; }
		public int? DaysLeft { get; set; }
	}

	public class MyTrainingsData
	{
		/// <summary>Overdue, due soon, in progress, other open and recently completed assignments, soonest due first.</summary>
		public List<TrainingAssignmentData> Assignments { get; set; } = new List<TrainingAssignmentData>();
		public List<TrainingCatalogData> Catalog { get; set; } = new List<TrainingCatalogData>();
		public List<TrainingCertificationData> Certifications { get; set; } = new List<TrainingCertificationData>();
		/// <summary>A leave period is running: due dates and reminders are paused.</summary>
		public bool OnLeave { get; set; }
	}

	public class MyTrainingsResult : StandardApiResponseV4Base
	{
		public MyTrainingsData Data { get; set; } = new MyTrainingsData();
	}

	public class TrainingAttachmentData
	{
		public int TrainingAttachmentId { get; set; }
		public string FileName { get; set; }
		public string FileType { get; set; }
		public long? FileSize { get; set; }
	}

	/// <summary>One quiz option: its id and text, never whether it is correct (R-T11).</summary>
	public class TrainingQuizOptionData
	{
		public int OptionId { get; set; }
		public string Text { get; set; }
	}

	/// <summary>One quiz question: text, type (TrainingQuestionTypes) and options; scoring happens on the server.</summary>
	public class TrainingQuizQuestionData
	{
		public int QuestionId { get; set; }
		public string Text { get; set; }
		/// <summary>TrainingQuestionTypes value: 0 single choice, 1 multiple choice (all-or-nothing), 2 true/false.</summary>
		public int Type { get; set; }
		public List<TrainingQuizOptionData> Options { get; set; } = new List<TrainingQuizOptionData>();
	}

	public class TrainingQuizData
	{
		public double MinimumScore { get; set; }
		public bool OneQuestionPerPage { get; set; }
		public int? AttemptLimit { get; set; }
		public List<TrainingQuizQuestionData> Questions { get; set; } = new List<TrainingQuizQuestionData>();
	}

	public class TrainingDetailData
	{
		public int TrainingId { get; set; }
		public string Name { get; set; }
		/// <summary>Sanitized rich text; allow-listed embeds (YouTube, Vimeo, Google Docs/Sheets, Microsoft Stream/Teams) only.</summary>
		public string Description { get; set; }
		/// <summary>Sanitized rich text; allow-listed embeds only.</summary>
		public string TrainingText { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime? StartsOn { get; set; }
		public bool OpenToAllMembers { get; set; }
		public bool AllowSelfEnroll { get; set; }
		public decimal? Hours { get; set; }
		public decimal? CeHours { get; set; }
		public string CeProvider { get; set; }
		public string CeCourseNumber { get; set; }
		public bool HasQuiz { get; set; }
		/// <summary>
		/// Round 9A read and confirm (optional, additive): a training without a quiz completes only when the member confirms they read and
		/// reviewed it (POST AcknowledgeTraining), not on opening it. Older apps ignore it; the member confirms on the web or in Omni.
		/// </summary>
		public bool RequiresAcknowledgment { get; set; }
		/// <summary>The member's assignment; null when they read an open training they are not assigned to.</summary>
		public TrainingAssignmentData Assignment { get; set; }
		public List<TrainingAttachmentData> Attachments { get; set; } = new List<TrainingAttachmentData>();
		/// <summary>Questions and options only; null when the member may not take the quiz (not assigned) or there is none.</summary>
		public TrainingQuizData Quiz { get; set; }
	}

	public class TrainingResult : StandardApiResponseV4Base
	{
		public TrainingDetailData Data { get; set; }
	}

	public class TrainingQuizAnswerInput
	{
		public int QuestionId { get; set; }
		public List<int> OptionIds { get; set; } = new List<int>();
	}

	public class SubmitTrainingQuizInput
	{
		public int TrainingId { get; set; }
		public List<TrainingQuizAnswerInput> Answers { get; set; } = new List<TrainingQuizAnswerInput>();
	}

	/// <summary>The outcome of a quiz submission: the score and result only, never which answers were right.</summary>
	public class TrainingQuizSubmissionData
	{
		/// <summary>TrainingQuizSubmissionStatuses name (Recorded, NotAssigned, NoQuiz, AlreadyPassed, AttemptLimitReached, NotStarted, NotFound).</summary>
		public string Status { get; set; }
		public bool Recorded { get; set; }
		public double Score { get; set; }
		public bool Passed { get; set; }
		public int QuestionCount { get; set; }
		public int CorrectCount { get; set; }
		public int AttemptNumber { get; set; }
		public int? AttemptsLeft { get; set; }
	}

	public class TrainingQuizSubmissionResult : StandardApiResponseV4Base
	{
		public TrainingQuizSubmissionData Data { get; set; } = new TrainingQuizSubmissionData();
	}

	/// <summary>One of the member's own attempts: result only, no snapshot of answers.</summary>
	public class TrainingAttemptData
	{
		public int AttemptNumber { get; set; }
		public int Cycle { get; set; }
		public DateTime SubmittedOn { get; set; }
		public double Score { get; set; }
		public bool Passed { get; set; }
		public int QuestionCount { get; set; }
		public int CorrectCount { get; set; }
	}

	public class TrainingAttemptsResult : StandardApiResponseV4Base
	{
		public List<TrainingAttemptData> Data { get; set; } = new List<TrainingAttemptData>();
	}

	public class TrainingIdInput
	{
		public int TrainingId { get; set; }
	}

	public class FlagTrainingQuestionInput
	{
		public int TrainingId { get; set; }
		public int QuestionId { get; set; }
		public string Comment { get; set; }
	}

	public class PinTrainingCertificationInput
	{
		public int CertificationId { get; set; }
		public bool Pinned { get; set; } = true;
	}

	public class TrainingAssignmentResult : StandardApiResponseV4Base
	{
		public TrainingAssignmentData Data { get; set; }
	}

	public class TrainingActionResult : StandardApiResponseV4Base
	{
		public bool Data { get; set; }
	}

	/// <summary>One assignee row of the officer's report. Per-question answers are not included (R-T12).</summary>
	public class TrainingReportRowData
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public string GroupName { get; set; }
		public bool IsActiveMember { get; set; }
		public int State { get; set; }
		public string StateName { get; set; }
		public DateTime? AssignedOn { get; set; }
		public DateTime? DueOn { get; set; }
		public DateTime? OriginalDueOn { get; set; }
		public bool Viewed { get; set; }
		public DateTime? ViewedOn { get; set; }
		public bool Complete { get; set; }
		public DateTime? CompletedOn { get; set; }
		public double Score { get; set; }
		public bool? Passed { get; set; }
		public int AttemptCount { get; set; }
		public double? LastAttemptScore { get; set; }
		public DateTime? LastAttemptOn { get; set; }
		public bool OnLeave { get; set; }
		public DateTime? ExemptOn { get; set; }
		public string ExemptReason { get; set; }
		public DateTime? ExemptUntil { get; set; }
		public DateTime? ExtendedOn { get; set; }
		public string ExtensionReason { get; set; }
		public DateTime? RemovedOn { get; set; }
	}

	public class TrainingReportData
	{
		public int TrainingId { get; set; }
		public string Name { get; set; }
		public bool IsArchived { get; set; }
		public int CompleteCount { get; set; }
		public int OverdueCount { get; set; }
		public int ExemptCount { get; set; }
		public double CompletionPercent { get; set; }
		public int OpenQuestionFlags { get; set; }
		public List<TrainingReportRowData> Rows { get; set; } = new List<TrainingReportRowData>();
	}

	public class TrainingReportResult : StandardApiResponseV4Base
	{
		public TrainingReportData Data { get; set; }
	}

	// Training T1 (one transcript): the member's transcript, the department's training categories and outside training
	// submissions for Omni. All times are UTC.

	/// <summary>One learning record on a transcript.</summary>
	public class LearningRecordData
	{
		public int Id { get; set; }
		public string Title { get; set; }
		public string CourseCode { get; set; }
		public DateTime CompletedOn { get; set; }
		public decimal Hours { get; set; }
		public int? CategoryId { get; set; }
		/// <summary>The category's name; null when uncategorized or the category is unknown.</summary>
		public string CategoryName { get; set; }
		/// <summary>LearningRecordSourceKinds value (0 course completion, 2 training record, 3 external submission, 9 import, 10 calendar attendance...).</summary>
		public int SourceKind { get; set; }
		/// <summary>LearningRecordSourceKinds name.</summary>
		public string SourceKindName { get; set; }
		/// <summary>LearningRecordStatuses value: 0 pending, 1 verified, 2 rejected, 3 voided. Only verified records count toward hours.</summary>
		public int Status { get; set; }
		/// <summary>LearningRecordStatuses name.</summary>
		public string StatusName { get; set; }
		public decimal? CeHours { get; set; }
		public string CeProvider { get; set; }
		public string CeCourseNumber { get; set; }
		/// <summary>Written by the history backfill from completions that predate the transcript.</summary>
		public bool IsBackfill { get; set; }
		/// <summary>The record may duplicate another one of the member's records and an officer has not resolved it yet.</summary>
		public bool PossibleDuplicate { get; set; }
		/// <summary>Round 7B: LearningCompletionMethods name ("Self" or "RecordedByOther": recorded for the member by an instructor). Optional.</summary>
		public string CompletionMethod { get; set; }
		/// <summary>Round 7B: who recorded the completion for the member (RecordedByOther only).</summary>
		public string RecordedByUserId { get; set; }
		/// <summary>Round 7B: when it was recorded (UTC; RecordedByOther only).</summary>
		public DateTime? RecordedOn { get; set; }
		/// <summary>Round 7B: the recording's source: "manual" or "class:{sessionId}" (RecordedByOther only).</summary>
		public string RecordedSource { get; set; }
		/// <summary>Round 7B: the recorder's note (RecordedByOther only).</summary>
		public string RecordedNote { get; set; }
		/// <summary>Round 7B: the attestation statement version the recorder accepted (RecordedByOther only).</summary>
		public string AttestationVersion { get; set; }
		/// <summary>
		/// Round 9B: how the record came about: System (recorded from its source), SelfReported (the member's, not reviewed), Reviewed (a reviewer
		/// or officer approved it), RecordedByOther (an instructor recorded it), Imported. Optional.
		/// </summary>
		public string Provenance { get; set; }
		/// <summary>Round 9B: a member submission's review state: AwaitingReview, InfoRequested, Approved, Denied or SelfReported (null otherwise).</summary>
		public string ReviewState { get; set; }
		/// <summary>Round 9B: when the submission was last handed to reviewers (UTC).</summary>
		public DateTime? SubmittedOn { get; set; }
		/// <summary>Round 9B: the reviewer's latest note on a submission (the reason of a denial, what more information is needed).</summary>
		public string ReviewNote { get; set; }
	}

	public class LearningRecordsResult : StandardApiResponseV4Base
	{
		public List<LearningRecordData> Data { get; set; } = new List<LearningRecordData>();
	}

	/// <summary>One of the department's training categories (removed categories are not listed).</summary>
	public class TrainingCategoryData
	{
		public int CategoryId { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		/// <summary>TrainingCategoryKinds value: 0 custom, 1 ISO FSRS, 2 NCCP, 3 OSHA, 4 state.</summary>
		public int Kind { get; set; }
		public int SortOrder { get; set; }
		public string Description { get; set; }
		public decimal? TargetHours { get; set; }
		/// <summary>TrainingTargetPeriods value: 0 none, 1 per year, 2 per month, 3 per certification cycle, 4 once, 5 first year.</summary>
		public int TargetPeriod { get; set; }
	}

	public class TrainingCategoriesResult : StandardApiResponseV4Base
	{
		public List<TrainingCategoryData> Data { get; set; } = new List<TrainingCategoryData>();
	}

	/// <summary>
	/// A course the member completed outside the department. It is Pending until an officer verifies it. The optional evidence
	/// file is base64 in <see cref="Data"/>: PDF, JPG, JPEG, PNG, GIF, DOC or DOCX by the extension of <see cref="FileName"/>, at
	/// most 10 MB decoded.
	/// </summary>
	public class SubmitExternalTrainingInput
	{
		public string Title { get; set; }
		/// <summary>When the course was completed (UTC); at most one day ahead of now.</summary>
		public DateTime CompletedOn { get; set; }
		/// <summary>Hours, more than 0 and at most 9999.</summary>
		public decimal? Hours { get; set; }
		public int? CategoryId { get; set; }
		public string CeProvider { get; set; }
		public string CeCourseNumber { get; set; }
		public decimal? CeHours { get; set; }
		/// <summary>MethodsOfTraining value: 0 classroom, 1 online, 2 drill/field, 3 virtual, 4 self study, 5 on the job.</summary>
		public int? Method { get; set; }
		public string Notes { get; set; }
		public string FileName { get; set; }
		/// <summary>Informational; the stored type follows the file name's extension.</summary>
		public string ContentType { get; set; }
		/// <summary>The evidence file, base64.</summary>
		public string Data { get; set; }
		/// <summary>
		/// Round 9B: more evidence files (optional; with or instead of <see cref="Data"/>): at most 5 in one submission, 10 MB each, 30 MB
		/// together; PDF, JPG, JPEG, PNG, GIF, WEBP, DOC, DOCX or ODT.
		/// </summary>
		public List<TrainingEvidenceFileInput> Files { get; set; }
	}

	/// <summary>Round 9B: one evidence file (base64) of a training hours submission or resubmission.</summary>
	public class TrainingEvidenceFileInput
	{
		public string FileName { get; set; }
		/// <summary>Informational; the stored type follows the file name's extension.</summary>
		public string ContentType { get; set; }
		/// <summary>The file, base64.</summary>
		public string Data { get; set; }
		/// <summary>TrainingEvidenceKinds: 0 other, 1 certificate, 2 photo, 3 document, 4 receipt.</summary>
		public int Kind { get; set; }
	}

	public class ExternalTrainingSubmissionResult : StandardApiResponseV4Base
	{
		/// <summary>The submission (Pending when it waits for a reviewer, Verified when accepted as self-reported); null when refused.</summary>
		public LearningRecordData Data { get; set; }
		/// <summary>Round 9B: true when the submission waits for a reviewer, false when it was accepted at once as self-reported.</summary>
		public bool ReviewRequired { get; set; }
		/// <summary>
		/// Why the submission was refused: submissions_disabled, title_required, title_too_long, date_invalid, hours_invalid,
		/// ce_hours_invalid, category_invalid, method_invalid, text_too_long, file_name_required, file_type_not_allowed, file_too_large,
		/// file_invalid, or a learning_record_* code of the service. Null on success.
		/// </summary>
		public string Error { get; set; }
	}
}
