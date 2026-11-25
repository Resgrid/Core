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
	// Training T4 part 2 (round 5C, plan §13 T4 items 2-3 and the §13a "Evaluations+" rows): skill sheets (JPR style, on the Checklists
	// execution model's item semantics), skill evaluations signed through the S3 evaluator platform, position task books (NWCG model) and
	// the skill notification digest. All LMS add-on features (§17.2): LMS tables reference baseline tables by id only (no foreign keys),
	// baseline tables never reference them (§17.3). Persisted enums are append only.

	#region Enums

	/// <summary>What a skill sheet is for.</summary>
	public enum LmsSkillSheetKinds
	{
		/// <summary>A JPR-style skill sheet: ordered steps, critical criteria, pass/fail.</summary>
		SkillSheet = 0,

		/// <summary>A field training (FTO / preceptor / CTO) daily observation report: rated categories and narrative.</summary>
		ObservationReport = 1
	}

	/// <summary>The question types of a skill sheet step (§13a "the full question-type set").</summary>
	public enum LmsSkillStepTypes
	{
		/// <summary>A section heading (display only).</summary>
		Heading = 0,
		/// <summary>Instructions to the evaluator (display only).</summary>
		Instructions = 1,
		/// <summary>The evaluator grades the step pass or fail. Scored; a critical step that fails fails the evaluation.</summary>
		PassFail = 2,
		/// <summary>Free text.</summary>
		Text = 3,
		/// <summary>A rating from RatingMin to RatingMax (scored in proportion; below PassingRating fails the step).</summary>
		RatingScale = 4,
		/// <summary>One option (each option carries points and whether it passes).</summary>
		ChooseOne = 5,
		/// <summary>Any number of options.</summary>
		ChooseAny = 6,
		Date = 7,
		Time = 8,
		DateTime = 9,
		/// <summary>Notes the evaluator adds as they happen, each with its time (a script of the evolution).</summary>
		TimestampedNotes = 10,
		/// <summary>A value computed from earlier numeric, rating or duration steps (sum, average, min or max); bounds make it pass/fail.</summary>
		Computed = 11,
		/// <summary>A measured duration in seconds (a timed station); Min/Max bound a pass.</summary>
		Duration = 12,
		/// <summary>A measured number; Min/Max bound a pass.</summary>
		Number = 13,
		/// <summary>Photo, video or file evidence of the step.</summary>
		Evidence = 14
	}

	/// <summary>How a computed step combines its inputs.</summary>
	public enum LmsSkillComputeOps
	{
		Sum = 0,
		Average = 1,
		Min = 2,
		Max = 3
	}

	/// <summary>The answer state of one step (the Checklists execution model's answer states).</summary>
	public enum LmsSkillAnswerStatuses
	{
		Unanswered = 0,
		Answered = 1,
		NotApplicable = 2
	}

	/// <summary>One member, or a crew evaluated together on one form (identical records per member).</summary>
	public enum LmsSkillEvaluationModes
	{
		Individual = 0,
		Crew = 1
	}

	/// <summary>An evaluation's place in its lifecycle.</summary>
	public enum LmsSkillEvaluationStatuses
	{
		/// <summary>The evaluator is grading; answers can change.</summary>
		InProgress = 0,

		/// <summary>Submitted: the result is fixed and the S3 evaluator request waits for the evaluator (and the next signer).</summary>
		AwaitingSignature = 1,

		/// <summary>Every signature is in: learning records are written and the evidence sealed (or sealing pending).</summary>
		Completed = 2,

		/// <summary>Withdrawn by an officer; never counts.</summary>
		Voided = 3
	}

	public enum LmsSkillEvaluationResults
	{
		Pending = 0,
		Passed = 1,
		Failed = 2
	}

	/// <summary>A member's place on a (crew) evaluation.</summary>
	public enum LmsSkillEvaluationMemberStates
	{
		Included = 0,

		/// <summary>Split out to their own evaluation (a failing member of a crew), see SplitToEvaluationId.</summary>
		SplitOut = 1
	}

	/// <summary>The task book families Resgrid ships starters for (§13 T4 item 3).</summary>
	public enum LmsTaskBookFamilies
	{
		/// <summary>Wildland (NWCG model): position codes and task identifiers only.</summary>
		Wildland = 0,
		/// <summary>Company officer qualification levels.</summary>
		Officer = 1,
		/// <summary>Driver/operator.</summary>
		DriverOperator = 2,
		/// <summary>OSHA operator evaluation (e.g. powered industrial trucks: re-evaluate every 3 years).</summary>
		OshaOperator = 3,
		/// <summary>NREMT NCCP skills verification (training officer for EMR/EMT, medical director for AEMT/Paramedic).</summary>
		NccpSkills = 4,
		/// <summary>Field training (FTO / preceptor) with daily observation reports.</summary>
		FieldTraining = 5,
		Custom = 6
	}

	public enum LmsTaskBookStatuses
	{
		/// <summary>Initiated: tasks are being evaluated.</summary>
		Open = 0,

		/// <summary>The final evaluator's recommendation and the certifying official's certification are requested (S3 ordered request).</summary>
		AwaitingCertification = 1,

		/// <summary>Certified: the certification record moved from Trainee to Active.</summary>
		Certified = 2,

		/// <summary>Not completed within its validity (worker 96).</summary>
		Expired = 3,

		Cancelled = 4
	}

	/// <summary>How skill notices are delivered (§13a "notification digests").</summary>
	public enum LmsSkillDigestModes
	{
		Immediate = 0,
		Daily = 1,
		Weekly = 2,
		Off = 3
	}

	/// <summary>What a queued skill notice is about.</summary>
	public enum LmsSkillNotificationKinds
	{
		EvaluationCompleted = 0,
		EvaluationAwaitingSignature = 1,
		TaskBookInitiated = 2,
		TaskSigned = 3,
		TaskBookCertificationRequested = 4,
		TaskBookCertified = 5,
		TaskBookExpired = 6
	}

	#endregion Enums

	#region Skill sheets

	/// <summary>
	/// A skill sheet (T4 item 2): the identity a department keeps across versions. The steps live on a <see cref="LmsSkillSheetVersion"/>;
	/// the sheet holds the catalog fields, who may evaluate and sign next, and which version is in force.
	/// </summary>
	[Table("LmsSkillSheets")]
	public class LmsSkillSheet : IEntity
	{
		public const int TitleMaxLength = 250;
		public const int CodeMaxLength = 100;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsSkillSheetId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The department's code for the skill: the learning record's course code, counted by 4B's "N passed evaluations".</summary>
		public string Code { get; set; }

		public string Title { get; set; }

		public string Description { get; set; }

		/// <summary><see cref="LmsSkillSheetKinds"/>.</summary>
		public int Kind { get; set; }

		/// <summary>The content core's <see cref="ProtocolStatuses"/> (Draft, Published, Retired).</summary>
		public int Status { get; set; }

		/// <summary>The version in force (<see cref="ProtocolVersionStatuses.Published"/>); null until first published.</summary>
		public int? CurrentVersionId { get; set; }

		/// <summary>The department's TrainingCategories row passed evaluations credit (baseline table); null for none.</summary>
		public int? CategoryId { get; set; }

		/// <summary>Who may evaluate (<see cref="SignerEligibility"/> JSON: certification types or roles). Empty: LMS managers only.</summary>
		public string EvaluatorEligibilityJson { get; set; }

		/// <summary>Who the evaluator may pick as the next signer (role or certification). Set: a next signer is required.</summary>
		public string NextSignerEligibilityJson { get; set; }

		/// <summary>A member may evaluate themselves (off by default, §13a "restrict self-evaluation").</summary>
		public bool AllowSelfEvaluation { get; set; }

		/// <summary>The sheet may be run for a whole crew on one form.</summary>
		public bool AllowCrewEvaluation { get; set; }

		/// <summary>The catalog starter it was made from, if any.</summary>
		public string TemplateKey { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime? UpdatedOn { get; set; }
		public string UpdatedByUserId { get; set; }

		/// <summary>Bumped on every write; a save against an older value is refused.</summary>
		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsSkillSheetId; }
			set { LmsSkillSheetId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsSkillSheets";

		[NotMapped]
		public string IdName => "LmsSkillSheetId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One version of a skill sheet on the content core's version shape: <see cref="ProtocolVersionStatuses"/> (Draft → Published →
	/// Superseded), the steps in <see cref="ContentJson"/> (<see cref="LmsSkillSheetContent"/>) and <see cref="ContentHash"/> over them
	/// with the grading settings. Immutable once published; an evaluation keeps the version it started on.
	/// </summary>
	[Table("LmsSkillSheetVersions")]
	public class LmsSkillSheetVersion : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsSkillSheetVersionId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsSkillSheetId { get; set; }

		public int Major { get; set; }

		public int Minor { get; set; }

		/// <summary><see cref="ProtocolVersionStatuses"/>.</summary>
		public int Status { get; set; }

		public string ContentJson { get; set; }

		[MaxLength(64)]
		public string ContentHash { get; set; }

		public string ChangeSummary { get; set; }

		/// <summary>The score (percent of the scored weight) a pass needs; critical steps fail regardless.</summary>
		public decimal PassThreshold { get; set; }

		/// <summary>Failed attempts allowed in the attempt window before an officer must step in (0: no limit).</summary>
		public int MaxAttempts { get; set; }

		/// <summary>The window the attempts are counted in, in days (0: ever).</summary>
		public int AttemptWindowDays { get; set; }

		/// <summary>The elapsed time a pass allows, in seconds (0: no limit). Exceeding it fails the evaluation.</summary>
		public int TimeLimitSeconds { get; set; }

		/// <summary>The hours credited when the evaluation's duration is unknown, in minutes.</summary>
		public int DefaultMinutes { get; set; }

		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime? UpdatedOn { get; set; }
		public string UpdatedByUserId { get; set; }
		public DateTime? PublishedOn { get; set; }
		public string PublishedByUserId { get; set; }
		public DateTime? SupersededOn { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public string Label => Major <= 0 ? null : Major.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + Minor.ToString(System.Globalization.CultureInfo.InvariantCulture);

		[NotMapped]
		[JsonIgnore]
		public LmsSkillGrading Grading => new LmsSkillGrading { PassThreshold = PassThreshold, TimeLimitSeconds = TimeLimitSeconds };

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsSkillSheetVersionId; }
			set { LmsSkillSheetVersionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsSkillSheetVersions";

		[NotMapped]
		public string IdName => "LmsSkillSheetVersionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Label", "Grading" };
	}

	#endregion Skill sheets

	#region Evaluations

	/// <summary>
	/// One evaluation (T4 item 2): an evaluator grades one member, or a crew on one form, against a published sheet version (or a task-book
	/// task without a sheet: one pass/fail step). Answers, timers and annotations live in <see cref="AnswersJson"/>; submitting fixes the
	/// result and asks for the evaluator's L2 signature through S3 (SignatureSubjectKinds.SkillEvaluation, subject "evaluation:{id}"); the
	/// completed request writes the learning records and seals the evidence. <see cref="IdempotencyKey"/> makes an offline submission
	/// (Omni outbox) replay to the same evaluation.
	/// </summary>
	[Table("LmsSkillEvaluations")]
	public class LmsSkillEvaluation : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsSkillEvaluationId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The sheet; null for a task-book task signed without one.</summary>
		public int? LmsSkillSheetId { get; set; }

		public int? LmsSkillSheetVersionId { get; set; }

		/// <summary>The sheet (or task) title when the evaluation started.</summary>
		public string Title { get; set; }

		/// <summary>The sheet code when the evaluation started (the learning record's course code).</summary>
		public string Code { get; set; }

		/// <summary><see cref="LmsSkillEvaluationModes"/>.</summary>
		public int Mode { get; set; }

		/// <summary><see cref="LmsSkillEvaluationStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary><see cref="LmsSkillEvaluationResults"/> (the same for every included member: one form, identical records).</summary>
		public int Result { get; set; }

		public decimal? Score { get; set; }

		public bool CriticalFailed { get; set; }

		public bool TimeExceeded { get; set; }

		public string EvaluatorUserId { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime StartedOn { get; set; }

		public DateTime? SubmittedOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		/// <summary>Elapsed seconds from start to submit (the timer; the learning record's hours).</summary>
		public int DurationSeconds { get; set; }

		/// <summary><see cref="LmsSkillAnswers"/> JSON: per step value, note (annotation) and timestamped notes.</summary>
		public string AnswersJson { get; set; }

		/// <summary>The evaluator's overall comments.</summary>
		public string Notes { get; set; }

		/// <summary>A call the evaluation took place on (evidence for a task-book task).</summary>
		public int? CallId { get; set; }

		/// <summary>A deployment the evaluation took place on (evidence for a task-book task).</summary>
		public int? DeploymentId { get; set; }

		public string Location { get; set; }

		/// <summary>The task book (and the task's key) this evaluation signs a task of.</summary>
		public int? LmsTaskBookId { get; set; }

		public string TaskKey { get; set; }

		/// <summary>A class session's reference when the evaluation is a post-class evaluation (5B's hook), e.g. "class-session:42".</summary>
		public string ClassSessionRef { get; set; }

		/// <summary>The crew evaluation a member was split out of.</summary>
		public int? ParentEvaluationId { get; set; }

		/// <summary>The offline outbox key (unique per department when set).</summary>
		public string IdempotencyKey { get; set; }

		/// <summary>The canonical hash of the submitted evaluation (what the signatures bind to).</summary>
		[MaxLength(64)]
		public string ContentHash { get; set; }

		/// <summary>The S3 evaluator request.</summary>
		public int? SignatureRequestId { get; set; }

		/// <summary>The member the evaluator picked to sign after them (restricted by the sheet's rule).</summary>
		public string NextSignerUserId { get; set; }

		/// <summary>An outside evaluator's printed name (L4 in person, or by link while the LMS add-on is active).</summary>
		public string ExternalEvaluatorName { get; set; }

		public DateTime? VoidedOn { get; set; }
		public string VoidedByUserId { get; set; }
		public string VoidReason { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsOpen => Status == (int)LmsSkillEvaluationStatuses.InProgress;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsSkillEvaluationId; }
			set { LmsSkillEvaluationId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsSkillEvaluations";

		[NotMapped]
		public string IdName => "LmsSkillEvaluationId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsOpen" };
	}

	/// <summary>A member evaluated on an evaluation (one row for an individual evaluation, one per crew member otherwise).</summary>
	[Table("LmsSkillEvaluationMembers")]
	public class LmsSkillEvaluationMember : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsSkillEvaluationMemberId { get; set; }

		public int LmsSkillEvaluationId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		/// <summary><see cref="LmsSkillEvaluationMemberStates"/>.</summary>
		public int State { get; set; }

		/// <summary>This member's attempt number on the sheet (1 for the first).</summary>
		public int AttemptNumber { get; set; }

		/// <summary>The evaluation the member was split out to.</summary>
		public int? SplitToEvaluationId { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsIncluded => State == (int)LmsSkillEvaluationMemberStates.Included;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsSkillEvaluationMemberId; }
			set { LmsSkillEvaluationMemberId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsSkillEvaluationMembers";

		[NotMapped]
		public string IdName => "LmsSkillEvaluationMemberId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsIncluded" };
	}

	/// <summary>Photo, video or file evidence of a step (bytes stored here, never loaded by list queries; the hash goes into the sealed PDF).</summary>
	[Table("LmsSkillEvaluationFiles")]
	public class LmsSkillEvaluationFile : IEntity
	{
		public const int MaxBytes = 30 * 1024 * 1024;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsSkillEvaluationFileId { get; set; }

		public int LmsSkillEvaluationId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The step the evidence belongs to; null for the evaluation as a whole.</summary>
		public string StepKey { get; set; }

		public string FileName { get; set; }

		public string ContentType { get; set; }

		public int Size { get; set; }

		[MaxLength(64)]
		public string Sha256 { get; set; }

		public byte[] Data { get; set; }

		public string UploadedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsImage => ContentType != null && ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsSkillEvaluationFileId; }
			set { LmsSkillEvaluationFileId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsSkillEvaluationFiles";

		[NotMapped]
		public string IdName => "LmsSkillEvaluationFileId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsImage" };
	}

	#endregion Evaluations

	#region Task books

	/// <summary>
	/// A position task book definition (T4 item 3, NWCG model): the position code, the tasks (<see cref="LmsTaskBookTask"/> JSON) with the
	/// evaluations each needs, who may sign tasks, who certifies, the certification type a certification activates, and the validity.
	/// </summary>
	[Table("LmsTaskBookDefinitions")]
	public class LmsTaskBookDefinition : IEntity
	{
		public const int DefaultValidityMonths = 36;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsTaskBookDefinitionId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The position code (e.g. an NWCG code such as FFT2) or the department's own.</summary>
		public string PositionCode { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		/// <summary><see cref="LmsTaskBookFamilies"/>.</summary>
		public int Family { get; set; }

		/// <summary>The tasks, in order (<see cref="LmsTaskBookTask"/> list JSON).</summary>
		public string TasksJson { get; set; }

		/// <summary>The certification type (DepartmentCertificationTypes) a certification moves from Trainee to Active; null for none.</summary>
		public int? CertificationTypeId { get; set; }

		/// <summary>Months a task book stays open after initiation (NWCG: 3 years).</summary>
		public int ValidityMonths { get; set; }

		/// <summary>Months after certification a re-evaluation is due (OSHA operators: 36); 0 for the certification type's own validity.</summary>
		public int RecertifyMonths { get; set; }

		/// <summary>Who may sign tasks (<see cref="SignerEligibility"/> JSON).</summary>
		public string EvaluatorEligibilityJson { get; set; }

		/// <summary>Who may recommend (the final evaluator); empty: the task evaluators' rule.</summary>
		public string FinalEvaluatorEligibilityJson { get; set; }

		/// <summary>Who certifies (the department's certifying official: a role or certification, e.g. the medical director).</summary>
		public string CertifyingEligibilityJson { get; set; }

		public string TemplateKey { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime? UpdatedOn { get; set; }
		public string UpdatedByUserId { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsTaskBookDefinitionId; }
			set { LmsTaskBookDefinitionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsTaskBookDefinitions";

		[NotMapped]
		public string IdName => "LmsTaskBookDefinitionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A member's task book (NWCG model): initiated with a position code and a date, the definition's tasks snapshotted, valid for the
	/// definition's months; tasks are signed by passed evaluations that reference it; the final evaluator recommends and the certifying
	/// official certifies through one S3 ordered request (subject "task-book:{id}"); certification moves the certification record from
	/// Trainee to Active.
	/// </summary>
	[Table("LmsTaskBooks")]
	public class LmsTaskBook : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsTaskBookId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsTaskBookDefinitionId { get; set; }

		/// <summary>The trainee.</summary>
		public string UserId { get; set; }

		public string PositionCode { get; set; }

		public string Name { get; set; }

		public int Family { get; set; }

		/// <summary>The definition's tasks when the book was initiated.</summary>
		public string TasksJson { get; set; }

		public string EvaluatorEligibilityJson { get; set; }

		public string FinalEvaluatorEligibilityJson { get; set; }

		public string CertifyingEligibilityJson { get; set; }

		public int? CertificationTypeId { get; set; }

		/// <summary>The trainee's certification record (Trainee while open, Active once certified).</summary>
		public int? PersonnelCertificationId { get; set; }

		public int RecertifyMonths { get; set; }

		public DateTime InitiatedOn { get; set; }

		public string InitiatedByUserId { get; set; }

		/// <summary>When the book stops being valid if not certified (worker 96 expires it).</summary>
		public DateTime ExpiresOn { get; set; }

		/// <summary><see cref="LmsTaskBookStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>The S3 request: the final evaluator recommends (step 1), the certifying official certifies (step 2).</summary>
		public int? SignatureRequestId { get; set; }

		public string FinalEvaluatorUserId { get; set; }

		public string CertifyingOfficialUserId { get; set; }

		public DateTime? RecommendedOn { get; set; }

		public DateTime? CertifiedOn { get; set; }

		public string CertifiedByUserId { get; set; }

		public DateTime? ClosedOn { get; set; }

		public string CloseReason { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsOpen => Status == (int)LmsTaskBookStatuses.Open || Status == (int)LmsTaskBookStatuses.AwaitingCertification;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsTaskBookId; }
			set { LmsTaskBookId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsTaskBooks";

		[NotMapped]
		public string IdName => "LmsTaskBookId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsOpen" };
	}

	/// <summary>One task of a task book (stored as JSON on the definition and snapshotted on each book).</summary>
	public sealed class LmsTaskBookTask
	{
		/// <summary>Stable within the definition (e.g. "t1"); evaluations reference it.</summary>
		public string Key { get; set; }

		/// <summary>The task identifier as the source numbers it (e.g. "1", "4.2.1").</summary>
		public string Number { get; set; }

		public string Title { get; set; }

		/// <summary>Passed evaluations the task needs (on different dates when more than one).</summary>
		public int RequiredEvaluations { get; set; } = 1;

		/// <summary>Each evaluation must reference a call or a deployment (an incident task).</summary>
		public bool RequiresIncident { get; set; }

		/// <summary>The skill sheet the task is evaluated on; null: one pass/fail step.</summary>
		public int? LmsSkillSheetId { get; set; }
	}

	#endregion Task books

	#region Notifications and settings

	/// <summary>
	/// A queued skill notice (the digest outbox): one row per recipient and event, due now (immediate) or at the department's digest time.
	/// Worker 96 reads only due unsent rows, claims them, skips inactive members and sends one message per recipient.
	/// </summary>
	[Table("LmsSkillNotifications")]
	public class LmsSkillNotification : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsSkillNotificationId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		/// <summary><see cref="LmsSkillNotificationKinds"/>.</summary>
		public int Kind { get; set; }

		/// <summary>The one-line text (English, like the rest of Resgrid's outbound messages).</summary>
		public string Text { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime DueOn { get; set; }

		public DateTime? ClaimedOn { get; set; }

		public DateTime? SentOn { get; set; }

		/// <summary>Sent, or skipped (inactive member, nothing to send through).</summary>
		public bool Skipped { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsSkillNotificationId; }
			set { LmsSkillNotificationId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsSkillNotifications";

		[NotMapped]
		public string IdName => "LmsSkillNotificationId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// ProtoBuf <see cref="DepartmentSettingTypes.LmsSkillSettings"/> (146), Trainings → Skills → Settings (LMS): how evaluation and task book
	/// notices are delivered and to whom. Missing = immediate, evaluated members and admins. The true defaults carry [DefaultValue(true)] so
	/// a false survives protobuf (protobuf-true-default-drop).
	/// </summary>
	[ProtoContract]
	public class LmsSkillSettings
	{
		public const int DefaultDigestHour = 7;

		/// <summary><see cref="LmsSkillDigestModes"/>: immediate notices, a daily or weekly digest, or none.</summary>
		[ProtoMember(1)]
		public int DigestMode { get; set; }

		/// <summary>The department-local hour (0-23) daily and weekly digests are sent.</summary>
		[ProtoMember(2)]
		[DefaultValue(DefaultDigestHour)]
		public int DigestHour { get; set; } = DefaultDigestHour;

		/// <summary>The weekday weekly digests are sent (0 Sunday .. 6 Saturday).</summary>
		[ProtoMember(3)]
		[DefaultValue(1)]
		public int DigestWeekday { get; set; } = 1;

		/// <summary>The evaluated members (and trainees) receive notices about their own evaluations and task books.</summary>
		[ProtoMember(4)]
		[DefaultValue(true)]
		public bool NotifyMembers { get; set; } = true;

		/// <summary>The department admins receive notices about every evaluation and task book.</summary>
		[ProtoMember(5)]
		[DefaultValue(true)]
		public bool NotifyOfficers { get; set; } = true;

		public LmsSkillSettings Normalized() => new LmsSkillSettings
		{
			DigestMode = Enum.IsDefined(typeof(LmsSkillDigestModes), DigestMode) ? DigestMode : (int)LmsSkillDigestModes.Immediate,
			DigestHour = DigestHour < 0 || DigestHour > 23 ? DefaultDigestHour : DigestHour,
			DigestWeekday = DigestWeekday < 0 || DigestWeekday > 6 ? 1 : DigestWeekday,
			NotifyMembers = NotifyMembers,
			NotifyOfficers = NotifyOfficers
		};
	}

	#endregion Notifications and settings
}
