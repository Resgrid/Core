using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Protocols
{
	// Enhanced AI on protocols (round 7A): the protocol flashcards page, the training draft page and the practice cards panel on the protocol view.

	/// <summary>A protocol's flashcards for its authors: drafting (when Enhanced AI is open), the cards and their review state.</summary>
	public class ProtocolFlashcardsModel
	{
		public DispatchProtocol Protocol { get; set; }
		public ProtocolAiStatus Status { get; set; } = new ProtocolAiStatus();
		public List<ProtocolFlashcard> Cards { get; set; } = new List<ProtocolFlashcard>();

		/// <summary>The card being edited in place, if any.</summary>
		public int? EditingCardId { get; set; }

		public string Message { get; set; }
		public string Error { get; set; }
	}

	/// <summary>"Draft a training from this protocol": the request form, then the drafted training to review before it is created.</summary>
	public class ProtocolTrainingDraftModel
	{
		public DispatchProtocol Protocol { get; set; }
		public ProtocolAiStatus Status { get; set; } = new ProtocolAiStatus();
		public int QuestionCount { get; set; } = 5;

		/// <summary>The draft to review; null before drafting.</summary>
		public ProtocolTrainingDraft Draft { get; set; }

		public string Error { get; set; }
	}

	/// <summary>The practice cards panel on the protocol view (members): hidden until its deck has cards.</summary>
	public class ProtocolPracticeCardsPanelModel
	{
		public int ProtocolId { get; set; }
		public string DeckUrl { get; set; }
	}
}
