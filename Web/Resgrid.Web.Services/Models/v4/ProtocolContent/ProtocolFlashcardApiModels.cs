using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.ProtocolContent
{
	// Round 7A: a protocol's practice flashcards for a member who may read it (Enhanced AI only), the runtime Omni would use. Practice is
	// client-side only: nothing is posted back, graded or recorded. Omni itself is out of scope for this build.

	/// <summary>One practice card: the prompt, the answer, and the protocol section it comes from.</summary>
	public class ProtocolFlashcardData
	{
		/// <summary>The card's id (practice state on the client is keyed by it).</summary>
		public int FlashcardId { get; set; }
		/// <summary>The prompt.</summary>
		public string Front { get; set; }
		/// <summary>The answer, as the cited section states it.</summary>
		public string Back { get; set; }
		/// <summary>The protocol version the card was drafted from (the one in force).</summary>
		public string SourceVersionLabel { get; set; }
		/// <summary>The cited section's heading path.</summary>
		public string SourceHeading { get; set; }

		/// <summary>The web path of the cited section of the protocol version in force.</summary>
		public string SourcePath { get; set; }
	}

	/// <summary>A protocol's practice deck.</summary>
	public class ProtocolFlashcardsResult : StandardApiResponseV4Base
	{
		/// <summary>The cards.</summary>
		public List<ProtocolFlashcardData> Data { get; set; } = new List<ProtocolFlashcardData>();
	}
}
