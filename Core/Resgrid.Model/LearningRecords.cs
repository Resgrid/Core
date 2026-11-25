using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>Where a learning record came from (§12.2). Persisted in LearningRecords.SourceKind; append only.</summary>
	public enum LearningRecordSourceKinds
	{
		/// <summary>A baseline training completed by the member (one record per assignment cycle).</summary>
		CourseCompletion = 0,

		/// <summary>A class session (T4, LMS). Reserved.</summary>
		ClassSession = 1,

		/// <summary>A Training Record participant: a legacy Log of type Training, or a finalized Records (RMS) training record.</summary>
		TrainingRecord = 2,

		/// <summary>An outside certificate the member submitted with evidence; an officer verifies it.</summary>
		ExternalSubmission = 3,

		/// <summary>A skill sheet evaluation (T4, LMS). Reserved.</summary>
		SkillEvaluation = 4,

		/// <summary>A task book sign-off (T4, LMS). Reserved.</summary>
		TaskBookSignoff = 5,

		/// <summary>A protocol acknowledgment (P2). Reserved.</summary>
		ProtocolAcknowledgment = 6,

		/// <summary>A Resgrid Academy lesson (A1). Reserved.</summary>
		AcademyLesson = 7,

		/// <summary>A certification credit typed in by hand on a certification record.</summary>
		ManualCredit = 8,

		/// <summary>A row of a history import (R-C1).</summary>
		Import = 9,

		/// <summary>Attendance at a calendar event whose type counts as training (check-in/out, R-T9).</summary>
		CalendarAttendance = 10,

		/// <summary>An LMS course completed by the member (T2, one record per enrollment). Not the baseline CourseCompletion.</summary>
		LmsCourseCompletion = 11,

		/// <summary>
		/// A Records occupancy inspection or pre-plan review credited as building familiarization to the person who did it (T3, LMS,
		/// §13a.1). Written by worker 94; one record per visit.
		/// </summary>
		BuildingFamiliarization = 12
	}

	/// <summary>Learning record states (§12.2). Append only.</summary>
	public enum LearningRecordStatuses
	{
		/// <summary>Waiting for an officer: an external submission, or a check-in without a check-out.</summary>
		Pending = 0,

		/// <summary>Counts toward hours and credits. Verified records are immutable; corrections are new records (R-T4).</summary>
		Verified = 1,

		/// <summary>An officer refused it. Visible, never counted.</summary>
		Rejected = 2,

		/// <summary>Replaced or withdrawn: its source changed or was removed. Visible in the audit view, never counted.</summary>
		Voided = 3
	}

	/// <summary>How the training was delivered (Vector parity, §13a). Append only.</summary>
	public enum MethodsOfTraining
	{
		Classroom = 0,
		Online = 1,
		DrillField = 2,
		Virtual = 3,
		SelfStudy = 4,
		OnTheJob = 5
	}

	/// <summary>How an officer resolved a possible duplicate (R-T2). Append only.</summary>
	public enum LearningRecordDuplicateResolutions
	{
		/// <summary>Both records describe different sessions and both count.</summary>
		KeptBoth = 0,

		/// <summary>The record was the same session as the other one and was voided.</summary>
		Merged = 1
	}

	/// <summary>Training category kinds (§12.2). Append only.</summary>
	public enum TrainingCategoryKinds
	{
		Custom = 0,
		IsoFsrs = 1,
		Nccp = 2,
		Osha = 3,
		State = 4
	}

	/// <summary>The period a category's target hours are counted over. Append only.</summary>
	public enum TrainingTargetPeriods
	{
		/// <summary>No target.</summary>
		None = 0,
		PerYear = 1,
		PerMonth = 2,
		/// <summary>Per certification cycle (NREMT NCCP: two years).</summary>
		PerCycle = 3,
		/// <summary>Once, e.g. a new driver's 60 hours.</summary>
		Once = 4,
		/// <summary>Within the member's first year (recruits).</summary>
		FirstYear = 5
	}

	/// <summary>Backfill job states (R-T3). Append only.</summary>
	public enum LearningRecordBackfillStates
	{
		Queued = 0,
		Running = 1,
		Completed = 2,
		Failed = 3,
		Cancelled = 4
	}

	/// <summary>
	/// One entry on a member's transcript (§12.2, R-T1): every source writes exactly one record per person per event
	/// component, idempotently keyed by (<see cref="SourceKind"/>, <see cref="SourceRefId"/>, <see cref="UserId"/>,
	/// <see cref="ComponentKey"/>) among the records that are not voided. A ledger: no foreign key to its source, its own
	/// <see cref="Title"/>, purged with the department by DepartmentId. Baseline (free); LMS tables may reference it, it never
	/// references them (§17.3).
	/// </summary>
	[Table("LearningRecords")]
	public class LearningRecord : IEntity
	{
		public const int MaxTitleLength = 250;
		public const int MaxNotesLength = 4000;
		public const int MaxObjectivesLength = 2000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LearningRecordId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		/// <summary><see cref="LearningRecordSourceKinds"/>.</summary>
		public int SourceKind { get; set; }

		/// <summary>The instance (completion cycle, log, record, check-in, submission, credit, import row), never the course id.</summary>
		public string SourceRefId { get; set; }

		/// <summary>The category or activity line of a multi-activity session; empty when the event has one.</summary>
		public string ComponentKey { get; set; } = string.Empty;

		/// <summary>Links records that describe the same real-world session (R-T2); only the highest precedence one carries hours.</summary>
		public string SessionKey { get; set; }

		public string Title { get; set; }

		/// <summary>The course code the source carried (a Log's course code, a training's external code, an import's code).</summary>
		public string CourseCode { get; set; }

		public DateTime CompletedOn { get; set; }

		/// <summary>Start and end of the session when the source knows them (the duplicate check compares them).</summary>
		public DateTime? StartedOn { get; set; }

		public DateTime? EndedOn { get; set; }

		public decimal Hours { get; set; }

		public int? CategoryId { get; set; }

		/// <summary><see cref="MethodsOfTraining"/>.</summary>
		public int? MethodOfTraining { get; set; }

		public string Objectives { get; set; }

		public string Location { get; set; }

		public string CeProvider { get; set; }

		public string CeCourseNumber { get; set; }

		public decimal? CeHours { get; set; }

		/// <summary>Comma separated user ids of the instructors.</summary>
		public string InstructorUserIds { get; set; }

		/// <summary>Instructor names as the source wrote them (a Log's instructors, an import's instructor column).</summary>
		public string InstructorNames { get; set; }

		/// <summary><see cref="LearningRecordStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>The voided record this one replaces (R-T4).</summary>
		public int? SupersedesId { get; set; }

		public string VoidReason { get; set; }

		public DateTime? VoidedOn { get; set; }

		public string VoidedByUserId { get; set; }

		public string VerifiedByUserId { get; set; }

		public DateTime? VerifiedOn { get; set; }

		/// <summary>An officer's note when verifying or rejecting.</summary>
		public string ReviewNote { get; set; }

		/// <summary>The evidence file (LearningRecordFiles) of an external submission.</summary>
		public int? EvidenceFileId { get; set; }

		/// <summary>Written by the history backfill (R-T3): never credits a certification by itself.</summary>
		public bool IsBackfill { get; set; }

		/// <summary>The id the source system used (imports), or a member's own reference.</summary>
		public string ExternalId { get; set; }

		public string Notes { get; set; }

		/// <summary>Comma separated tags (from the training, or the officer).</summary>
		public string Tags { get; set; }

		/// <summary>Set when the member has another record with overlapping times on the same day (unlinked sources, R-T2).</summary>
		public int? PossibleDuplicateOfId { get; set; }

		public DateTime? DuplicateResolvedOn { get; set; }

		/// <summary><see cref="LearningRecordDuplicateResolutions"/>.</summary>
		public int? DuplicateResolution { get; set; }

		/// <summary>LMS (dark until wave 3): the public verification code of the certificate issued for this record.</summary>
		public string VerificationCode { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool Counts => Status == (int)LearningRecordStatuses.Verified;

		[NotMapped]
		[JsonIgnore]
		public bool IsVoided => Status == (int)LearningRecordStatuses.Voided;

		[NotMapped]
		[JsonIgnore]
		public bool HasUnresolvedDuplicate => PossibleDuplicateOfId.HasValue && !DuplicateResolvedOn.HasValue && !IsVoided;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LearningRecordId; }
			set { LearningRecordId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LearningRecords";

		[NotMapped]
		public string IdName => "LearningRecordId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Counts", "IsVoided", "HasUnresolvedDuplicate" };
	}

	/// <summary>Evidence uploaded with an external submission. Bytes live here, never in a list query of LearningRecords.</summary>
	[Table("LearningRecordFiles")]
	public class LearningRecordFile : IEntity
	{
		public const long MaxBytes = 10 * 1024 * 1024;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LearningRecordFileId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		public string FileName { get; set; }

		public string ContentType { get; set; }

		public byte[] Data { get; set; }

		public DateTime UploadedOn { get; set; }

		public string UploadedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LearningRecordFileId; }
			set { LearningRecordFileId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LearningRecordFiles";

		[NotMapped]
		public string IdName => "LearningRecordFileId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A department's training category (§12.2): custom, or applied from a vertical template (ISO FSRS, NREMT NCCP, OSHA) with
	/// its target hours. Targets only: no ISO point values (§10.6). Soft deleted, because learning records keep the id.
	/// </summary>
	[Table("TrainingCategories")]
	public class TrainingCategory : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int TrainingCategoryId { get; set; }

		public int DepartmentId { get; set; }

		public string Name { get; set; }

		public string Code { get; set; }

		/// <summary><see cref="TrainingCategoryKinds"/>.</summary>
		public int Kind { get; set; }

		public int SortOrder { get; set; }

		public string Description { get; set; }

		/// <summary>Target hours per <see cref="TargetPeriod"/> (null: no target).</summary>
		public decimal? TargetHours { get; set; }

		/// <summary><see cref="TrainingTargetPeriods"/>.</summary>
		public int TargetPeriod { get; set; }

		/// <summary>Who the target applies to, as the template wrote it ("per firefighter", "officers").</summary>
		public string TargetAppliesTo { get; set; }

		/// <summary>Code for state and third-party exports (Vector "RMS code" parity).</summary>
		public string ExternalCode { get; set; }

		/// <summary>Minimum years a record in this category is kept after the member leaves (§14, R-T13). Null: department default.</summary>
		public int? RetentionYears { get; set; }

		/// <summary>The template the category was applied from (TrainingCategoryTemplateCatalog), null for a custom one.</summary>
		public string TemplateKey { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return TrainingCategoryId; }
			set { TrainingCategoryId = (int)value; }
		}

		[NotMapped]
		public string TableName => "TrainingCategories";

		[NotMapped]
		public string IdName => "TrainingCategoryId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A certification type counts the hours of a category (§12.2): a learning record in the category (and component, when
	/// set) becomes a PersonnelCertificationCredit in the certification cycle that contains its CompletedOn (R-T5), for
	/// records completed on or after <see cref="EnabledOn"/>. Backfilled records never credit by themselves (R-T3).
	/// </summary>
	[Table("CategoryCreditMappings")]
	public class CategoryCreditMapping : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CategoryCreditMappingId { get; set; }

		public int DepartmentId { get; set; }

		public int DepartmentCertificationTypeId { get; set; }

		public int CategoryId { get; set; }

		/// <summary>Optional component (activity line) of the category; null counts every component.</summary>
		public string ComponentKey { get; set; }

		public bool IsEnabled { get; set; }

		/// <summary>When the mapping was (last) turned on: records completed before it need "apply history to credits".</summary>
		public DateTime? EnabledOn { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CategoryCreditMappingId; }
			set { CategoryCreditMappingId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CategoryCreditMappings";

		[NotMapped]
		public string IdName => "CategoryCreditMappingId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A resumable, per-department history backfill (R-T3) run by worker 85: training completions (live rows and the T0
	/// completion history) and Training Record participants become Verified, IsBackfill learning records with their original
	/// dates. A dry run only counts. Never credits a certification.
	/// </summary>
	[Table("LearningRecordBackfills")]
	public class LearningRecordBackfill : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LearningRecordBackfillId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="LearningRecordBackfillStates"/>.</summary>
		public int State { get; set; }

		public bool DryRun { get; set; }

		public DateTime RequestedOn { get; set; }

		public string RequestedByUserId { get; set; }

		public DateTime? StartedOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		/// <summary>Where a stopped run resumes: the stage and the last source id it finished ("stage:id").</summary>
		public string ResumeAfter { get; set; }

		public int CompletionsFound { get; set; }

		public int TrainingRecordsFound { get; set; }

		public int RecordsWritten { get; set; }

		/// <summary>Sources that already had a record (idempotent re-runs) or could not be written.</summary>
		public int RecordsSkipped { get; set; }

		public int Attempts { get; set; }

		public string Error { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsFinished => State == (int)LearningRecordBackfillStates.Completed || State == (int)LearningRecordBackfillStates.Failed || State == (int)LearningRecordBackfillStates.Cancelled;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LearningRecordBackfillId; }
			set { LearningRecordBackfillId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LearningRecordBackfills";

		[NotMapped]
		public string IdName => "LearningRecordBackfillId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsFinished" };
	}

	/// <summary>
	/// LMS add-on (dark until wave 3, §25.4a item 7): a department's own certificate layout. An LMS table: it references the
	/// department only and nothing baseline references it (§17.3).
	/// </summary>
	[Table("LmsCertificateTemplates")]
	public class LmsCertificateTemplate : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCertificateTemplateId { get; set; }

		public int DepartmentId { get; set; }

		public string Name { get; set; }

		/// <summary>Sanitized HTML with placeholders ({{member}}, {{course}}, {{date}}, {{hours}}, {{department}}, {{code}}).</summary>
		public string Body { get; set; }

		public bool IsDefault { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCertificateTemplateId; }
			set { LmsCertificateTemplateId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCertificateTemplates";

		[NotMapped]
		public string IdName => "LmsCertificateTemplateId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>How check-in/out hours are rounded (<see cref="TrainingRecordSettings.HoursRoundingMode"/>). Append only.</summary>
	public enum TrainingHoursRoundingModes
	{
		Nearest = 0,
		Down = 1,
		Up = 2
	}

	/// <summary>
	/// ProtoBuf <see cref="DepartmentSettingTypes.TrainingRecordSettings"/> (135), Trainings → Records settings. Missing = the
	/// defaults below (no rounding, member submissions on).
	/// </summary>
	[ProtoContract]
	public class TrainingRecordSettings
	{
		public static readonly int[] AllowedRoundingMinutes = { 0, 5, 6, 10, 15, 30, 60 };

		/// <summary>Calendar check-in/out hours are rounded to this many minutes (0 = exact to the minute).</summary>
		[ProtoMember(1)] public int HoursRoundingMinutes { get; set; }

		/// <summary><see cref="TrainingHoursRoundingModes"/>.</summary>
		[ProtoMember(2)] public int HoursRoundingMode { get; set; }

		/// <summary>Members may not submit outside certificates (external submissions) themselves.</summary>
		[ProtoMember(3)] public bool MemberSubmissionsDisabled { get; set; }

		/// <summary>Minimum years training records are kept after a member leaves, where a category sets none (§14). 0 = no minimum.</summary>
		[ProtoMember(4)] public int DefaultRetentionYears { get; set; }

		public TrainingRecordSettings Normalized()
		{
			return new TrainingRecordSettings
			{
				HoursRoundingMinutes = AllowedRoundingMinutes.Contains(HoursRoundingMinutes) ? HoursRoundingMinutes : 0,
				HoursRoundingMode = Enum.IsDefined(typeof(TrainingHoursRoundingModes), HoursRoundingMode) ? HoursRoundingMode : 0,
				MemberSubmissionsDisabled = MemberSubmissionsDisabled,
				DefaultRetentionYears = Math.Clamp(DefaultRetentionYears, 0, 75)
			};
		}

		/// <summary>Rounds a check-in/out duration to hours per the setting.</summary>
		public decimal RoundHours(TimeSpan duration)
		{
			var minutes = Math.Max(0, duration.TotalMinutes);
			var step = HoursRoundingMinutes;
			if (step > 0)
			{
				var units = minutes / step;
				switch ((TrainingHoursRoundingModes)HoursRoundingMode)
				{
					case TrainingHoursRoundingModes.Down: units = Math.Floor(units); break;
					case TrainingHoursRoundingModes.Up: units = Math.Ceiling(units - 1e-9); break;
					default: units = Math.Round(units, MidpointRounding.AwayFromZero); break;
				}
				minutes = units * step;
			}
			else
			{
				minutes = Math.Round(minutes, MidpointRounding.AwayFromZero);
			}

			return Math.Round((decimal)minutes / 60m, 2, MidpointRounding.AwayFromZero);
		}
	}
}
