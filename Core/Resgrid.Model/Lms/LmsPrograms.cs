using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model.Lms
{
	// Training T3 (part 2, round 4C): the annual training plan (R-C5), competency frameworks (R-C7), training requests with cost
	// and approval (§13a.2), the LMS report catalog with folders and scheduled reports (§13a.3), certification cycle auto-renew and
	// alerts (§13a.1) and building-familiarization credit (§13a.1). All LMS add-on features (§17.2): LMS tables reference baseline
	// tables by id only (no foreign keys), baseline tables never reference them (§17.3). Persisted enums are append only.

	#region Enums

	/// <summary>Who an annual training plan covers.</summary>
	public enum LmsTrainingPlanScopes
	{
		Department = 0,
		Group = 1,
		Shift = 2
	}

	/// <summary>An annual training plan's state. A draft may be edited freely; an adopted plan is the written plan audits ask for.</summary>
	public enum LmsTrainingPlanStatuses
	{
		Draft = 0,
		Adopted = 1,
		Archived = 2
	}

	/// <summary>Where a competency framework came from.</summary>
	public enum LmsCompetencyFrameworkSources
	{
		/// <summary>Applied from Resgrid's catalog: structure and public identifiers only, never a standard's text.</summary>
		Catalog = 0,
		/// <summary>Imported from a CASE 1.1 package (a state's or college's own framework).</summary>
		CaseImport = 1,
		/// <summary>Built by the department.</summary>
		Custom = 2
	}

	/// <summary>What a competency item is tagged on (the evidence the gap report reads).</summary>
	public enum LmsCompetencySubjectKinds
	{
		/// <summary>An LMS course (T2): a completed enrollment is evidence.</summary>
		Course = 0,
		/// <summary>A baseline training: a verified completion on the transcript is evidence.</summary>
		Training = 1,
		/// <summary>A skill sheet (T4). Reserved: no evidence source until T4 lands.</summary>
		SkillSheet = 2,
		/// <summary>A task-book task (T4). Reserved: no evidence source until T4 lands.</summary>
		TaskBookTask = 3,
		/// <summary>A training category: any verified record in it is evidence.</summary>
		Category = 4,
		/// <summary>A certification type: a live record of it is evidence.</summary>
		CertificationType = 5
	}

	/// <summary>A competency item's evidence state for one member.</summary>
	public enum LmsCompetencyEvidenceStates
	{
		/// <summary>Evidence inside the age limit.</summary>
		Current = 0,
		/// <summary>Evidence older than the age limit.</summary>
		Aged = 1,
		/// <summary>No evidence.</summary>
		None = 2,
		/// <summary>Nothing is tagged with the item yet, so nothing can evidence it.</summary>
		NotTagged = 3
	}

	/// <summary>A training request's state.</summary>
	public enum LmsTrainingRequestStatuses
	{
		/// <summary>Waiting for the approver of <see cref="LmsTrainingRequest.CurrentStep"/>.</summary>
		Submitted = 0,
		Approved = 1,
		Rejected = 2,
		/// <summary>The member withdrew it before a final decision.</summary>
		Withdrawn = 3,
		/// <summary>The class was taken: an external submission (T1) waits for an officer's verification.</summary>
		Completed = 4,
		/// <summary>Approved, then not taken.</summary>
		Cancelled = 5
	}

	/// <summary>An approval step of a training request, in order.</summary>
	public enum LmsTrainingRequestSteps
	{
		/// <summary>The member's supervisor: the admins of their group (and its parents), else the department admins.</summary>
		Supervisor = 0,
		/// <summary>A training officer: a department-wide training records manager, or a department admin.</summary>
		TrainingOfficer = 1
	}

	public enum LmsTrainingRequestDecisions
	{
		Approved = 0,
		Rejected = 1
	}

	/// <summary>The tier a catalog report belongs to (§13a.3: completions, overdue, transcript and hours are free).</summary>
	public enum LmsReportTiers
	{
		Free = 0,
		Lms = 1
	}

	/// <summary>A saved report's attachment format.</summary>
	public enum LmsReportFormats
	{
		Csv = 0,
		Pdf = 1
	}

	/// <summary>How often a saved report is sent.</summary>
	public enum LmsReportFrequencies
	{
		/// <summary>Saved only; run by hand.</summary>
		None = 0,
		Daily = 1,
		Weekly = 2,
		Monthly = 3
	}

	/// <summary>The period a report covers, relative to the run date (department-local).</summary>
	public enum LmsReportPeriods
	{
		MonthToDate = 0,
		LastMonth = 1,
		QuarterToDate = 2,
		YearToDate = 3,
		Last12Months = 4,
		LastYear = 5,
		/// <summary>The explicit From/To of the parameters.</summary>
		Custom = 6
	}

	/// <summary>What one scheduled run did.</summary>
	public enum LmsReportRunStatuses
	{
		Sent = 0,
		Failed = 1,
		/// <summary>Nothing sent: the add-on is not active, the owner left, or no recipient is an active member.</summary>
		Skipped = 2
	}

	/// <summary>When a certification type starts its next cycle by itself.</summary>
	public enum LmsAutoRenewModes
	{
		Off = 0,
		/// <summary>As soon as the cycle's renewal hours are met, the next cycle is created (it starts when this one ends).</summary>
		OnCompletion = 1,
		/// <summary>On the cycle's expiry date, when its renewal hours were met by then.</summary>
		OnExpiry = 2
	}

	/// <summary>The certification alerts the LMS sends (and the claims that keep each one, and each auto-renewal, to once).</summary>
	public enum LmsCertificationAlertKinds
	{
		/// <summary>The cycle's renewal hours were met.</summary>
		Completed = 0,
		/// <summary>A cycle started: issued or renewed.</summary>
		Renewed = 1,
		/// <summary>N days before expiry.</summary>
		Expiring = 2,
		/// <summary>The cycle expired without a later cycle.</summary>
		Expired = 3,
		/// <summary>Not an alert: the claim of an automatic renewal (one next cycle per cycle).</summary>
		AutoRenewClaim = 4
	}

	#endregion Enums

	#region Annual training plan (R-C5)

	/// <summary>
	/// An annual training plan (§13 T3 item 3a, R-C5): a year of planned sessions and hours per category per month, for the
	/// department, a group or a shift. The written plan ISO and state audits ask for; the dashboard compares it with the ledger.
	/// </summary>
	[Table("LmsTrainingPlans")]
	public class LmsTrainingPlan : IEntity
	{
		public const int NameMaxLength = 150;
		public const int NotesMaxLength = 4000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsTrainingPlanId { get; set; }

		public int DepartmentId { get; set; }

		public int Year { get; set; }

		public string Name { get; set; }

		/// <summary><see cref="LmsTrainingPlanScopes"/>.</summary>
		public int ScopeKind { get; set; }

		/// <summary>The group or shift id; null for the department.</summary>
		public int? ScopeId { get; set; }

		/// <summary><see cref="LmsTrainingPlanStatuses"/>.</summary>
		public int Status { get; set; }

		public string Notes { get; set; }

		public DateTime? AdoptedOn { get; set; }

		public string AdoptedByUserId { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsTrainingPlanId; }
			set { LmsTrainingPlanId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsTrainingPlans";

		[NotMapped]
		public string IdName => "LmsTrainingPlanId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One planned line of an annual plan: a month, a category, a title, the hours each member should get from one session and how
	/// many sessions. "Make it a calendar item" stores the calendar item it created; <see cref="ClassSessionId"/> is the T4 seam.
	/// </summary>
	[Table("LmsTrainingPlanItems")]
	public class LmsTrainingPlanItem : IEntity
	{
		public const int TitleMaxLength = 200;
		public const int NotesMaxLength = 2000;
		public const decimal MaxHours = 1000m;
		public const int MaxSessions = 100;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsTrainingPlanItemId { get; set; }

		public int LmsTrainingPlanId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>1–12.</summary>
		public int Month { get; set; }

		public int? CategoryId { get; set; }

		public string Title { get; set; }

		/// <summary>Hours per member for one session.</summary>
		public decimal PlannedHours { get; set; }

		public int PlannedSessions { get; set; } = 1;

		/// <summary>The planned date (department-local) when the officer knows it.</summary>
		public DateTime? PlannedOn { get; set; }

		public string Notes { get; set; }

		/// <summary>The calendar item "make it a calendar item" created.</summary>
		public int? CalendarItemId { get; set; }

		/// <summary>T4 seam: the class session this line became. Reserved until class sessions exist.</summary>
		public int? ClassSessionId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public decimal PlannedHoursTotal => PlannedHours * Math.Max(1, PlannedSessions);

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsTrainingPlanItemId; }
			set { LmsTrainingPlanItemId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsTrainingPlanItems";

		[NotMapped]
		public string IdName => "LmsTrainingPlanItemId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "PlannedHoursTotal" };
	}

	#endregion Annual training plan (R-C5)

	#region Competency frameworks (R-C7)

	/// <summary>
	/// A competency framework a department works toward (§13 T3 item 3b, R-C7): applied from Resgrid's catalog (structure and public
	/// identifiers only), imported from CASE 1.1, or built by hand.
	/// </summary>
	[Table("LmsCompetencyFrameworks")]
	public class LmsCompetencyFramework : IEntity
	{
		public const int NameMaxLength = 250;
		public const int DescriptionMaxLength = 2000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCompetencyFrameworkId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The catalog key it was applied from; null for imported and custom frameworks.</summary>
		public string CatalogKey { get; set; }

		public string Name { get; set; }

		public string Publisher { get; set; }

		public string Edition { get; set; }

		public string Description { get; set; }

		/// <summary><see cref="LmsCompetencyFrameworkSources"/>.</summary>
		public int Source { get; set; }

		/// <summary>The CASE CFDocument identifier (re-importing the same document updates it).</summary>
		public string CaseDocumentId { get; set; }

		public string CaseUri { get; set; }

		/// <summary>Evidence older than this many months reads as aged in the gap report (0: never ages).</summary>
		public int EvidenceMaxAgeMonths { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCompetencyFrameworkId; }
			set { LmsCompetencyFrameworkId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCompetencyFrameworks";

		[NotMapped]
		public string IdName => "LmsCompetencyFrameworkId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One node of a framework: a level or chapter, a JPR, a position, a skill. <see cref="HumanCodingScheme"/> carries the public
	/// identifier (a JPR number, an NWCG position code); <see cref="Label"/> a short label (Resgrid's catalog never carries a
	/// standard's text; an imported framework carries what its publisher put in the package).
	/// </summary>
	[Table("LmsCompetencyItems")]
	public class LmsCompetencyItem : IEntity
	{
		public const int LabelMaxLength = 500;
		public const int CodeMaxLength = 100;
		public const int TypeMaxLength = 60;
		public const int IdentifierMaxLength = 200;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCompetencyItemId { get; set; }

		public int LmsCompetencyFrameworkId { get; set; }

		public int DepartmentId { get; set; }

		public int? ParentItemId { get; set; }

		/// <summary>The CASE CFItem identifier or the catalog's item key; unique in the framework.</summary>
		public string Identifier { get; set; }

		public string HumanCodingScheme { get; set; }

		public string Label { get; set; }

		/// <summary>The CASE CFItemType or the catalog's kind (Level, Chapter, JPR, Position, Skill).</summary>
		public string ItemType { get; set; }

		public int SortOrder { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCompetencyItemId; }
			set { LmsCompetencyItemId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCompetencyItems";

		[NotMapped]
		public string IdName => "LmsCompetencyItemId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A competency item tagged on a course, training, category, certification type (or, later, skill sheet / task-book task).</summary>
	[Table("LmsCompetencyAlignments")]
	public class LmsCompetencyAlignment : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCompetencyAlignmentId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCompetencyItemId { get; set; }

		/// <summary><see cref="LmsCompetencySubjectKinds"/>.</summary>
		public int SubjectKind { get; set; }

		/// <summary>The course, training, category or certification type id as text.</summary>
		public string SubjectId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCompetencyAlignmentId; }
			set { LmsCompetencyAlignmentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCompetencyAlignments";

		[NotMapped]
		public string IdName => "LmsCompetencyAlignmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	#endregion Competency frameworks (R-C7)

	#region Training requests

	/// <summary>
	/// A member's request to attend an outside class (§13a.2, Vector training-request parity): cost, an approval chain (supervisor,
	/// then training officer, per <see cref="LmsProgramSettings"/>), and on completion an external submission (T1) the officer verifies.
	/// </summary>
	[Table("LmsTrainingRequests")]
	public class LmsTrainingRequest : IEntity
	{
		public const int TitleMaxLength = 250;
		public const int TextMaxLength = 250;
		public const int JustificationMaxLength = 4000;
		public const decimal MaxCost = 1000000m;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsTrainingRequestId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		public string Title { get; set; }

		public string Provider { get; set; }

		public string Location { get; set; }

		public string CourseCode { get; set; }

		public DateTime? StartsOn { get; set; }

		public DateTime? EndsOn { get; set; }

		public decimal Hours { get; set; }

		public int? CategoryId { get; set; }

		public decimal CostTuition { get; set; }

		public decimal CostTravel { get; set; }

		public decimal CostOther { get; set; }

		/// <summary>ISO 4217 code.</summary>
		public string Currency { get; set; }

		public string Justification { get; set; }

		/// <summary><see cref="LmsTrainingRequestStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>The step waiting for a decision (<see cref="LmsTrainingRequestSteps"/>); null when none is.</summary>
		public int? CurrentStep { get; set; }

		/// <summary>The steps this request needs, comma separated, fixed at submission (settings changes do not move open requests).</summary>
		public string Steps { get; set; }

		public DateTime SubmittedOn { get; set; }

		public DateTime? DecidedOn { get; set; }

		public string DecidedByUserId { get; set; }

		/// <summary>The external submission written when the member completed the class.</summary>
		public int? LearningRecordId { get; set; }

		public DateTime? CompletedOn { get; set; }

		/// <summary>What the class actually cost, when the member reported it on completion.</summary>
		public decimal? ActualCost { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public decimal RequestedCost => CostTuition + CostTravel + CostOther;

		/// <summary>The cost a report counts: the actual one when reported, else the requested one.</summary>
		[NotMapped]
		[JsonIgnore]
		public decimal EffectiveCost => ActualCost ?? RequestedCost;

		[NotMapped]
		[JsonIgnore]
		public bool IsOpen => Status == (int)LmsTrainingRequestStatuses.Submitted;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsTrainingRequestId; }
			set { LmsTrainingRequestId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsTrainingRequests";

		[NotMapped]
		public string IdName => "LmsTrainingRequestId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "RequestedCost", "EffectiveCost", "IsOpen" };

		/// <summary>The steps parsed from <see cref="Steps"/>, in order.</summary>
		public List<LmsTrainingRequestSteps> StepList() =>
			(Steps ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(s => int.TryParse(s.Trim(), out var v) && Enum.IsDefined(typeof(LmsTrainingRequestSteps), v) ? (LmsTrainingRequestSteps?)v : null)
				.Where(s => s.HasValue).Select(s => s.Value).ToList();
	}

	/// <summary>One approver's decision on one step of a training request.</summary>
	[Table("LmsTrainingRequestDecisions")]
	public class LmsTrainingRequestDecision : IEntity
	{
		public const int NoteMaxLength = 2000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsTrainingRequestDecisionId { get; set; }

		public int LmsTrainingRequestId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="LmsTrainingRequestSteps"/>.</summary>
		public int Step { get; set; }

		/// <summary><see cref="LmsTrainingRequestDecisions"/>.</summary>
		public int Decision { get; set; }

		public string DecidedByUserId { get; set; }

		public string Note { get; set; }

		public DateTime DecidedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsTrainingRequestDecisionId; }
			set { LmsTrainingRequestDecisionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsTrainingRequestDecisions";

		[NotMapped]
		public string IdName => "LmsTrainingRequestDecisionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	#endregion Training requests

	#region Report catalog, folders and scheduled reports

	/// <summary>A folder of saved reports.</summary>
	[Table("LmsReportFolders")]
	public class LmsReportFolder : IEntity
	{
		public const int NameMaxLength = 100;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsReportFolderId { get; set; }

		public int DepartmentId { get; set; }

		public string Name { get; set; }

		public int SortOrder { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsReportFolderId; }
			set { LmsReportFolderId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsReportFolders";

		[NotMapped]
		public string IdName => "LmsReportFolderId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A saved report: a catalog report with its parameters, in a folder, optionally scheduled (daily, weekly, monthly at a
	/// department-local hour) to recipients as a CSV or PDF attachment. Worker 94 sends the due ones.
	/// </summary>
	[Table("LmsReportDefinitions")]
	public class LmsReportDefinition : IEntity
	{
		public const int NameMaxLength = 150;
		public const int MaxRecipients = 50;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsReportDefinitionId { get; set; }

		public int DepartmentId { get; set; }

		public int? LmsReportFolderId { get; set; }

		/// <summary>The catalog report's key (<c>ILmsReportSource.Key</c>).</summary>
		public string ReportKey { get; set; }

		public string Name { get; set; }

		/// <summary>JSON <see cref="LmsReportParameters"/>.</summary>
		public string ParametersJson { get; set; }

		/// <summary><see cref="LmsReportFormats"/>.</summary>
		public int Format { get; set; }

		/// <summary><see cref="LmsReportFrequencies"/>.</summary>
		public int Frequency { get; set; }

		/// <summary>Weekly: the day (<see cref="System.DayOfWeek"/>).</summary>
		public int DayOfWeek { get; set; }

		/// <summary>Monthly: the day of the month (1–28; a short month runs on its last day).</summary>
		public int DayOfMonth { get; set; } = 1;

		/// <summary>The department-local hour it is sent at (0–23).</summary>
		public int HourLocal { get; set; } = 6;

		/// <summary>Comma separated member ids.</summary>
		public string RecipientUserIds { get; set; }

		/// <summary>When it is next due (UTC); null when not scheduled.</summary>
		public DateTime? NextRunUtc { get; set; }

		public DateTime? LastRunUtc { get; set; }

		/// <summary><see cref="LmsReportRunStatuses"/> of the last run.</summary>
		public int? LastRunStatus { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsReportDefinitionId; }
			set { LmsReportDefinitionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsReportDefinitions";

		[NotMapped]
		public string IdName => "LmsReportDefinitionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };

		public List<string> Recipients() =>
			(RecipientUserIds ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	/// <summary>One scheduled run of a saved report (the history the folder page shows).</summary>
	[Table("LmsReportRuns")]
	public class LmsReportRun : IEntity
	{
		public const int ErrorMaxLength = 500;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsReportRunId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsReportDefinitionId { get; set; }

		public DateTime RanOn { get; set; }

		/// <summary><see cref="LmsReportRunStatuses"/>.</summary>
		public int Status { get; set; }

		public int Recipients { get; set; }

		public int RowTotal { get; set; }

		public string Error { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsReportRunId; }
			set { LmsReportRunId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsReportRuns";

		[NotMapped]
		public string IdName => "LmsReportRunId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	#endregion Report catalog, folders and scheduled reports

	#region Certification cycles and alerts

	/// <summary>
	/// A certification type's LMS cycle rule (§13a.1): auto-renew (the next cycle starts by itself once the renewal hours are met) and
	/// alerts to supervisors, admins and named members on completion, renewal, approaching expiry and expiry. One per type.
	/// </summary>
	[Table("LmsCertificationCycleRules")]
	public class LmsCertificationCycleRule : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCertificationCycleRuleId { get; set; }

		public int DepartmentId { get; set; }

		public int DepartmentCertificationTypeId { get; set; }

		/// <summary><see cref="LmsAutoRenewModes"/>.</summary>
		public int AutoRenewMode { get; set; }

		public bool AlertOnCompletion { get; set; }

		public bool AlertOnRenewal { get; set; }

		/// <summary>Days before expiry the alert fires on, e.g. "30,7"; empty for none.</summary>
		public string AlertLeadDaysCsv { get; set; }

		public bool AlertOnExpired { get; set; }

		public bool NotifySupervisors { get; set; }

		public bool NotifyAdmins { get; set; }

		/// <summary>Comma separated member ids that receive every alert of the type.</summary>
		public string NamedRecipientUserIds { get; set; }

		/// <summary>When the alerts were (last) turned on: a cycle that started earlier never sends a renewal alert.</summary>
		public DateTime AlertsFrom { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool HasAlerts => (AlertOnCompletion || AlertOnRenewal || AlertOnExpired || LeadDays().Count > 0) &&
			(NotifySupervisors || NotifyAdmins || NamedRecipients().Count > 0);

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCertificationCycleRuleId; }
			set { LmsCertificationCycleRuleId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCertificationCycleRules";

		[NotMapped]
		public string IdName => "LmsCertificationCycleRuleId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "HasAlerts" };

		public List<int> LeadDays()
		{
			var days = new SortedSet<int>();
			foreach (var part in (AlertLeadDaysCsv ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
				if (int.TryParse(part.Trim(), out var day) && day > 0 && day <= 365)
					days.Add(day);
			return days.Reverse().ToList();
		}

		public List<string> NamedRecipients() =>
			(NamedRecipientUserIds ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	/// <summary>
	/// A sent certification alert, or an auto-renewal claim: unique per (record, kind, key), inserted before the alert is sent or the
	/// next cycle is created, so a repeated or overlapping sweep sends and renews once.
	/// </summary>
	[Table("LmsCertificationAlerts")]
	public class LmsCertificationAlert : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCertificationAlertId { get; set; }

		public int DepartmentId { get; set; }

		public int PersonnelCertificationId { get; set; }

		/// <summary><see cref="LmsCertificationAlertKinds"/>.</summary>
		public int Kind { get; set; }

		/// <summary>What makes the alert distinct (the expiry date and lead day, the cycle start).</summary>
		public string AlertKey { get; set; }

		public DateTime SentOn { get; set; }

		public int Recipients { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCertificationAlertId; }
			set { LmsCertificationAlertId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCertificationAlerts";

		[NotMapped]
		public string IdName => "LmsCertificationAlertId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Worker 94's per-department state: the day the cycle sweep last claimed and how far familiarization credit has read.</summary>
	[Table("LmsProgramStates")]
	public class LmsProgramState : IEntity
	{
		[Key]
		[Required]
		public int DepartmentId { get; set; }

		/// <summary>The department-local date the certification cycle sweep last claimed.</summary>
		public DateTime? CycleSweepLocalDate { get; set; }

		/// <summary>Records changed up to this time (UTC) were read for familiarization credit.</summary>
		public DateTime? FamiliarizationScannedThrough { get; set; }

		public DateTime UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentId; }
			set { DepartmentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsProgramStates";

		[NotMapped]
		public string IdName => "DepartmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	#endregion Certification cycles and alerts

	#region Settings

	/// <summary>
	/// ProtoBuf <see cref="DepartmentSettingTypes.LmsProgramSettings"/> (144), Trainings → Programs → Settings (LMS): training request
	/// approval steps and currency, and building-familiarization credit from Records. Missing = both approval steps, USD, no credit.
	/// The true defaults carry [DefaultValue(true)] so a false survives protobuf (protobuf-true-default-drop).
	/// </summary>
	[ProtoContract]
	public class LmsProgramSettings
	{
		public const int DefaultFamiliarizationMinutes = 60;

		/// <summary>The member's supervisor approves a training request first.</summary>
		[ProtoMember(1)]
		[DefaultValue(true)]
		public bool RequestSupervisorApproval { get; set; } = true;

		/// <summary>A training officer approves a training request (after the supervisor when both are on).</summary>
		[ProtoMember(2)]
		[DefaultValue(true)]
		public bool RequestTrainingOfficerApproval { get; set; } = true;

		/// <summary>The ISO 4217 currency training request costs are entered in.</summary>
		[ProtoMember(3)]
		public string RequestCurrency { get; set; }

		/// <summary>Records occupancy inspections and pre-plan reviews credit the person who did them.</summary>
		[ProtoMember(4)]
		public bool FamiliarizationEnabled { get; set; }

		/// <summary>The training category the credit lands in (e.g. the ISO pre-fire planning category).</summary>
		[ProtoMember(5)]
		public int FamiliarizationCategoryId { get; set; }

		/// <summary>Minutes credited per visit when the record has no start and end (0: 60).</summary>
		[ProtoMember(6)]
		public int FamiliarizationMinutes { get; set; }

		/// <summary>Completed occupancy inspections earn credit.</summary>
		[ProtoMember(7)]
		[DefaultValue(true)]
		public bool FamiliarizationFromInspections { get; set; } = true;

		/// <summary>Occupancy and contact pre-plan reviews earn credit.</summary>
		[ProtoMember(8)]
		[DefaultValue(true)]
		public bool FamiliarizationFromPreplanReviews { get; set; } = true;

		public LmsProgramSettings Normalized()
		{
			var currency = (RequestCurrency ?? string.Empty).Trim().ToUpperInvariant();
			return new LmsProgramSettings
			{
				RequestSupervisorApproval = RequestSupervisorApproval,
				RequestTrainingOfficerApproval = RequestTrainingOfficerApproval,
				RequestCurrency = currency.Length == 3 && currency.All(char.IsLetter) ? currency : "USD",
				FamiliarizationEnabled = FamiliarizationEnabled,
				FamiliarizationCategoryId = Math.Max(0, FamiliarizationCategoryId),
				FamiliarizationMinutes = FamiliarizationMinutes <= 0 ? DefaultFamiliarizationMinutes : Math.Min(FamiliarizationMinutes, 24 * 60),
				FamiliarizationFromInspections = FamiliarizationFromInspections,
				FamiliarizationFromPreplanReviews = FamiliarizationFromPreplanReviews
			};
		}

		/// <summary>The approval steps a request submitted now needs, in order.</summary>
		public List<LmsTrainingRequestSteps> RequestSteps()
		{
			var steps = new List<LmsTrainingRequestSteps>();
			if (RequestSupervisorApproval)
				steps.Add(LmsTrainingRequestSteps.Supervisor);
			if (RequestTrainingOfficerApproval)
				steps.Add(LmsTrainingRequestSteps.TrainingOfficer);
			return steps;
		}
	}

	#endregion Settings
}
