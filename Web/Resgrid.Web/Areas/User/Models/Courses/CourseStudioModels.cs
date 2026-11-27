using System.Collections.Generic;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Models.Courses
{
	/// <summary>The Resgrid starter course library page (T5 item 4).</summary>
	public class StarterLibraryModel : CoursesPageModel
	{
		public List<LmsStarterCourseManifest> Starters { get; set; } = new List<LmsStarterCourseManifest>();
		public List<LmsStarterCourseManifest> PartnerSlots { get; set; } = new List<LmsStarterCourseManifest>();
		public LmsStudioStatus Status { get; set; } = new LmsStudioStatus();

		/// <summary>Keys of the starters the department's operating profile suggests (round 9C); they are listed first.</summary>
		public ISet<string> Suggested { get; set; } = new HashSet<string>(System.StringComparer.Ordinal);
	}

	/// <summary>The "draft a course from a protocol or SOP" page (T5 item 5).</summary>
	public class CourseDraftModel : CoursesPageModel
	{
		public LmsStudioStatus Status { get; set; } = new LmsStudioStatus();
		public List<LmsDraftableProtocol> Protocols { get; set; } = new List<LmsDraftableProtocol>();
		public int? ProtocolId { get; set; }
		public long MaxDocumentBytes { get; set; } = LmsCourseDraftRequest.MaxDocumentBytes;
	}

	/// <summary>A course's flashcards for its authors (T5 item 5).</summary>
	public class FlashcardsModel : CoursesPageModel
	{
		public LmsCourse Course { get; set; }
		public List<LmsFlashcard> Cards { get; set; } = new List<LmsFlashcard>();
		public List<LmsFlashcardSourceChoice> Choices { get; set; } = new List<LmsFlashcardSourceChoice>();
		public LmsStudioStatus Status { get; set; } = new LmsStudioStatus();
		public int? EditingCardId { get; set; }
	}

	/// <summary>The course's origin shown on its page and in the builder (AI draft provenance or starter attribution).</summary>
	public class CourseOriginModel
	{
		public LmsCourseOrigin Origin { get; set; }
		public List<LmsLessonCitation> Citations { get; set; } = new List<LmsLessonCitation>();
		public bool ShowCitations { get; set; }
	}

	/// <summary>The practice-cards panel in the course player: where to fetch the learner's deck.</summary>
	public class PracticeCardsPanelModel
	{
		public int EnrollmentId { get; set; }
		public int CourseId { get; set; }
		public string DeckUrl { get; set; }
	}
}
