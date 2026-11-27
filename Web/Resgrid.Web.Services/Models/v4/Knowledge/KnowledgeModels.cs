using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Knowledge
{
	// K1 (protocols-training-knowledge-plan §16): department knowledge answers with citations for Omni. Answers come only from the
	// department's published protocols and training and the Resgrid Academy; every part cites its sources (item, version, anchor, effective
	// date). All times are UTC. The endpoints return 404 unless the department has Ai.Knowledge.

	public class KnowledgeStatusResult : StandardApiResponseV4Base
	{
		public KnowledgeStatusData Data { get; set; } = new KnowledgeStatusData();
	}

	public class KnowledgeStatusData
	{
		public bool Available { get; set; }

		/// <summary>Why not available: NotEntitled, Unconfigured or Invalid.</summary>
		public string Reason { get; set; }

		/// <summary>Advanced Data Protection keeps questions local: answers are the matching sources only.</summary>
		public bool QuestionsKeptLocal { get; set; }

		public bool CanSeeGaps { get; set; }
	}

	/// <summary>An item version the client holds offline; a cited item with a newer version is flagged stale.</summary>
	public class KnowledgeOfflineVersionInput
	{
		/// <summary>0 Protocol, 1 ProtocolFile, 2 Training, 3 TrainingFile, 4 Course, 5 Academy, 6 Document.</summary>
		public int SourceKind { get; set; }

		public string ItemId { get; set; }

		public string VersionId { get; set; }
	}

	public class KnowledgeAskInput
	{
		/// <summary>The question, at most 1,000 characters. Do not include patient or personal details.</summary>
		public string Question { get; set; }

		/// <summary>Ask about one item only (0 Protocol, 2 Training) with <see cref="ScopeItemId"/>, or 5 alone for "How do I…?" over the Resgrid Academy.</summary>
		public int? ScopeKind { get; set; }

		public string ScopeItemId { get; set; }

		/// <summary>K3: with ScopeKind 4 (Course), the course version the learner takes; blank picks the member's own enrollment.</summary>
		public string ScopeVersionId { get; set; }

		/// <summary>
		/// "Ask about this call's protocol": the call and its CallProtocolId (grounded in the version the call cites). K3: the call alone asks about
		/// the whole call — every protocol version it cites and its type, priority, determinant and location type (never patient or caller details).
		/// </summary>
		public int? CallId { get; set; }

		public int? CallProtocolId { get; set; }

		public List<KnowledgeOfflineVersionInput> OfflineVersions { get; set; }
	}

	public class KnowledgeCitationData
	{
		/// <summary>The id the answer parts cite this source by ("S1").</summary>
		public string Ref { get; set; }
		public int SourceKind { get; set; }
		public string ItemId { get; set; }
		public string VersionId { get; set; }
		public string VersionLabel { get; set; }
		/// <summary>The heading anchor of the cited section (or page-N for file text).</summary>
		public string Anchor { get; set; }
		public string HeadingPath { get; set; }
		public string Title { get; set; }
		public DateTime? EffectiveOnUtc { get; set; }
		public string Excerpt { get; set; }
		/// <summary>The client's offline copy of this item is older than the version cited.</summary>
		public bool OfflineCopyStale { get; set; }
		/// <summary>The web app path (or Academy URL) of the exact section.</summary>
		public string WebPath { get; set; }
	}

	public class KnowledgePartData
	{
		public string Text { get; set; }
		public List<string> Refs { get; set; } = new List<string>();
	}

	public class KnowledgeConflictData
	{
		public string Note { get; set; }
		public List<string> Refs { get; set; } = new List<string>();
	}

	public class KnowledgeAnswerData
	{
		/// <summary>Answered, NotCovered, Matched, BudgetExhausted, Busy, Unavailable, Unconfigured or Invalid.</summary>
		public string Outcome { get; set; }
		public List<KnowledgePartData> Parts { get; set; } = new List<KnowledgePartData>();
		public List<KnowledgeCitationData> Citations { get; set; } = new List<KnowledgeCitationData>();
		/// <summary>The closest items when the department's content does not cover the question.</summary>
		public List<KnowledgeCitationData> Nearest { get; set; } = new List<KnowledgeCitationData>();
		public List<KnowledgeConflictData> Conflicts { get; set; } = new List<KnowledgeConflictData>();
		/// <summary>Hybrid, or KeywordOnly (embeddings unavailable or the question kept local).</summary>
		public string RetrievalMode { get; set; }
		public bool EmbeddingsUnavailable { get; set; }
		public bool QuestionKeptLocal { get; set; }
		/// <summary>The answer quotes a dose (verbatim from its source); show DosingToolUrl when set. Nothing is calculated.</summary>
		public bool QuotesDose { get; set; }
		public string DosingToolUrl { get; set; }
		public bool OfflineCopyStale { get; set; }
		/// <summary>Send it back with Feedback.</summary>
		public string ExchangeId { get; set; }
		public string PromptVersion { get; set; }
		/// <summary>K3: the answer was grounded in the whole call (its cited versions and operational fields).</summary>
		public bool CallContextUsed { get; set; }
	}

	public class KnowledgeAskResult : StandardApiResponseV4Base
	{
		public KnowledgeAnswerData Data { get; set; } = new KnowledgeAnswerData();
	}

	public class KnowledgeFeedbackInput
	{
		public string ExchangeId { get; set; }

		/// <summary>False (thumbs-down) adds the question to the department's knowledge gaps report.</summary>
		public bool Helpful { get; set; }
	}

	public class KnowledgeFeedbackResult : StandardApiResponseV4Base
	{
		public KnowledgeFeedbackData Data { get; set; } = new KnowledgeFeedbackData();
	}

	public class KnowledgeFeedbackData
	{
		public bool Recorded { get; set; }
	}
}
