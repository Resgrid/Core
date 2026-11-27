using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Lms
{
	// Round 10 (Omni Responder LMS): the learner's LMS access answer, the self-enrollable catalog, hosted video for bearer clients (signed
	// playlist and caption links), the member's uploaded lesson files and surveys. All times are UTC.

	/// <summary>What the calling member may do with the LMS right now (one answer for the app's Training screen).</summary>
	public class LmsMyAccessData
	{
		/// <summary>The department's Trainings module is on (the free baseline and the Academy need only this).</summary>
		public bool TrainingModuleOn { get; set; }

		/// <summary>The Training.Lms operator flag is on (and the module): department LMS content is not dark.</summary>
		public bool LmsFlagOn { get; set; }

		/// <summary>The LMS add-on is active: true, false, or null when billing could not answer.</summary>
		public bool? AddonActive { get; set; }

		/// <summary>The add-on was active once and is not now: open assignments run out under the lapse rules; nothing new is assigned.</summary>
		public bool IsLapsed { get; set; }

		/// <summary>The member holds one of the department's purchased LMS seats (an LMS-enabled, active member within the packs).</summary>
		public bool CanBeTrained { get; set; }

		/// <summary>The department may use the Enhanced AI learning items (knowledge answers, course practice flashcards) right now.</summary>
		public bool EnhancedAiActive { get; set; }
	}

	public class LmsMyAccessResult : StandardApiResponseV4Base
	{
		public LmsMyAccessData Data { get; set; } = new LmsMyAccessData();
	}

	/// <summary>A self-enrollable department course in the member's catalog (published, in the member's audience).</summary>
	public class LmsCatalogCourseData
	{
		public int CourseId { get; set; }
		/// <summary>The version a new enrollment takes (the one in force).</summary>
		public int CourseVersionId { get; set; }
		public string Title { get; set; }
		public string Summary { get; set; }
		public decimal Hours { get; set; }
		public string VersionLabel { get; set; }
		/// <summary>Lms/Enroll would succeed: the add-on allows new enrollments and the member has no open one.</summary>
		public bool CanEnroll { get; set; }
		/// <summary>The member's open enrollment of this course (open it with Lms/GetCourse).</summary>
		public int? OpenEnrollmentId { get; set; }
	}

	public class LmsCatalogResult : StandardApiResponseV4Base
	{
		public List<LmsCatalogCourseData> Data { get; set; } = new List<LmsCatalogCourseData>();
	}

	/// <summary>One caption track of a video lesson.</summary>
	public class LmsCaptionTrackData
	{
		/// <summary>BCP 47 language, e.g. "en".</summary>
		public string Locale { get; set; }
		public string Label { get; set; }
		/// <summary>A URL a player may load without the bearer token (signed and short-lived for an uploaded track); null when there is none.</summary>
		public string Url { get; set; }
	}

	/// <summary>
	/// A hosted video lesson's streams for a bearer client: the HLS playlist and the progressive MP4 as URLs a native player loads itself.
	/// Both stop working at <see cref="UrlExpiresOn"/> (ask again to resume later); the playlist's segment URLs carry the same lifetime.
	/// </summary>
	public class LmsLessonPlaylistData
	{
		public int EnrollmentId { get; set; }
		public int LessonId { get; set; }
		/// <summary>The signed HLS playlist (application/vnd.apple.mpegurl); null when the video has no HLS rendition.</summary>
		public string PlaylistUrl { get; set; }
		/// <summary>The signed progressive MP4 rendition; null when there is none.</summary>
		public string Mp4Url { get; set; }
		public DateTime UrlExpiresOn { get; set; }
		public int DurationSeconds { get; set; }
		public List<LmsCaptionTrackData> Captions { get; set; } = new List<LmsCaptionTrackData>();
	}

	public class LmsLessonPlaylistResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Null on success. Otherwise an LmsErrors name (LessonLocked, AddonRequired, LapseWindowEnded, NotEntitled, MediaUnavailable when the
		/// video is not ready, archived or not streamable under lapse rule 6) or NotLicensed (the member holds no LMS seat).
		/// </summary>
		public string Error { get; set; }
		public LmsLessonPlaylistData Data { get; set; }
	}

	/// <summary>A file the member uploaded for a file-request lesson (metadata; the bytes come from Lms/GetLessonFile).</summary>
	public class LmsLessonFileData
	{
		public int LessonFileId { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		/// <summary>Bytes.</summary>
		public long Size { get; set; }
		public DateTime UploadedOn { get; set; }
	}

	/// <summary>One question of a survey lesson.</summary>
	public class LmsSurveyQuestionData
	{
		/// <summary>The answer's key in Lms/SubmitSurvey.</summary>
		public string Key { get; set; }
		/// <summary>LmsSurveyQuestionKinds name: Rating (answer "1" to "5"), Choice (answer the option's 0-based index, "0"), Text (optional free text, up to 2000).</summary>
		public string Kind { get; set; }
		public string Text { get; set; }
		public List<string> Options { get; set; } = new List<string>();
	}

	/// <summary>A survey lesson's answers (anonymous: no user id is stored with them), by question key.</summary>
	public class LmsSubmitSurveyInput
	{
		public int EnrollmentId { get; set; }
		public int LessonId { get; set; }
		public Dictionary<string, string> Answers { get; set; } = new Dictionary<string, string>();
	}
}
