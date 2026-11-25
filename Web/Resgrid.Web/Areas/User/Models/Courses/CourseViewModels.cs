using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Lms;

namespace Resgrid.Web.Areas.User.Models.Courses
{
	/// <summary>Who an assignment is for: the member, group and role lists the picker offers (active members only).</summary>
	public class CourseWhoLists
	{
		public Dictionary<string, string> Members { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<int, string> Groups { get; set; } = new Dictionary<int, string>();
		public Dictionary<int, string> Roles { get; set; } = new Dictionary<int, string>();
	}

	/// <summary>The posted audience of an assignment (members, groups, roles, or everyone).</summary>
	public class CourseWhoInput
	{
		public bool Everyone { get; set; }
		public List<string> Members { get; set; } = new List<string>();
		public List<int> Groups { get; set; } = new List<int>();
		public List<int> Roles { get; set; } = new List<int>();
		/// <summary>yyyy-MM-dd in the department's time zone; empty for no due date.</summary>
		public string DueOn { get; set; }

		public List<ContentAudienceRule> Rules()
		{
			if (Everyone)
				return new List<ContentAudienceRule> { new ContentAudienceRule(ContentAudienceKinds.Everyone) };
			return (Members ?? new List<string>()).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => new ContentAudienceRule(ContentAudienceKinds.User, m.Trim()))
				.Concat((Groups ?? new List<int>()).Where(g => g > 0).Select(g => new ContentAudienceRule(ContentAudienceKinds.Group, g.ToString(System.Globalization.CultureInfo.InvariantCulture))))
				.Concat((Roles ?? new List<int>()).Where(r => r > 0).Select(r => new ContentAudienceRule(ContentAudienceKinds.Role, r.ToString(System.Globalization.CultureInfo.InvariantCulture))))
				.ToList();
		}
	}

	public class CoursesPageModel
	{
		public string Message { get; set; }
		public string Error { get; set; }
		public LmsActor Actor { get; set; }

		/// <summary>A UTC moment as a short date in the department's time zone.</summary>
		public Func<DateTime?, string> Date { get; set; } = d => d?.ToShortDateString();
	}

	public class MyCoursesModel : CoursesPageModel
	{
		public List<LmsMyCourse> Courses { get; set; } = new List<LmsMyCourse>();
		public List<LmsCatalogEntry> Catalog { get; set; } = new List<LmsCatalogEntry>();
		public Func<LmsCourse, string> TitleOf { get; set; }
	}

	public class AcademyModel : CoursesPageModel
	{
		public AcademyCatalogView Catalog { get; set; }
		public CourseWhoLists Who { get; set; }
		public string AssignLessonId { get; set; }
		public string AssignPathKey { get; set; }
		public Func<string, string> Text { get; set; }
		public string AcademyHome { get; set; }
	}

	public class AcademyPathModel : CoursesPageModel
	{
		public AcademyRolePath Path { get; set; }
		public List<AcademyPathProgressRow> Rows { get; set; } = new List<AcademyPathProgressRow>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Func<string, string> Text { get; set; }
	}

	public class PlayerModel : CoursesPageModel
	{
		public LmsPlayerView Player { get; set; }
		public string CourseTitle { get; set; }
		public Func<string, string> LessonTitle { get; set; }
		/// <summary>Hosted video: the playlist and MP4 URLs served through Resgrid (the playlist carries signed segment URLs).</summary>
		public string HlsUrl { get; set; }
		public string Mp4Url { get; set; }
		public List<LmsSurveyQuestion> SurveyQuestions { get; set; } = new List<LmsSurveyQuestion>();
		public bool CanStartCrewViewing { get; set; }

		/// <summary>K3 "Ask this course": a department course, the LMS add-on active, and not an assessment or survey lesson.</summary>
		public bool CanAskCourse { get; set; }
	}

	public class AttemptModel : CoursesPageModel
	{
		public LmsAttemptView View { get; set; }
		public int EnrollmentId { get; set; }
	}

	public class AttemptResultModel : CoursesPageModel
	{
		public LmsAttemptResultView Result { get; set; }
		public string MemberName { get; set; }
	}

	public class CourseLibraryModel : CoursesPageModel
	{
		public List<LmsCourseRow> Rows { get; set; } = new List<LmsCourseRow>();
		public List<LmsReviewItem> Reviews { get; set; } = new List<LmsReviewItem>();
		public int PendingGrades { get; set; }
		public bool CanAuthor { get; set; }
		public bool IsLapsed { get; set; }
	}

	public class CourseDetailModel : CoursesPageModel
	{
		public LmsCourse Course { get; set; }
		public LmsCourseRights Rights { get; set; }
		public List<LmsCourseVersion> Versions { get; set; } = new List<LmsCourseVersion>();
		public LmsCourseOutline Current { get; set; }
		public List<LmsEnrollmentRow> Enrollments { get; set; } = new List<LmsEnrollmentRow>();
		public List<LmsCourseOwner> Owners { get; set; } = new List<LmsCourseOwner>();
		public List<LmsCourseAudience> Audience { get; set; } = new List<LmsCourseAudience>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public CourseWhoLists Who { get; set; }
		public bool CanAssignNew { get; set; }
	}

