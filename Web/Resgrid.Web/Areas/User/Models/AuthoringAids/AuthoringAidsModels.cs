using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.AuthoringAids
{
	/// <summary>
	/// The K2 drafting-aids panel a content editor hosts (Shared/_AuthoringAids): which source the drafts come from, which aids to offer and
	/// how an accepted draft reaches the editor. Without an accept URL the panel raises the "rg:authoring-aid:accept" event for the host
	/// page's own script (the training editor puts questions into its quiz builder and the outline into its text); with one it posts the
	/// accepted items there (the course builder saves them to a bank or as modules). A change summary fills the inputs the selector names.
	/// </summary>
	public sealed class AuthoringAidsPanelModel
	{
		public string PanelId { get; set; } = "aids";

		/// <summary><see cref="AuthoringAidSourceKinds"/>.</summary>
		public int SourceKind { get; set; }

		/// <summary>The protocol version, training (0 for a new one) or course version.</summary>
		public int SourceId { get; set; }

		public bool OfferQuiz { get; set; } = true;
		public bool OfferOutline { get; set; } = true;
		public bool OfferChangeSummary { get; set; }

		/// <summary>The editor holding unsaved content (a Quill root or textarea) sent instead of the stored text (training editor).</summary>
		public string ContentSelector { get; set; }

		/// <summary>The editor's title input (training editor).</summary>
		public string TitleSelector { get; set; }

		/// <summary>The editor's "exclude from AI" check box: ticked refuses the aid.</summary>
		public string ExcludeSelector { get; set; }

		/// <summary>Inputs a change summary is written into (the editor's change-summary fields).</summary>
		public string SummaryTargetSelector { get; set; }

		/// <summary>Where accepted quiz questions are posted (the course builder); null raises the accept event instead.</summary>
		public string AcceptQuestionsUrl { get; set; }

		/// <summary>Where an accepted outline is posted (the course builder); null raises the accept event instead.</summary>
		public string AcceptOutlineUrl { get; set; }
	}

	/// <summary>What the panel posts to AuthoringAids/Draft.</summary>
	public sealed class AuthoringAidInput
	{
		public int Kind { get; set; }
		public int SourceKind { get; set; }
		public int SourceId { get; set; }
		public string ContentTitle { get; set; }
		public string ContentHtml { get; set; }
		public bool ExcludeFromAi { get; set; }
		public int QuestionCount { get; set; } = 5;
	}

	/// <summary>The draft as the panel script reads it (camelCase JSON).</summary>
	public sealed class AuthoringAidDraftView
	{
		public string Outcome { get; set; }
		public string Message { get; set; }
		public string Notice { get; set; }
		public string ExchangeId { get; set; }
		public int Kind { get; set; }
		public List<AuthoringAidQuestionDraft> Questions { get; set; } = new List<AuthoringAidQuestionDraft>();
		public List<AuthoringAidOutlineModule> Outline { get; set; } = new List<AuthoringAidOutlineModule>();
		public List<string> Changes { get; set; } = new List<string>();
	}
}
