using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	// Training T5 data exit (round 6D, R-C12, plan §13 T5 item 3): a department exports everything it put in — courses as documented
	// JSON with their media (and a SCORM 1.2 package where the lesson kinds allow), question banks with their keys, and every learning
	// record, attempt and certificate as CSV and as xAPI statements. It works after a lapse (§17.3 rule 2: records stay exportable, rule 1:
	// nothing is deleted): neither the request nor worker 99 asks the add-on gate. Department owners and admins only; every request and
	// download is audited. The zip lives in the LMS store (or, without one, in LmsDataExportBlobs) and its link expires.

	/// <summary>What a data export carries (LmsDataExports.Scope). Persisted: append only.</summary>
	public enum LmsDataExportScopes
	{
		/// <summary>Everything: course content, question banks with their answer keys, attempt answers, programs, and every record.</summary>
		Full = 0,

		/// <summary>Records only: learning records, attempts (scores, no answers or keys), enrollments, certificates, members, categories.</summary>
		RecordsOnly = 1
	}

	/// <summary>A data export's state (LmsDataExports.Status). Persisted: append only.</summary>
	public enum LmsDataExportStatuses
	{
		/// <summary>Waiting for worker 99.</summary>
		Queued = 0,

		/// <summary>Claimed by worker 99 (ClaimedOn says when; a stale claim goes back to the queue).</summary>
		Running = 1,

		/// <summary>The zip is stored and downloadable until ExpiresOn.</summary>
		Completed = 2,

		/// <summary>Building failed <see cref="LmsDataExitRules.MaxAttempts"/> times.</summary>
		Failed = 3,

		/// <summary>The link expired and the stored zip was removed.</summary>
		Expired = 4
	}

	/// <summary>Why a data exit request or download was refused.</summary>
	public enum LmsDataExitErrors
	{
		None = 0,
		NotFound,

		/// <summary>Only the department's owner and admins export the department's training data.</summary>
		NotAuthorized,

		/// <summary>Two exports are already queued or running for the department.</summary>
		TooManyActive,

		/// <summary>The export is not complete yet, or it failed.</summary>
		NotReady,

		/// <summary>The download link expired and the zip was removed.</summary>
		Expired
	}

	/// <summary>
	/// One data export job (worker 99). The zip is in the LMS store at <see cref="ObjectKey"/>, or in LmsDataExportBlobs when the install
	/// has no store (<see cref="StoredInDatabase"/>). Bytes never live on this row, so list queries never carry them.
	/// </summary>
	[Table("LmsDataExports")]
	public class LmsDataExport : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsDataExportId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="LmsDataExportScopes"/>.</summary>
		public int Scope { get; set; }

		/// <summary>Hosted media files are copied into the zip (within the size cap); otherwise the zip lists signed links.</summary>
		public bool IncludeMedia { get; set; }

		/// <summary><see cref="LmsDataExportStatuses"/>.</summary>
		public int Status { get; set; }

		public string RequestedByUserId { get; set; }

		public DateTime RequestedOn { get; set; }

		public DateTime? ClaimedOn { get; set; }

		/// <summary>Claims so far; a job that failed <see cref="LmsDataExitRules.MaxAttempts"/> times stays Failed.</summary>
		public int Attempts { get; set; }

		public DateTime? CompletedOn { get; set; }

		/// <summary>The download link works until this time; worker 99 then removes the zip.</summary>
		public DateTime? ExpiresOn { get; set; }

		[MaxLength(500)]
		public string ObjectKey { get; set; }

		public bool StoredInDatabase { get; set; }

		[MaxLength(250)]
		public string FileName { get; set; }

		public long SizeBytes { get; set; }

		/// <summary>SHA-256 of the zip, lower-case hex.</summary>
		[MaxLength(64)]
		public string Sha256 { get; set; }

		/// <summary>The counts per entity the zip holds (<see cref="LmsDataExportManifest.Counts"/>), JSON.</summary>
		public string CountsJson { get; set; }

		public string Error { get; set; }

		public int DownloadCount { get; set; }

		public DateTime? LastDownloadedOn { get; set; }

		/// <summary>When worker 99 removed the expired zip.</summary>
		public DateTime? PurgedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public Dictionary<string, int> Counts
		{
			get
			{
				if (string.IsNullOrWhiteSpace(CountsJson))
					return new Dictionary<string, int>(StringComparer.Ordinal);
				try
				{
					return JsonConvert.DeserializeObject<Dictionary<string, int>>(CountsJson) ?? new Dictionary<string, int>(StringComparer.Ordinal);
				}
				catch (JsonException)
				{
					return new Dictionary<string, int>(StringComparer.Ordinal);
				}
			}
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsDataExportId; }
			set { LmsDataExportId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsDataExports";

		[NotMapped]
		public string IdName => "LmsDataExportId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Counts" };
	}

	/// <summary>The zip of an export on an install without the LMS store. Read only by the download, never by a list.</summary>
	[Table("LmsDataExportBlobs")]
	public class LmsDataExportBlob : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsDataExportBlobId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsDataExportId { get; set; }

		public byte[] Data { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsDataExportBlobId; }
			set { LmsDataExportBlobId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsDataExportBlobs";

		[NotMapped]
		public string IdName => "LmsDataExportBlobId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A member as the export names them: current, former (removed or disabled) or hidden, never dropped (records are evidence).</summary>
	public sealed class LmsExportMember
	{
		public string UserId { get; set; }
		public string FirstName { get; set; }
		public string LastName { get; set; }
		public string Email { get; set; }

		/// <summary>The department's own member number (UserProfile.IdentificationNumber).</summary>
		public string IdentificationNumber { get; set; }

		/// <summary>Not removed and not disabled (DepartmentMemberStateHelper.IsCurrentMember).</summary>
		public bool IsCurrent { get; set; }

		/// <summary>Also not hidden (DepartmentMemberStateHelper.IsActiveMember): who automation and people reports address.</summary>
		public bool IsActive { get; set; }

		public string Name => ((FirstName ?? string.Empty) + " " + (LastName ?? string.Empty)).Trim();
	}

	/// <summary>A hosted media object of the department as the export sees it: where the zip may copy it from, and its signed link.</summary>
	public sealed class LmsExportMediaFile
	{
		public int LmsMediaAssetId { get; set; }
		public string ObjectKey { get; set; }

		/// <summary>The path inside the zip when the file was copied; null when only a link is listed.</summary>
		public string ZipPath { get; set; }

		public string SignedUrl { get; set; }
		public long? SizeBytes { get; set; }
		public string ContentType { get; set; }
	}

	/// <summary>Everything one department export reads, loaded once by worker 99 (every list is the department's rows only).</summary>
	public sealed class LmsDataExportSnapshot
	{
		public int DepartmentId { get; set; }
		public string DepartmentName { get; set; }
		public LmsDataExportScopes Scope { get; set; }
		public DateTime GeneratedOnUtc { get; set; }
		public int ExportId { get; set; }
		public string RequestedByUserId { get; set; }

		/// <summary>The install's base URL (actor home page, activity ids, certificate verification links).</summary>
		public string BaseUrl { get; set; }

		public List<LmsCourse> Courses { get; set; } = new List<LmsCourse>();
		public List<LmsCourseVersion> Versions { get; set; } = new List<LmsCourseVersion>();
		public List<LmsCourseModule> Modules { get; set; } = new List<LmsCourseModule>();
		public List<LmsLesson> Lessons { get; set; } = new List<LmsLesson>();
		public List<LmsCourseAudience> CourseAudiences { get; set; } = new List<LmsCourseAudience>();
		public List<LmsMediaAsset> MediaAssets { get; set; } = new List<LmsMediaAsset>();
		public List<LmsQuestionBank> Banks { get; set; } = new List<LmsQuestionBank>();
		public List<LmsQuestion> Questions { get; set; } = new List<LmsQuestion>();
		public List<LmsAssessmentAttempt> Attempts { get; set; } = new List<LmsAssessmentAttempt>();
		public List<LmsEnrollment> Enrollments { get; set; } = new List<LmsEnrollment>();
		public List<LearningRecord> LearningRecords { get; set; } = new List<LearningRecord>();
		public List<TrainingCategory> Categories { get; set; } = new List<TrainingCategory>();
		public List<LmsLessonPlan> LessonPlans { get; set; } = new List<LmsLessonPlan>();
		public List<LmsLessonPlanVersion> LessonPlanVersions { get; set; } = new List<LmsLessonPlanVersion>();
		public List<LmsSkillSheet> SkillSheets { get; set; } = new List<LmsSkillSheet>();
		public List<LmsSkillSheetVersion> SkillSheetVersions { get; set; } = new List<LmsSkillSheetVersion>();
		public List<LmsTaskBookDefinition> TaskBookDefinitions { get; set; } = new List<LmsTaskBookDefinition>();
		public List<LmsRequirement> Requirements { get; set; } = new List<LmsRequirement>();
		public List<LmsRequirementAudience> RequirementAudiences { get; set; } = new List<LmsRequirementAudience>();
		public List<LmsTrainingPlan> TrainingPlans { get; set; } = new List<LmsTrainingPlan>();
		public List<LmsTrainingPlanItem> TrainingPlanItems { get; set; } = new List<LmsTrainingPlanItem>();
		public List<LmsPath> Paths { get; set; } = new List<LmsPath>();
		public List<LmsPathStep> PathSteps { get; set; } = new List<LmsPathStep>();
		public List<LmsExportMember> Members { get; set; } = new List<LmsExportMember>();

		/// <summary>Hosted media by asset id: copied files or signed links (filled by the service before the zip is written).</summary>
		public Dictionary<int, LmsExportMediaFile> Media { get; set; } = new Dictionary<int, LmsExportMediaFile>();
	}

	/// <summary>One file in the zip, as the manifest lists it.</summary>
	public sealed class LmsDataExportFileEntry
	{
		public string Path { get; set; }

		/// <summary>Rows (CSV, JSON lines) or top-level items (JSON); null for binary files.</summary>
		public int? Rows { get; set; }

		public long Bytes { get; set; }
		public string Sha256 { get; set; }
	}

	/// <summary>A course the SCORM writer packaged, or left out with the reason.</summary>
	public sealed class LmsDataExportScormEntry
	{
		public int LmsCourseId { get; set; }
		public int? LmsCourseVersionId { get; set; }
		public string Path { get; set; }

		/// <summary>Why the course has no package: "no-published-version" or "lesson-kind:{Kind}".</summary>
		public string SkippedReason { get; set; }
	}

	/// <summary>manifest.json: what the zip holds, documented in docs/training-data-export.md of the Core repository.</summary>
	public sealed class LmsDataExportManifest
	{
		public const string FormatName = "resgrid-training-export";
		public const int CurrentFormatVersion = 1;

		public string Format { get; set; } = FormatName;
		public int FormatVersion { get; set; } = CurrentFormatVersion;
		public string SchemaDocument { get; set; } = "docs/training-data-export.md";
		public int ExportId { get; set; }
		public int DepartmentId { get; set; }
		public string DepartmentName { get; set; }
		public string Scope { get; set; }
		public DateTime GeneratedOnUtc { get; set; }
		public string RequestedByUserId { get; set; }

		/// <summary>Entities and how many of each the zip holds (the completeness check: these equal the department's rows).</summary>
		public Dictionary<string, int> Counts { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

		public List<LmsDataExportFileEntry> Files { get; set; } = new List<LmsDataExportFileEntry>();
		public List<LmsDataExportScormEntry> Scorm { get; set; } = new List<LmsDataExportScormEntry>();

		/// <summary>Hosted media copied into the zip.</summary>
		public int MediaFilesIncluded { get; set; }

		/// <summary>Hosted media listed as signed links only (too large for the cap, or media not requested); the links expire with the export.</summary>
		public int MediaLinksOnly { get; set; }

		/// <summary>What this scope leaves out on purpose.</summary>
		public List<string> Omitted { get; set; } = new List<string>();
	}

	/// <summary>The outcome of a data exit call.</summary>
	public sealed class LmsDataExitResult
	{
		public LmsDataExitErrors Error { get; set; }
		public LmsDataExport Export { get; set; }

		/// <summary>A download: a short-lived signed URL of the store, or the bytes when the zip is kept in the database.</summary>
		public string DownloadUrl { get; set; }
		public byte[] Bytes { get; set; }
		public string FileName { get; set; }

		public bool Succeeded => Error == LmsDataExitErrors.None;

		public static LmsDataExitResult Fail(LmsDataExitErrors error) => new LmsDataExitResult { Error = error };
	}

	/// <summary>What one run of worker 99 did.</summary>
	public sealed class LmsDataExportRunResult
	{
		public int Requeued { get; set; }
		public int Completed { get; set; }
		public int Failed { get; set; }
		public int Purged { get; set; }
	}

	/// <summary>The data exit rules, pure so they can be tested.</summary>
	public static class LmsDataExitRules
	{
		/// <summary>A job is claimed at most this many times; then it stays Failed.</summary>
		public const int MaxAttempts = 3;

		/// <summary>At most this many exports queued or running per department.</summary>
		public const int MaxActivePerDepartment = 2;

		/// <summary>A Running claim older than this is considered abandoned (worker stopped) and goes back to the queue.</summary>
		public static readonly TimeSpan StaleClaim = TimeSpan.FromHours(3);

		/// <summary>How long a single download redirect (a signed store URL) stays valid.</summary>
		public static readonly TimeSpan DownloadUrlLifetime = TimeSpan.FromMinutes(15);

		/// <summary>
		/// Who may export: the department's owner and admins, while they are current members. Deliberately no add-on, flag or module
		/// check: data exit works after a lapse and even with the LMS switched off (§17.3 rules 1 and 2, R-C12).
		/// </summary>
		public static bool CanExport(LmsActor actor) => actor != null && actor.IsCurrentMember && actor.IsDepartmentAdmin;

		/// <summary>The download link works only for a completed, unpurged export before its expiry.</summary>
		public static bool IsDownloadable(LmsDataExport export, DateTime utcNow) =>
			export != null && export.Status == (int)LmsDataExportStatuses.Completed && !export.PurgedOn.HasValue && export.ExpiresOn.HasValue &&
			export.ExpiresOn.Value > utcNow;

		/// <summary>Why <see cref="IsDownloadable"/> said no.</summary>
		public static LmsDataExitErrors DownloadRefusal(LmsDataExport export, DateTime utcNow)
		{
			if (export == null)
				return LmsDataExitErrors.NotFound;
			if (export.Status == (int)LmsDataExportStatuses.Expired || export.PurgedOn.HasValue ||
			    (export.Status == (int)LmsDataExportStatuses.Completed && (!export.ExpiresOn.HasValue || export.ExpiresOn.Value <= utcNow)))
				return LmsDataExitErrors.Expired;
			return export.Status == (int)LmsDataExportStatuses.Completed ? LmsDataExitErrors.None : LmsDataExitErrors.NotReady;
		}

		/// <summary>The expiry of an export completed at <paramref name="completedOnUtc"/>.</summary>
		public static DateTime ExpiresOn(DateTime completedOnUtc, int linkDays) =>
			completedOnUtc.AddDays(Math.Clamp(linkDays <= 0 ? 7 : linkDays, 1, 30));

		/// <summary>Whether the job counts toward <see cref="MaxActivePerDepartment"/>.</summary>
		public static bool IsActive(LmsDataExport export) =>
			export != null && (export.Status == (int)LmsDataExportStatuses.Queued || export.Status == (int)LmsDataExportStatuses.Running);

		/// <summary>A safe file-name part: lower-case letters, digits and dashes, at most 40 characters.</summary>
		public static string Slug(string text, string fallback = "item")
		{
			var builder = new StringBuilder();
			var dash = false;
			foreach (var ch in (text ?? string.Empty).Normalize(NormalizationForm.FormD))
			{
				if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
					continue;
				var lower = char.ToLowerInvariant(ch);
				if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9'))
				{
					builder.Append(lower);
					dash = false;
				}
				else if (!dash && builder.Length > 0)
				{
					builder.Append('-');
					dash = true;
				}

				if (builder.Length >= 40)
					break;
			}

			var slug = builder.ToString().Trim('-');
			return slug.Length == 0 ? fallback : slug;
		}

		/// <summary>The zip's file name, e.g. "training-export-1234-20261125.zip".</summary>
		public static string FileName(LmsDataExport export, DateTime generatedOnUtc) =>
			"training-export-" + export.DepartmentId.ToString(CultureInfo.InvariantCulture) + "-" +
			(export.Scope == (int)LmsDataExportScopes.RecordsOnly ? "records-" : string.Empty) +
			generatedOnUtc.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".zip";

		/// <summary>The SCORM rule: a course is packaged when every lesson of its version is blocks, video or a document (a single-SCO export).</summary>
		public static string ScormRefusal(IEnumerable<LmsLesson> lessons)
		{
			var list = (lessons ?? Enumerable.Empty<LmsLesson>()).ToList();
			if (list.Count == 0)
				return "no-lessons";
			var blocked = list.FirstOrDefault(l => l.Kind != (int)LmsLessonKinds.Blocks && l.Kind != (int)LmsLessonKinds.Video && l.Kind != (int)LmsLessonKinds.Document);
			return blocked == null ? null : "lesson-kind:" + (Enum.IsDefined(typeof(LmsLessonKinds), blocked.Kind) ? ((LmsLessonKinds)blocked.Kind).ToString() : blocked.Kind.ToString(CultureInfo.InvariantCulture));
		}
	}

	/// <summary>
	/// The CSV of every export (data exit files and CE files): RFC 4180, CRLF, UTF-8 without a byte order mark, every cell that a spreadsheet
	/// would evaluate (a leading =, +, -, @, tab or CR) written as text with a leading apostrophe unless it is a plain number.
	/// </summary>
	public static class LmsExportCsv
	{
		private const string FormulaPrefixes = "=+-@\t\r";

		public static string Cell(string value)
		{
			var text = value ?? string.Empty;
			if (text.Length > 0 && FormulaPrefixes.IndexOf(text[0]) >= 0 && !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
				text = "'" + text;
			return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
		}

		public static string Line(IEnumerable<string> cells) => string.Join(",", (cells ?? Enumerable.Empty<string>()).Select(Cell)) + "\r\n";

		public static string Write(IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
		{
			var builder = new StringBuilder();
			if (header != null)
				builder.Append(Line(header));
			foreach (var row in rows ?? Enumerable.Empty<IEnumerable<string>>())
				builder.Append(Line(row));
			return builder.ToString();
		}

		public static string Date(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : string.Empty;

		public static string Number(decimal? value) => value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;

		public static string Number(int? value) => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

		public static string Flag(bool value) => value ? "true" : "false";
	}
}