	public class CourseBuilderModel : CoursesPageModel
	{
		public LmsCourseOutline Outline { get; set; }
		public LmsCourseRights Rights { get; set; }
		public LmsLesson Lesson { get; set; }
		public int? ModuleForNewLesson { get; set; }
		public int NewLessonKind { get; set; }
		public bool Editable { get; set; }
		public List<LmsCourseVersionApproval> Approvals { get; set; } = new List<LmsCourseVersionApproval>();
		public List<SignatureVerification> Signatures { get; set; } = new List<SignatureVerification>();
		public Dictionary<int, string> Categories { get; set; } = new Dictionary<int, string>();
		public Dictionary<int, string> CertificationTypes { get; set; } = new Dictionary<int, string>();
		public Dictionary<int, string> CertificateTemplates { get; set; } = new Dictionary<int, string>();
		public Dictionary<int, string> Banks { get; set; } = new Dictionary<int, string>();
		public Dictionary<int, string> ProtocolVersions { get; set; } = new Dictionary<int, string>();
		public Dictionary<int, string> Media { get; set; } = new Dictionary<int, string>();
		public bool MediaConfigured { get; set; }
		public bool CanUploadMedia { get; set; }
		public bool HasOpenComments { get; set; }
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	public class BanksModel : CoursesPageModel
	{
		public List<LmsQuestionBank> Banks { get; set; } = new List<LmsQuestionBank>();
		public bool CanAuthor { get; set; }
	}

	public class BankModel : CoursesPageModel
	{
		public LmsQuestionBank Bank { get; set; }
		public List<LmsQuestion> Questions { get; set; } = new List<LmsQuestion>();
		public LmsQuestion Editing { get; set; }
		public bool CanAuthor { get; set; }
	}

	public class GradingModel : CoursesPageModel
	{
		public List<LmsGradingItem> Items { get; set; } = new List<LmsGradingItem>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	public class ItemAnalysisModel : CoursesPageModel
	{
		public LmsItemAnalysisReport Report { get; set; }
	}

	public class SurveyResultsModel : CoursesPageModel
	{
		public LmsSurveyReport Report { get; set; }
	}

	public class PrintTestModel
	{
		public LmsPrintableTest Test { get; set; }
		public string DepartmentName { get; set; }
	}

	public class StorageModel : CoursesPageModel
	{
		public LmsStorageReport Report { get; set; }
		public List<LmsMediaAsset> Assets { get; set; } = new List<LmsMediaAsset>();
		public bool MediaConfigured { get; set; }
		public bool CanUpload { get; set; }
	}

	public class CrewViewingModel : CoursesPageModel
	{
		public LmsCrewViewing Viewing { get; set; }
		public string LessonTitle { get; set; }
		public string CourseTitle { get; set; }
		public List<LmsCrewViewingAttendee> Attendees { get; set; } = new List<LmsCrewViewingAttendee>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public string ScanUrl { get; set; }
		public string QrSvg { get; set; }
		public bool Open { get; set; }
	}

	public class CrewScanModel : CoursesPageModel
	{
		public int ViewingId { get; set; }
		public string Code { get; set; }
		public string LessonTitle { get; set; }
		public bool Open { get; set; }
	}

	/// <summary>Labels for the LMS enums (keys in the Courses resources).</summary>
	public static class CourseLabels
	{
		public static string LessonKind(int kind) => "Kind." + (Enum.IsDefined(typeof(LmsLessonKinds), kind) ? ((LmsLessonKinds)kind).ToString() : "Blocks");

		public static string LessonIcon(int kind)
		{
			switch ((LmsLessonKinds)kind)
			{
				case LmsLessonKinds.Video: return "fa-play-circle";
				case LmsLessonKinds.Document: return "fa-file-text-o";
				case LmsLessonKinds.ProtocolVersion: return "fa-book";
				case LmsLessonKinds.Assessment: return "fa-check-square-o";
				case LmsLessonKinds.Survey: return "fa-comments-o";
				default: return "fa-align-left";
			}
		}

		public static string QuestionKind(int kind) => "QuestionKind." + (Enum.IsDefined(typeof(LmsQuestionKinds), kind) ? ((LmsQuestionKinds)kind).ToString() : "SingleChoice");

		public static string VersionStatus(int status) => "VersionStatus." + (Enum.IsDefined(typeof(ProtocolVersionStatuses), status) ? ((ProtocolVersionStatuses)status).ToString() : "Draft");

		public static string EnrollmentStatus(int status) => "EnrollmentStatus." + (Enum.IsDefined(typeof(LmsEnrollmentStatuses), status) ? ((LmsEnrollmentStatuses)status).ToString() : "NotStarted");

		public static string Error(LmsErrors error) => "Error." + error;

		public static string LifecycleError(ProtocolLifecycleErrors error) => "Lifecycle." + error;

		public static string MediaStatus(int status) => "MediaStatus." + (Enum.IsDefined(typeof(LmsMediaStatuses), status) ? ((LmsMediaStatuses)status).ToString() : "Queued");
	}
}
