using System;
using System.Collections.Generic;
using Resgrid.Model.Helpers;

namespace Resgrid.Model
{
	/// <summary>Why a quiz submission was or was not recorded.</summary>
	public enum TrainingQuizSubmissionStatuses
	{
		Recorded = 0,
		NotFound = 1,
		NotAssigned = 2,
		NoQuiz = 3,
		AlreadyPassed = 4,
		AttemptLimitReached = 5,
		NotStarted = 6
	}

	/// <summary>The outcome of one quiz submission.</summary>
	public class TrainingQuizSubmission
	{
		public TrainingQuizSubmissionStatuses Status { get; set; }

		public TrainingQuizAttempt Attempt { get; set; }

		public TrainingQuizResult Result { get; set; }

		/// <summary>Attempts the member has left in this cycle; null when unlimited.</summary>
		public int? AttemptsLeft { get; set; }
	}

	/// <summary>What deleting a training did (T-D21: a training with completions is archived instead).</summary>
	public enum TrainingDeleteOutcomes
	{
		NotFound = 0,
		Deleted = 1,
		HasCompletions = 2
	}

	/// <summary>What an assignment change did, so the officer is told about every member who was skipped and why (T-D16).</summary>
	public class TrainingAssignmentOutcome
	{
		public List<string> Added { get; set; } = new List<string>();

		public List<string> Withdrawn { get; set; } = new List<string>();

		public List<string> AlreadyAssigned { get; set; } = new List<string>();

		public List<ContentAudienceSkip> Skipped { get; set; } = new List<ContentAudienceSkip>();

		/// <summary>Picked members a group admin may not assign (outside the group they administer).</summary>
		public List<string> OutOfScope { get; set; } = new List<string>();

		/// <summary>True when the training is closed to new assignees, so nobody was added.</summary>
		public bool Closed { get; set; }
	}

	/// <summary>One assignee row of a training report.</summary>
	public class TrainingReportRow
	{
		public TrainingUser Assignment { get; set; }

		public string UserId { get; set; }

		public string Name { get; set; }

		public string GroupName { get; set; }

		public bool IsActiveMember { get; set; }

		public TrainingAssignmentStates State { get; set; }

		public DateTime? DueUtc { get; set; }

		public bool OnLeave { get; set; }

		public int AttemptCount { get; set; }

		public TrainingQuizAttempt LastAttempt { get; set; }
	}

	/// <summary>The officer's report of one training (per-assignee status, attempts, history and question flags).</summary>
	public class TrainingReport
	{
		public Training Training { get; set; }

		public List<TrainingReportRow> Rows { get; set; } = new List<TrainingReportRow>();

		/// <summary>Performance records (R-T12): only for whoever may assign and report on the training.</summary>
		public List<TrainingQuizAttempt> Attempts { get; set; } = new List<TrainingQuizAttempt>();

		public List<TrainingCompletionHistory> History { get; set; } = new List<TrainingCompletionHistory>();

		public List<TrainingQuestionFlag> Flags { get; set; } = new List<TrainingQuestionFlag>();

		public int CompleteCount { get; set; }

		public int OverdueCount { get; set; }

		public int ExemptCount { get; set; }

		/// <summary>Completed share of the tracked (not removed, not exempt) assignees, 0-100.</summary>
		public double CompletionPercent { get; set; }
	}

	/// <summary>The officer KPI tiles of the Trainings index.</summary>
	public class TrainingManagerSummary
	{
		public int Overdue { get; set; }

		public int DueThisWeek { get; set; }

		public double CompletionPercent { get; set; }
	}

	/// <summary>A certification shown on My Training (due soon, expired or pinned).</summary>
	public class MyTrainingCertification
	{
		public PersonnelCertification Certification { get; set; }

		public string Name { get; set; }

		public DateTime? ExpiresOn { get; set; }

		public bool Pinned { get; set; }

		public bool IsExpired { get; set; }

		public bool DueSoon { get; set; }

		public int? DaysLeft { get; set; }
	}

	/// <summary>The member's My Training page: their assignments by state, the self-enrol catalog and certifications due.</summary>
	public class MyTrainingOverview
	{
		public const int DueSoonDays = 14;

		public const int CertificationDueDays = 60;

		public List<TrainingAssignmentStatus> Overdue { get; set; } = new List<TrainingAssignmentStatus>();

		public List<TrainingAssignmentStatus> DueSoon { get; set; } = new List<TrainingAssignmentStatus>();

		public List<TrainingAssignmentStatus> InProgress { get; set; } = new List<TrainingAssignmentStatus>();

		public List<TrainingAssignmentStatus> Other { get; set; } = new List<TrainingAssignmentStatus>();

		public List<TrainingAssignmentStatus> RecentCompletions { get; set; } = new List<TrainingAssignmentStatus>();

		/// <summary>Open trainings the member is not assigned: reference material, and those they may enrol in.</summary>
		public List<Training> Catalog { get; set; } = new List<Training>();

		public List<MyTrainingCertification> Certifications { get; set; } = new List<MyTrainingCertification>();

		public bool OnLeave { get; set; }
	}
}
