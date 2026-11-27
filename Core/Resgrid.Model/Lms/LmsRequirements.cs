using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	/// <summary>What a learning requirement asks of a member (T3 item 1). Persisted (LmsRequirements.Kind): append only.</summary>
	public enum LmsRequirementKinds
	{
		/// <summary>Complete an LMS course or a Resgrid Academy lesson (both are LmsCourses). TargetRefId is the LmsCourseId.</summary>
		Course = 0,

		/// <summary>Complete a baseline training. TargetRefId is the TrainingId.</summary>
		Training = 1,

		/// <summary>Log a number of hours in a training category. TargetRefId is the TrainingCategoryId; RequiredHours the hours.</summary>
		CategoryHours = 2,

		/// <summary>Finish a learning path (onboarding). TargetRefId is the LmsPathId.</summary>
		Path = 3,

		/// <summary>
		/// Pass a number of skill evaluations (§13a "N passed evaluations"). Counts verified LearningRecords of the SkillEvaluation kind
		/// whose course code is TargetCode; the evaluation source itself arrives with T4.
		/// </summary>
		Evaluations = 4
	}

	/// <summary>How often a requirement comes round. Persisted (LmsRequirements.Recurrence): append only.</summary>
	public enum LmsRequirementRecurrences
	{
		/// <summary>Once: a completion (or the hours, or the evaluations) satisfies it for good.</summary>
		OneTime = 0,

		/// <summary>Every IntervalMonths, rolling from the last completion ("every 12 months").</summary>
		RollingMonths = 1,

		/// <summary>Each department-local calendar year ("24 hours per calendar year").</summary>
		CalendarYear = 2
	}

	/// <summary>Where a member stands on a requirement (the compliance matrix cell). Persisted as the last computed state: append only.</summary>
	public enum LmsRequirementStates
	{
		/// <summary>Satisfied for the current period.</summary>
		Compliant = 0,

		/// <summary>Not yet satisfied, due later.</summary>
		Pending = 1,

		/// <summary>Due soon, behind pace, or past due within the grace period.</summary>
		AtRisk = 2,

		/// <summary>Past due and past the grace period.</summary>
		Overdue = 3,

		/// <summary>An officer exempted the member (shown as exempt, never as complete, R-C2).</summary>
		Exempt = 4,

		/// <summary>The member is on leave (R-C2): due dates pause.</summary>
		OnLeave = 5,

		/// <summary>The member holds no licensed LMS seat (§17.4): never overdue, never enforced.</summary>
		NotLicensed = 6
	}

	/// <summary>Why an enforcement action was undone. Persisted (LmsRequirementEnforcements.UndoReason): append only.</summary>
	public enum LmsEnforcementUndoReasons
	{
		/// <summary>The member completed the requirement.</summary>
		Completed = 1,

		/// <summary>The LMS add-on lapsed (lapse rule 10: no state stranded by enforcement).</summary>
		Lapse = 2,

		/// <summary>The member was exempted, left the audience, or the requirement no longer enforces.</summary>
		NoLongerApplies = 3,

		/// <summary>An officer undid it.</summary>
		Manual = 4
	}

	/// <summary>
	/// A department learning requirement (T3 item 1, LMS, §17.2): who (audience rules in LmsRequirementAudiences), what (a course, a
	/// training, hours of a category, a path, or N passed evaluations), how often, and when it is due for each member (DueDays after they
	/// enter the audience by joining, a role add or any other audience entry; then the recurrence). Optionally assigns its course or path
	/// to each member on entry (onboarding, item 6) and backs a certification type for training-backed enforcement (item 3, R-L11).
	/// Soft-deleted (DeletedOn); never hard-deleted while members' history refers to it.
	/// </summary>
	[Table("LmsRequirements")]
	public class LmsRequirement : IEntity
	{
		public const int DefaultDueDays = 30;
		public const int DefaultAtRiskDays = 14;
		public const int MaxNameLength = 200;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsRequirementId { get; set; }

		public int DepartmentId { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		/// <summary><see cref="LmsRequirementKinds"/>.</summary>
		public int Kind { get; set; }

		/// <summary>The course, training, category or path id (see <see cref="LmsRequirementKinds"/>).</summary>
		public int? TargetRefId { get; set; }

		/// <summary>The skill code of an Evaluations requirement (matched against LearningRecords.CourseCode, case-insensitive).</summary>
		public string TargetCode { get; set; }

		/// <summary>Hours a CategoryHours requirement asks for per period.</summary>
		public decimal? RequiredHours { get; set; }

		/// <summary>Passed evaluations an Evaluations requirement asks for per period.</summary>
		public int? RequiredCount { get; set; }

		/// <summary><see cref="LmsRequirementRecurrences"/>.</summary>
		public int Recurrence { get; set; }

		/// <summary>The period of a RollingMonths requirement.</summary>
		public int? IntervalMonths { get; set; }

		/// <summary>Days a member has after entering the audience (and after a renewal lapses for someone who just joined).</summary>
		public int DueDays { get; set; } = DefaultDueDays;

		/// <summary>Days after the due date before the member is overdue (they show at risk meanwhile).</summary>
		public int GraceDays { get; set; }

		/// <summary>Days before the due date from which the member shows at risk.</summary>
		public int AtRiskDays { get; set; } = DefaultAtRiskDays;

		/// <summary>Only members who enter the audience after the requirement starts (onboarding: "new members"); existing members are not required.</summary>
		public bool NewMembersOnly { get; set; }

		/// <summary>Assign the course or path to each member when they enter the audience, and again when a renewal opens (onboarding).</summary>
		public bool AutoAssign { get; set; }

		/// <summary>Shown on the Onboarding section (a department-defined path per role, item 6).</summary>
		public bool IsOnboarding { get; set; }

		/// <summary>Training-backed enforcement (item 3): the certification type this requirement backs (Person-scoped, expiring).</summary>
		public int? BacksCertificationTypeId { get; set; }

		/// <summary>Opt-in per requirement (decision 12); also needs the department switch (setting 143) and the add-on.</summary>
		public bool EnforcementEnabled { get; set; }

		/// <summary>Overdue members' supervisors (their group's admins) are told (§13a supervisor alerts).</summary>
		public bool AlertSupervisors { get; set; }

		/// <summary>Comma-separated user ids also told on overdue and, with <see cref="AlertOnCompletion"/>, on completion (§13a named alerts).</summary>
		public string AlertUserIds { get; set; }

		/// <summary>Supervisors and named people are told when a member completes or renews it (§13a on-completion / on-renew alerts).</summary>
		public bool AlertOnCompletion { get; set; }

		public bool IsActive { get; set; } = true;

		/// <summary>
		/// The department's choice for a requirement on a Resgrid Academy lesson, directly or through a path (§15 A2 release rule, M0387):
		/// what a major Academy update of the lesson does here, as <see cref="LmsMajorChangePolicies"/> (Notify by default; Reassign
		/// re-assigns the requirement's completed members with this requirement's due days; Nothing keeps completions quiet). Department
		/// courses ignore it: their publisher chooses (R-L12).
		/// </summary>
		public int AcademyUpdatePolicy { get; set; } = (int)LmsMajorChangePolicies.Notify;

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime? DeletedOn { get; set; }

		public string DeletedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsRequirementId; }
			set { LmsRequirementId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsRequirements";

		[NotMapped]
		public string IdName => "LmsRequirementId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };

		[NotMapped]
		[JsonIgnore]
		public bool IsLive => IsActive && !DeletedOn.HasValue;

		[NotMapped]
		[JsonIgnore]
		public bool IsCounted => Kind == (int)LmsRequirementKinds.CategoryHours || Kind == (int)LmsRequirementKinds.Evaluations;

		/// <summary>The amount a period asks for: hours, evaluations, or one completion.</summary>
		[NotMapped]
		[JsonIgnore]
		public decimal RequiredAmount =>
			Kind == (int)LmsRequirementKinds.CategoryHours ? Math.Max(0m, RequiredHours ?? 0m)
			: Kind == (int)LmsRequirementKinds.Evaluations ? Math.Max(1, RequiredCount ?? 1)
			: 1m;
	}

	/// <summary>One audience rule of a requirement (the shared <see cref="ContentAudienceKinds"/>: roles, groups, certification types, shifts...).</summary>
	[Table("LmsRequirementAudiences")]
	public class LmsRequirementAudience : IEntity, IContentAudienceRule
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsRequirementAudienceId { get; set; }

		public int LmsRequirementId { get; set; }

		public int DepartmentId { get; set; }

		public int Kind { get; set; }

		public string RefId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsRequirementAudienceId; }
			set { LmsRequirementAudienceId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsRequirementAudiences";

		[NotMapped]
		public string IdName => "LmsRequirementAudienceId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One member under one requirement: when they entered its audience (join, role add, any audience entry), their exemption or
	/// extension (R-C2), the last state the engine computed (its transitions raise workflow events 224-228) and the notices already sent
	/// for a due date (claimed before sending). A member who leaves the audience gets LeftOn; re-entering starts a new row.
	/// </summary>
	[Table("LmsRequirementMembers")]
	public class LmsRequirementMember : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsRequirementMemberId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsRequirementId { get; set; }

		public string UserId { get; set; }

		/// <summary>When the member entered the audience (the due-date anchor), UTC.</summary>
		public DateTime EnteredOn { get; set; }

		public DateTime? LeftOn { get; set; }

		/// <summary>The member was already in the audience when a new-members-only requirement started: not required.</summary>
		public bool NotRequired { get; set; }

		/// <summary><see cref="LmsRequirementStates"/> as last computed.</summary>
		public int State { get; set; } = (int)LmsRequirementStates.Pending;

		public DateTime? StateChangedOn { get; set; }

		/// <summary>The effective due moment last computed (leave and lapse applied), UTC.</summary>
		public DateTime? DueOn { get; set; }

		public DateTime? LastCompletedOn { get; set; }

		public DateTime? ExemptOn { get; set; }

		/// <summary>Null with <see cref="ExemptOn"/> set: exempt with no end.</summary>
		public DateTime? ExemptUntil { get; set; }

		public string ExemptReason { get; set; }

		public string ExemptByUserId { get; set; }

		/// <summary>An officer's extension (R-C2): a later due moment for the current period, UTC.</summary>
		public DateTime? ExtendedDueOn { get; set; }

		public string ExtendedByUserId { get; set; }

		public string ExtensionReason { get; set; }

		/// <summary>The due moment whose at-risk notice was sent (claimed before sending).</summary>
		public DateTime? AtRiskNotifiedFor { get; set; }

		/// <summary>The due moment whose overdue notice was sent (claimed before sending).</summary>
		public DateTime? OverdueNotifiedFor { get; set; }

		/// <summary>The due moment the course or path was last auto-assigned for (onboarding and renewals).</summary>
		public DateTime? AssignedForDueOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsRequirementMemberId; }
			set { LmsRequirementMemberId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsRequirementMembers";

		[NotMapped]
		public string IdName => "LmsRequirementMemberId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };

		public bool IsExemptOn(DateTime utcNow) => ExemptOn.HasValue && ExemptOn.Value <= utcNow && (!ExemptUntil.HasValue || ExemptUntil.Value > utcNow);
	}

	/// <summary>
	/// One training-backed enforcement action (item 3, R-L11): the overdue requirement expired the member's certification record. The
	/// record's status and expiry before the action are kept so the action can be undone exactly (on completion, lapse rule 10, or by an
	/// officer); the roles that required the certification are kept so the role removals the certification sweep then made (its existing
	/// grace logic) are restored with it. Audited with the requirement and the learning record that caused it.
	/// </summary>
	[Table("LmsRequirementEnforcements")]
	public class LmsRequirementEnforcement : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsRequirementEnforcementId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsRequirementId { get; set; }

		public int LmsRequirementMemberId { get; set; }

		public string UserId { get; set; }

		public int PersonnelCertificationId { get; set; }

		public int DepartmentCertificationTypeId { get; set; }

		/// <summary>The record's status before the action (<see cref="PersonnelCertificationStatuses"/>).</summary>
		public int PreviousStatus { get; set; }

		public DateTime? PreviousExpiresOn { get; set; }

		/// <summary>The learning record of the completion that lapsed (null when the member never completed it).</summary>
		public int? CauseLearningRecordId { get; set; }

		/// <summary>The due moment the member missed, UTC.</summary>
		public DateTime DueOn { get; set; }

		public DateTime ActedOn { get; set; }

		/// <summary>JSON: the roles (id, name) that required the certification when it was expired.</summary>
		public string RolesJson { get; set; }

		public DateTime? UndoneOn { get; set; }

		/// <summary><see cref="LmsEnforcementUndoReasons"/>.</summary>
		public int? UndoReason { get; set; }

		/// <summary>JSON: the role ids given back on undo.</summary>
		public string RestoredRolesJson { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsRequirementEnforcementId; }
			set { LmsRequirementEnforcementId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsRequirementEnforcements";

		[NotMapped]
		public string IdName => "LmsRequirementEnforcementId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Worker 93's per-department clock: the department-local day of the last claimed daily pass (claimed before it runs, so an
	/// overlapping tick cannot send a day's notices twice), and the last blast-radius refusal.
	/// </summary>
	[Table("LmsRequirementDepartmentStates")]
	public class LmsRequirementDepartmentState : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsRequirementDepartmentStateId { get; set; }

		public int DepartmentId { get; set; }

		public DateTime? LastSweepLocalDate { get; set; }

		public DateTime? LastSweepOn { get; set; }

		public DateTime? LastRefusedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsRequirementDepartmentStateId; }
			set { LmsRequirementDepartmentStateId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsRequirementDepartmentStates";

		[NotMapped]
		public string IdName => "LmsRequirementDepartmentStateId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A completion, an amount of hours or a passed evaluation that counts toward a requirement (projection rows; never stored).</summary>
	public sealed class LmsRequirementFact
	{
		public string UserId { get; set; }

		/// <summary>The course, training, category or path id; 0 for evaluations (matched by <see cref="Code"/>).</summary>
		public int RefId { get; set; }

		public string Code { get; set; }

		public DateTime CompletedOn { get; set; }

		/// <summary>Hours for category records; 1 for everything else.</summary>
		public decimal Amount { get; set; } = 1m;

		/// <summary>The learning record behind it, when there is one (the enforcement cause).</summary>
		public int? LearningRecordId { get; set; }

		/// <summary>Round 7B: <see cref="LearningCompletionMethods"/> of the completion (RecordedByOther: recorded by an instructor).</summary>
		public int CompletionMethod { get; set; }

		/// <summary>Round 9A: every learning record behind a composite fact (a finished path's step completions); null for a single record.</summary>
		public List<int> RecordIds { get; set; }
	}
}
