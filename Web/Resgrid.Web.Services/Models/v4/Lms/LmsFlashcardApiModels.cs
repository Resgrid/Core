using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Lms
{
	// Training T5 (round 6F): a course's practice flashcards for the learner, the runtime Omni would use. Practice is client-side only: nothing is
	// posted back, graded or recorded. Omni itself is out of scope for this build.

	/// <summary>One practice card: the prompt, the answer, and the section it comes from.</summary>
	public class LmsFlashcardData
	{
		public int FlashcardId { get; set; }
		public string Front { get; set; }
		public string Back { get; set; }
		public string SourceTitle { get; set; }
		public string SourceVersionLabel { get; set; }
		public string SourceHeading { get; set; }

		/// <summary>The web path of the cited section (a protocol version's section, or the course lesson).</summary>
		public string SourcePath { get; set; }
	}

	public class LmsFlashcardsResult : StandardApiResponseV4Base
	{
		public List<LmsFlashcardData> Data { get; set; } = new List<LmsFlashcardData>();
	}
}
