using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("Trainings")]
	public class Training : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int TrainingId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		public string CreatedByUserId { get; set; }

		[Required]
		public string Name { get; set; }

		[Required]
		public string Description { get; set; }

		[Required]
		public string TrainingText { get; set; }

		public double MinimumScore { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? ToBeCompletedBy { get; set; }

		public string UsersToAdd { get; set; }

		public string GroupsToAdd { get; set; }

		public string RolesToAdd { get; set; }

		public DateTime? Notified { get; set; }

		/// <summary>
		/// Any member of the department may view this training without being assigned to it (reference and optional
		/// material). Only assignees are tracked, notified and take the quiz. Off by default: a training is otherwise seen
		/// by its assignees and by whoever manages it.
		/// </summary>
		public bool OpenToAllMembers { get; set; }

		/// <summary>
		/// Open trainings only: a member may enrol themselves to be tracked (the self-enrol catalog). Reading an open
		/// training never assigns it; enrolling does, with the training's due rule.
		/// </summary>
		public bool AllowSelfEnroll { get; set; }

		/// <summary>
		/// Scheduled start, UTC. Before it the training is hidden from its assignees and is not announced; managers see it.
		/// Null starts at once.
		/// </summary>
		public DateTime? StartsOn { get; set; }

		/// <summary>
		/// Relative due rule: an assignment is due this many days after it was made (end of that department-local day).
		/// Null when the training has a fixed date (<see cref="ToBeCompletedBy"/>) or no due date.
		/// </summary>
		public int? DueDays { get; set; }

		/// <summary>No assignment is added to this training any more, by an edit, the audience worker or self-enrolment.</summary>
		public bool ClosedToNewAssignees { get; set; }

		/// <summary>One simple repeat (decision §25.4a item 10): every N months the current audience is assigned again.</summary>
		public int? RepeatMonths { get; set; }

		/// <summary>When the next repeat runs, UTC. Set from <see cref="RepeatMonths"/> on save and advanced by each run.</summary>
		public DateTime? NextRepeatOn { get; set; }

		/// <summary>The most quiz attempts one member may submit per assignment cycle. Null is unlimited.</summary>
		public int? QuizAttemptLimit { get; set; }

		/// <summary>The quiz player shows one question per page.</summary>
		public bool OneQuestionPerPage { get; set; }

		/// <summary>Due reminder lead days, comma separated (the certification NotifyLeadDaysCsv pattern). Null is one day.</summary>
		public string ReminderLeadDaysCsv { get; set; }

		/// <summary>An overdue notice goes to the member and to the training's creator.</summary>
		public bool SendOverdueNotices { get; set; }

		/// <summary>Repeat the overdue notice every N days while the assignment stays overdue. Null or 0 sends it once.</summary>
		public int? OverdueRepeatDays { get; set; }

		/// <summary>Custom text added to the new-training, reminder and overdue notices.</summary>
		public string ReminderMessage { get; set; }

		/// <summary>Training hours a completion is worth (nullable; T1 turns completions into learning records).</summary>
		public decimal? Hours { get; set; }

		/// <summary>Continuing-education hours, when they differ from <see cref="Hours"/>.</summary>
		public decimal? CeHours { get; set; }

		public string CeProvider { get; set; }

		public string CeCourseNumber { get; set; }

		/// <summary>A training category. T1 adds TrainingCategories; until then the column carries no foreign key.</summary>
		public int? CategoryId { get; set; }

		/// <summary>Archived trainings are hidden from members and lists but kept for reports and transcripts (T-D21).</summary>
		public DateTime? ArchivedOn { get; set; }

		public string ArchivedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsArchived => ArchivedOn.HasValue;

		/// <summary>True once the scheduled start has passed (or there is none).</summary>
		public bool HasStarted(DateTime utcNow) => !StartsOn.HasValue || StartsOn.Value <= utcNow;

		/// <summary>Question count from a list read (Index, widget, My Training); null when the list did not read it.</summary>
		[NotMapped]
		public int? QuestionCount { get; set; }

		/// <summary>True when the training has a quiz, from the loaded questions or the list's question count.</summary>
		[NotMapped]
		[JsonIgnore]
		public bool HasQuiz => (Questions != null && Questions.Count > 0) || QuestionCount.GetValueOrDefault() > 0;

		/// <summary>Dynamic assignment rules (T-D20). A training without rows keeps its static assignee list.</summary>
		[NotMapped]
		public virtual ICollection<TrainingAudience> Audiences { get; set; }

		public virtual ICollection<TrainingQuestion> Questions { get; set; }

		public virtual ICollection<TrainingAttachment> Attachments { get; set; }

		public virtual ICollection<TrainingUser> Users { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return TrainingId; }
			set { TrainingId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Trainings";

		[NotMapped]
		public string IdName => "TrainingId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Questions", "Attachments", "Users", "Audiences", "QuestionCount", "IsArchived", "HasQuiz" };
	}
}
