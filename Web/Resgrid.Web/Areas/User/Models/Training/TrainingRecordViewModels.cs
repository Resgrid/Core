using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Web.Areas.User.Models.Training
{
	/// <summary>How learning records show everywhere (source icon, status pill, enum labels): the transcript, the queue and the reports.</summary>
	public static class LearningRecordPresentation
	{
		public static string SourceKey(int kind) =>
			Enum.IsDefined(typeof(LearningRecordSourceKinds), kind) ? "LearningSource." + (LearningRecordSourceKinds)kind : "Rec.UnknownSource";

		public static string SourceIcon(int kind)
		{
			switch ((LearningRecordSourceKinds)kind)
			{
				case LearningRecordSourceKinds.CourseCompletion: return "fa-graduation-cap";
				case LearningRecordSourceKinds.TrainingRecord: return "fa-clipboard";
				case LearningRecordSourceKinds.ExternalSubmission: return "fa-upload";
				case LearningRecordSourceKinds.CalendarAttendance: return "fa-calendar-check-o";
				case LearningRecordSourceKinds.ManualCredit: return "fa-certificate";
				case LearningRecordSourceKinds.Import: return "fa-file-text-o";
				case LearningRecordSourceKinds.AcademyLesson: return "fa-play-circle";
				case LearningRecordSourceKinds.LmsCourseCompletion: return "fa-university";
				case LearningRecordSourceKinds.BuildingFamiliarization: return "fa-building-o";
				default: return "fa-book";
			}
		}

		public static string StatusKey(int status) =>
			"LearningStatus." + (Enum.IsDefined(typeof(LearningRecordStatuses), status) ? (LearningRecordStatuses)status : LearningRecordStatuses.Pending);

		public static string StatusIcon(int status)
		{
			switch ((LearningRecordStatuses)status)
			{
				case LearningRecordStatuses.Verified: return "fa-check-circle";
				case LearningRecordStatuses.Rejected: return "fa-times-circle";
				case LearningRecordStatuses.Voided: return "fa-ban";
				default: return "fa-hourglass-half";
			}
		}

		public static string StatusClass(int status)
		{
			switch ((LearningRecordStatuses)status)
			{
				case LearningRecordStatuses.Verified: return "label-primary";
				case LearningRecordStatuses.Rejected: return "label-danger";
				case LearningRecordStatuses.Voided: return "label-default";
				default: return "label-warning";
			}
		}

		/// <summary>The method's resource key, or null when none is set.</summary>
		public static string MethodKey(int? method) =>
			method.HasValue && Enum.IsDefined(typeof(MethodsOfTraining), method.Value) ? "TrainingMethod." + (MethodsOfTraining)method.Value : null;

		public static string TargetPeriodKey(int period) =>
			"TargetPeriod." + (Enum.IsDefined(typeof(TrainingTargetPeriods), period) ? (TrainingTargetPeriods)period : TrainingTargetPeriods.None);

		public static string CategoryKindKey(int kind) =>
			"CategoryKind." + (Enum.IsDefined(typeof(TrainingCategoryKinds), kind) ? (TrainingCategoryKinds)kind : TrainingCategoryKinds.Custom);

		public static string BackfillStateKey(int state) =>
			"BackfillState." + (Enum.IsDefined(typeof(LearningRecordBackfillStates), state) ? (LearningRecordBackfillStates)state : LearningRecordBackfillStates.Queued);

		public static string Hours(decimal hours) => hours.ToString("0.##", CultureInfo.CurrentCulture);

		public static string Hours(decimal? hours) => hours.HasValue ? Hours(hours.Value) : null;

		/// <summary>A number for a value attribute (number inputs read invariant).</summary>
		public static string Invariant(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

		/// <summary>A UTC moment as a department-local yyyy-MM-dd (date inputs).</summary>
		public static string IsoDate(DateTime? utc, Department department) =>
			utc.HasValue ? DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).TimeConverter(department).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

		public static string IsoDate(DateTime? local) => local?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	}

	/// <summary>What every Trainings → Records page carries: the department, the officer strip, names and categories for labels.</summary>
	public abstract class TrainingRecordsPageView
	{
		public Department Department { get; set; }

		public TrainingManagementScope Scope { get; set; }

		/// <summary>The officer pages' strip (Queue | Categories | ...); null on member pages.</summary>
		public ModuleTabsView OfficerTabs { get; set; }

		public string Message { get; set; }

		public string Error { get; set; }

		/// <summary>Every member's name, former members included (labels).</summary>
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Current active members; a labelled id outside it belongs to a former member.</summary>
		public HashSet<string> ActiveMemberIds { get; set; }

		/// <summary>The department's categories, removed ones included (records keep the id).</summary>
		public List<TrainingCategory> Categories { get; set; } = new List<TrainingCategory>();

		public string NameOf(string userId) => userId != null && Names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : userId;

		public bool IsFormer(string userId) => ActiveMemberIds != null && !string.IsNullOrWhiteSpace(userId) && !ActiveMemberIds.Contains(userId);

		public TrainingCategory CategoryOf(int? categoryId) => categoryId.HasValue ? Categories.FirstOrDefault(c => c.TrainingCategoryId == categoryId.Value) : null;

		/// <summary>The categories a picker offers (not removed), in the department's order.</summary>
		public List<TrainingCategory> LiveCategories => Categories.Where(c => !c.IsDeleted)
			.OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	/// <summary>A member a picker offers; former members are offered where history may belong to them.</summary>
	public class TrainingRecordMemberOption
	{
		public string UserId { get; set; }

		public string Name { get; set; }

		public bool IsFormer { get; set; }
	}

	/// <summary>Counted hours in one category on the transcript, beside the category's target.</summary>
	public class TranscriptCategoryTotal
	{
		public int? CategoryId { get; set; }

		public string Name { get; set; }

		public decimal Hours { get; set; }

		public int Records { get; set; }

		public decimal? TargetHours { get; set; }

		public int TargetPeriod { get; set; }

		public bool IsRemoved { get; set; }
	}

	/// <summary>One member's transcript (R-T1): every learning record with its source, state, hours and category.</summary>
	public class TrainingTranscriptView : TrainingRecordsPageView
	{
		public string UserId { get; set; }

		public string MemberName { get; set; }

		public bool IsSelf { get; set; }

		public bool IsFormerMember { get; set; }

		/// <summary>yyyy-MM-dd, department-local.</summary>
		public string From { get; set; }

		public string To { get; set; }

		public int? CategoryId { get; set; }

		public int? Source { get; set; }

		public bool Audit { get; set; }

		/// <summary>May open the audit view (voided records and the supersede chain): whoever reviews this member's records.</summary>
		public bool CanAudit { get; set; }

		/// <summary>May void this member's records (an officer for someone else's).</summary>
		public bool CanReview { get; set; }

		/// <summary>Own transcript and the department lets members submit outside certificates.</summary>
		public bool CanSubmit { get; set; }

		public List<LearningRecord> Records { get; set; } = new List<LearningRecord>();

		/// <summary>Round 7B: the recordings behind completions an instructor recorded (id → recording): who, when, from where, the note.</summary>
		public Dictionary<int, TrainingCompletionRecording> Recordings { get; set; } = new Dictionary<int, TrainingCompletionRecording>();

		/// <summary>Round 7B: member names (the recorder of a recorded completion).</summary>
		public Dictionary<string, string> RecorderNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public TrainingCompletionRecording RecordingOf(LearningRecord record) =>
			record?.RecordedCompletionId != null && Recordings.TryGetValue(record.RecordedCompletionId.Value, out var recording) ? recording : null;

		public string RecorderName(string userId) => userId != null && RecorderNames.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : userId;

		/// <summary>Audit view: record id → the chain it belongs to (oldest first).</summary>
		public Dictionary<int, List<LearningRecord>> Chains { get; set; } = new Dictionary<int, List<LearningRecord>>();

		public List<TranscriptCategoryTotal> Totals { get; set; } = new List<TranscriptCategoryTotal>();

		public decimal TotalHours { get; set; }

		public int PendingCount { get; set; }

		/// <summary>Officers: the members whose transcript they may open.</summary>
		public List<TrainingRecordMemberOption> Members { get; set; } = new List<TrainingRecordMemberOption>();

		public bool HasFilter => !string.IsNullOrEmpty(From) || !string.IsNullOrEmpty(To) || CategoryId.HasValue || Source.HasValue;
	}

	/// <summary>The outside-training form (an external submission, R-T1): Pending until an officer verifies it.</summary>
	public class LearningRecordSubmitInput
	{
		public string Title { get; set; }

		/// <summary>yyyy-MM-dd, department-local.</summary>
		public string CompletedOn { get; set; }

		public decimal? Hours { get; set; }

		public int? CategoryId { get; set; }

		public string CeProvider { get; set; }

		public string CeCourseNumber { get; set; }

		public decimal? CeHours { get; set; }

		public int? MethodOfTraining { get; set; }

		public string Notes { get; set; }
	}

	public class TrainingRecordSubmitView : TrainingRecordsPageView
	{
		public LearningRecordSubmitInput Input { get; set; } = new LearningRecordSubmitInput();

		public bool SubmissionsDisabled { get; set; }

		public string Today { get; set; }

		/// <summary>Round 9B: TrainingSubmissionReviewModes of the department (what the page tells the member happens next).</summary>
		public int ReviewMode { get; set; }
	}

	/// <summary>A possible duplicate in the queue: the record and the one it may repeat.</summary>
	public class TrainingRecordDuplicateRow
	{
		public LearningRecord Record { get; set; }

		public LearningRecord Other { get; set; }
	}

	/// <summary>The officer queue: pending records (external submissions, open check-ins) and unresolved possible duplicates.</summary>
	public class TrainingRecordQueueView : TrainingRecordsPageView
	{
		public string CurrentUserId { get; set; }

		public List<LearningRecord> Pending { get; set; } = new List<LearningRecord>();

		public List<TrainingRecordDuplicateRow> Duplicates { get; set; } = new List<TrainingRecordDuplicateRow>();

		/// <summary>Round 9B: members' hours submissions waiting for this officer in Training reviews (no longer listed here).</summary>
		public int SubmissionsAwaiting { get; set; }

		public bool IsOwn(LearningRecord record) => record != null && string.Equals(record.UserId, CurrentUserId, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>The category form; only these fields bind.</summary>
	public class TrainingCategoryInput
	{
		public int TrainingCategoryId { get; set; }

		public string Name { get; set; }

		public string Code { get; set; }

		public int Kind { get; set; }

		public decimal? TargetHours { get; set; }

		public int TargetPeriod { get; set; }

		public string TargetAppliesTo { get; set; }

		public string ExternalCode { get; set; }

		public int? RetentionYears { get; set; }

		public string Description { get; set; }

		public bool IsNew => TrainingCategoryId <= 0;
	}

	public class TrainingCategoriesView : TrainingRecordsPageView
	{
		public TrainingCategoryInput Input { get; set; } = new TrainingCategoryInput();

		public IReadOnlyList<TrainingCategoryTemplate> Templates { get; set; } = new List<TrainingCategoryTemplate>();

		/// <summary>Keys of the templates the department's operating profile suggests (round 9C); they are listed first.</summary>
		public ISet<string> Suggested { get; set; } = new HashSet<string>(StringComparer.Ordinal);

		/// <summary>Codes of the department's categories (a template's categories already applied).</summary>
		public HashSet<string> Codes { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>Bulk categorize (R-T3): uncategorized records grouped by course name and code.</summary>
	public class TrainingCategorizeView : TrainingRecordsPageView
	{
		public string From { get; set; }

		public string To { get; set; }

		public List<LearningRecordGroup> Groups { get; set; } = new List<LearningRecordGroup>();
	}

	/// <summary>The credit mapping form; only these fields bind.</summary>
	public class CategoryCreditMappingInput
	{
		public int CategoryCreditMappingId { get; set; }

		public int DepartmentCertificationTypeId { get; set; }

		public int CategoryId { get; set; }

		public string ComponentKey { get; set; }

		public bool IsEnabled { get; set; } = true;

		public bool IsNew => CategoryCreditMappingId <= 0;
	}

	public class TrainingCreditsView : TrainingRecordsPageView
	{
		public List<CategoryCreditMapping> Mappings { get; set; } = new List<CategoryCreditMapping>();

		/// <summary>Person certification types (not removed, not unit scoped).</summary>
		public List<DepartmentCertificationType> Types { get; set; } = new List<DepartmentCertificationType>();

		public CategoryCreditMappingInput Input { get; set; } = new CategoryCreditMappingInput();

		/// <summary>Department administrators and certification managers change mappings and apply history.</summary>
		public bool CanEdit { get; set; }

		public string TypeName(int typeId)
		{
			var type = Types.FirstOrDefault(t => t.DepartmentCertificationTypeId == typeId);
			return type == null ? null : string.IsNullOrWhiteSpace(type.Code) ? type.Type : type.Type + " (" + type.Code + ")";
		}

		/// <summary>The types that count at least one category: the ones "apply history to credits" can run for.</summary>
		public List<DepartmentCertificationType> MappedTypes => Types.Where(t => Mappings.Any(m => m.DepartmentCertificationTypeId == t.DepartmentCertificationTypeId)).ToList();
	}

	/// <summary>"Apply history to credits" (R-T3): the proposed credits per cycle beside the credits already there.</summary>
	public class TrainingApplyHistoryView : TrainingRecordsPageView
	{
		public DepartmentCertificationType Type { get; set; }

		public HistoryCreditPreview Preview { get; set; } = new HistoryCreditPreview();
	}

	public class TrainingBackfillView : TrainingRecordsPageView
	{
		public LearningRecordBackfill Latest { get; set; }
	}

	/// <summary>History import (R-C1): upload, review the dry run (member matches), then import.</summary>
	public class TrainingImportView : TrainingRecordsPageView
	{
		/// <summary>upload, preview or result.</summary>
		public string Stage { get; set; } = "upload";

		public TrainingHistoryImportResult Result { get; set; }

		/// <summary>The uploaded file, base64, carried to the next step.</summary>
		public string FileData { get; set; }

		public string FileName { get; set; }

		public List<TrainingRecordMemberOption> Members { get; set; } = new List<TrainingRecordMemberOption>();

		public int MaxMegabytes { get; set; }
	}

	/// <summary>The hours report (free): by category, member or month.</summary>
	public class TrainingHoursView : TrainingRecordsPageView
	{
		public string From { get; set; }

		public string To { get; set; }

		public int GroupBy { get; set; }

		public int? CategoryId { get; set; }

		public bool Former { get; set; }

		public bool OnDuty { get; set; }

		public TrainingHoursReport Report { get; set; } = new TrainingHoursReport();
	}

	public class TrainingRecordSettingsView : TrainingRecordsPageView
	{
		public TrainingRecordSettings Settings { get; set; } = new TrainingRecordSettings();
	}

	/// <summary>LMS (dark): the formal ISO FSRS report.</summary>
	public class TrainingIsoReportView : TrainingRecordsPageView
	{
		public string From { get; set; }

		public string To { get; set; }

		public bool Former { get; set; }

		public IsoFsrsReport Report { get; set; } = new IsoFsrsReport();
	}

	/// <summary>LMS (dark): NREMT NCCP progress per member in the current cycle.</summary>
	public class TrainingNccpReportView : TrainingRecordsPageView
	{
		public bool Former { get; set; }

		public NccpProgressReport Report { get; set; } = new NccpProgressReport();
	}

	/// <summary>LMS (dark): the custom certificate template form; only these fields bind.</summary>
	public class CertificateTemplateInput
	{
		public int LmsCertificateTemplateId { get; set; }

		public string Name { get; set; }

		public string Body { get; set; }

		public bool IsDefault { get; set; }

		public bool IsNew => LmsCertificateTemplateId <= 0;
	}

	public class TrainingCertificateTemplatesView : TrainingRecordsPageView
	{
		public List<LmsCertificateTemplate> Templates { get; set; } = new List<LmsCertificateTemplate>();

		public CertificateTemplateInput Input { get; set; } = new CertificateTemplateInput();
	}
}
