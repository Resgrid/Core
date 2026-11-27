using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Knowledge
{
	/// <summary>The "Ask" panel's context: where it sits and what it is scoped to.</summary>
	public class KnowledgeAskPanelModel
	{
		/// <summary><see cref="KnowledgeAskSurfaces"/>.</summary>
		public int Surface { get; set; }

		/// <summary>Ask about one item only (<see cref="KnowledgeSourceKinds"/>).</summary>
		public int? ScopeKind { get; set; }

		public string ScopeItemId { get; set; }

		/// <summary>K3: the version of the scoped item (the course version the learner takes in "Ask this course").</summary>
		public string ScopeVersionId { get; set; }

		/// <summary>The call; without <see cref="CallProtocolId"/> the panel asks about the whole call (K3).</summary>
		public int? CallId { get; set; }

		public int? CallProtocolId { get; set; }

		/// <summary>A distinct element id prefix when a page shows several panels (one per call protocol).</summary>
		public string PanelId { get; set; } = "kask";

		public bool Collapsed { get; set; }

		/// <summary>A heading and help text of the host's own (the Help search's "How do I…?"); null keeps the panel's.</summary>
		public string Header { get; set; }

		public string HelpText { get; set; }
	}

	/// <summary>The form the Ask panel posts.</summary>
	public class KnowledgeAskInput
	{
		public string Question { get; set; }
		public int Surface { get; set; }
		public int? ScopeKind { get; set; }
		public string ScopeItemId { get; set; }
		public string ScopeVersionId { get; set; }
		public int? CallId { get; set; }
		public int? CallProtocolId { get; set; }
	}

	/// <summary>The "Practice" panel's context (K3 tutor): the protocol whose version in force the scenarios are written from.</summary>
	public class KnowledgePracticePanelModel
	{
		public int ProtocolId { get; set; }

		public string PanelId { get; set; } = "kpractice";
	}

	public class KnowledgePracticeQuestionView
	{
		public string Key { get; set; }
		public string Text { get; set; }
		public List<string> Refs { get; set; } = new List<string>();
	}

	/// <summary>The JSON a new practice scenario returns, text already localized.</summary>
	public class KnowledgePracticeScenarioView
	{
		public string Outcome { get; set; }
		public string Message { get; set; }
		public string ExchangeId { get; set; }
		public string Scenario { get; set; }
		public string Version { get; set; }
		public List<KnowledgePracticeQuestionView> Questions { get; set; } = new List<KnowledgePracticeQuestionView>();
		public List<KnowledgeCitationView> Citations { get; set; } = new List<KnowledgeCitationView>();
	}

	public class KnowledgePracticeItemView
	{
		public string Key { get; set; }
		public string Score { get; set; }
		public string Feedback { get; set; }
		public List<string> Refs { get; set; } = new List<string>();
	}

	/// <summary>The JSON a scored practice returns.</summary>
	public class KnowledgePracticeResultView
	{
		public string Outcome { get; set; }
		public string Message { get; set; }
		public List<string> Notices { get; set; } = new List<string>();
		public List<KnowledgePracticeItemView> Items { get; set; } = new List<KnowledgePracticeItemView>();
		public List<KnowledgeCitationView> Citations { get; set; } = new List<KnowledgeCitationView>();
		public string DosingToolUrl { get; set; }
	}

	/// <summary>One citation as the panel renders it.</summary>
	public class KnowledgeCitationView
	{
		public string Ref { get; set; }
		public string Title { get; set; }
		public string Section { get; set; }
		public string Version { get; set; }
		public string Effective { get; set; }
		public string Url { get; set; }
		public string Excerpt { get; set; }
		public string Kind { get; set; }
	}

	public class KnowledgePartView
	{
		public string Text { get; set; }
		public List<string> Refs { get; set; } = new List<string>();
	}

	public class KnowledgeConflictView
	{
		public string Note { get; set; }
		public List<string> Refs { get; set; } = new List<string>();
	}

	/// <summary>The JSON the Ask panel receives: the answer, its sources and the notices to show, all text already localized.</summary>
	public class KnowledgeAskResultView
	{
		public string Outcome { get; set; }
		public string Message { get; set; }
		public List<string> Notices { get; set; } = new List<string>();
		public List<KnowledgePartView> Parts { get; set; } = new List<KnowledgePartView>();
		public List<KnowledgeConflictView> Conflicts { get; set; } = new List<KnowledgeConflictView>();
		public List<KnowledgeCitationView> Citations { get; set; } = new List<KnowledgeCitationView>();
		public string DosingToolUrl { get; set; }
		public string ExchangeId { get; set; }
	}

	public class KnowledgeGapRow
	{
		public KnowledgeGap Gap { get; set; }
		public List<KnowledgeCitationView> Nearest { get; set; } = new List<KnowledgeCitationView>();
		public string AskedBy { get; set; }
		public int? DraftProtocolId { get; set; }
	}

	public class KnowledgeGapsModel
	{
		public string Status { get; set; }
		public int OpenCount { get; set; }
		public int ResolvedCount { get; set; }
		public int DismissedCount { get; set; }
		public int Page { get; set; }
		public bool HasMore { get; set; }
		public bool CanAuthorProtocols { get; set; }
		public bool CanCreateTrainings { get; set; }
		public bool IsAdmin { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
		public List<KnowledgeGapRow> Rows { get; set; } = new List<KnowledgeGapRow>();
	}

	public class KnowledgeSettingsModel
	{
		public bool IncludeDocuments { get; set; }
		public string DosingToolUrl { get; set; }
		public KnowledgeIndexState State { get; set; }
		public bool CanSeeGaps { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
	}
}
