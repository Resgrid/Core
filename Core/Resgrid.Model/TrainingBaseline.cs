using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Question types of the baseline training quiz (R-S9). Persisted in TrainingQuestions.Type; append only.</summary>
	public enum TrainingQuestionTypes
	{
		/// <summary>One correct option.</summary>
		SingleChoice = 0,

		/// <summary>Several correct options, scored all-or-nothing: every correct option and no other.</summary>
		MultiChoice = 1,

		/// <summary>Two options, True and False, one of them correct.</summary>
		TrueFalse = 2
	}

	/// <summary>How a member came to be assigned a training. Persisted in TrainingUsers.AssignmentSource; append only.</summary>
	public enum TrainingAssignmentSources
	{
		/// <summary>Picked by name on the training, or a static list from before dynamic assignment.</summary>
		Manual = 0,

		/// <summary>The training's audience rules (dynamic assignment) placed the member.</summary>
		Audience = 1,

		/// <summary>The member enrolled from the self-enrol catalog.</summary>
		SelfEnrolled = 2
	}

	/// <summary>Why a completion was moved into TrainingCompletionHistories. Append only.</summary>
	public enum TrainingCompletionHistoryReasons
	{
		/// <summary>The training's repeat interval assigned it again.</summary>
		Repeat = 0,

		/// <summary>A material edit of the quiz re-assigned completed members (R-T6).</summary>
		MaterialEdit = 1,

		/// <summary>An officer reset the member.</summary>
		Reset = 2
	}

	/// <summary>
	/// One submitted quiz attempt (R-T7, baseline and free; the LMS keeps its own LmsAssessmentAttempts). The attempt keeps
	/// the quiz it was taken against (R-T6): a revision hash and a snapshot of the questions, the options offered, the
	/// answer key at the time and the answers chosen. Score and Passed are stored and never recomputed (T-D20). Failed
	/// attempts and the per-question answers are performance records (R-T12): only the member and whoever reports on the
	/// training see them.
	/// </summary>
	[Table("TrainingQuizAttempts")]
	public class TrainingQuizAttempt : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int TrainingQuizAttemptId { get; set; }

		public int DepartmentId { get; set; }

		public int TrainingId { get; set; }

		public string UserId { get; set; }

		/// <summary>1-based, per member and training, across cycles.</summary>
		public int AttemptNumber { get; set; }

		/// <summary>The assignment cycle the attempt belongs to (TrainingUser.Cycle at the time).</summary>
		public int Cycle { get; set; }

		public DateTime SubmittedOn { get; set; }

		/// <summary>SHA-256 (hex) of the quiz as scored: questions, types, options and answer key, plus the pass mark.</summary>
		public string QuizRevisionHash { get; set; }

		/// <summary><see cref="TrainingQuizSnapshot"/> as JSON.</summary>
		public string Snapshot { get; set; }

		public int QuestionCount { get; set; }

		public int CorrectCount { get; set; }

		public double Score { get; set; }

		public double MinimumScore { get; set; }

		public bool Passed { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return TrainingQuizAttemptId; }
			set { TrainingQuizAttemptId = (int)value; }
		}

		[NotMapped]
		public string TableName => "TrainingQuizAttempts";

		[NotMapped]
		public string IdName => "TrainingQuizAttemptId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>The quiz as it was when an attempt was scored.</summary>
	public class TrainingQuizSnapshot
	{
		public double MinimumScore { get; set; }

		public List<TrainingQuizSnapshotQuestion> Questions { get; set; } = new List<TrainingQuizSnapshotQuestion>();
	}

	public class TrainingQuizSnapshotQuestion
	{
		public int QuestionId { get; set; }

		public string Text { get; set; }

		public int Type { get; set; }

		public List<TrainingQuizSnapshotOption> Options { get; set; } = new List<TrainingQuizSnapshotOption>();

		/// <summary>The option ids the member chose (ids of this question's options only).</summary>
		public List<int> ChosenOptionIds { get; set; } = new List<int>();

		public bool AnsweredCorrectly { get; set; }
	}

	public class TrainingQuizSnapshotOption
	{
		public int OptionId { get; set; }

		public string Text { get; set; }

		public bool Correct { get; set; }
	}

	/// <summary>
	/// A dynamic assignment rule of a training (T-D5, T-D20): a <see cref="ContentAudienceKinds"/> and the id it refers to.
	/// Re-evaluated on membership changes by worker 83. Protocols (P1) keep their own ProtocolAudiences rows with the same
	/// kinds and resolver (<see cref="IContentAudienceRule"/>).
	/// </summary>
	[Table("TrainingAudiences")]
	public class TrainingAudience : IEntity, IContentAudienceRule
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int TrainingAudienceId { get; set; }

		public int DepartmentId { get; set; }

		public int TrainingId { get; set; }

		public int Kind { get; set; }

		/// <summary>Group, role, certification type, unit type or shift id, or a user id; null for Everyone.</summary>
		public string RefId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return TrainingAudienceId; }
			set { TrainingAudienceId = (int)value; }
		}

		[NotMapped]
		public string TableName => "TrainingAudiences";

		[NotMapped]
		public string IdName => "TrainingAudienceId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A completion that a repeat, a material edit or a reset moved off the live assignment row, so it stays on the
	/// member's record. T1's transcript backfills its learning records from this table and TrainingUsers.
	/// </summary>
	[Table("TrainingCompletionHistories")]
	public class TrainingCompletionHistory : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int TrainingCompletionHistoryId { get; set; }

		public int DepartmentId { get; set; }

		public int TrainingId { get; set; }

		public string UserId { get; set; }

		public int Cycle { get; set; }

		public DateTime? AssignedOn { get; set; }

		public DateTime? DueOn { get; set; }

		public DateTime? ViewedOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		public double Score { get; set; }

		public bool? Passed { get; set; }

		/// <summary><see cref="TrainingCompletionHistoryReasons"/>.</summary>
		public int Reason { get; set; }

		public DateTime RecordedOn { get; set; }

		public string RecordedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return TrainingCompletionHistoryId; }
			set { TrainingCompletionHistoryId = (int)value; }
		}

		[NotMapped]
		public string TableName => "TrainingCompletionHistories";

		[NotMapped]
		public string IdName => "TrainingCompletionHistoryId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>"Flag this question": a member tells the author a question looks wrong. Resolved by whoever authors it.</summary>
	[Table("TrainingQuestionFlags")]
	public class TrainingQuestionFlag : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int TrainingQuestionFlagId { get; set; }

		public int DepartmentId { get; set; }

		public int TrainingId { get; set; }

		/// <summary>No foreign key: removing a question in an edit removes its flags in the service.</summary>
		public int TrainingQuestionId { get; set; }

		public string UserId { get; set; }

		public string Comment { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? ResolvedOn { get; set; }

		public string ResolvedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return TrainingQuestionFlagId; }
			set { TrainingQuestionFlagId = (int)value; }
		}

		[NotMapped]
		public string TableName => "TrainingQuestionFlags";

		[NotMapped]
		public string IdName => "TrainingQuestionFlagId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Where one member stands on one assignment, as every page, the API and the notifier show it.</summary>
	public enum TrainingAssignmentStates
	{
		NotStarted = 0,
		InProgress = 1,
		Failed = 2,
		Complete = 3,
		Overdue = 4,
		Exempt = 5,
		OnLeave = 6,
		Scheduled = 7,
		Removed = 8
	}
}
