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
}
