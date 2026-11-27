using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Knowledge
{
	// K3 tutor (protocols-training-knowledge-plan §13a, §16.3): scenario practice from a protocol's version in force for Omni. A scenario and
	// its questions are written only from the protocol's sections and cite them; the member's answers are scored against the cited sections
	// only. Practice only: nothing is recorded on the transcript. 404 unless the department has the tutor (Enhanced AI knowledge answers and
	// the LMS add-on).

	public class KnowledgePracticeStatusResult : StandardApiResponseV4Base
	{
		public KnowledgePracticeStatusData Data { get; set; } = new KnowledgePracticeStatusData();
	}

	public class KnowledgePracticeStatusData
	{
		public bool Available { get; set; }

		/// <summary>Why not available: Unconfigured, KeptLocal (Advanced Data Protection) or Invalid.</summary>
		public string Reason { get; set; }
	}

	public class KnowledgePracticeStartInput
	{
		public int ProtocolId { get; set; }
	}

	public class KnowledgePracticeQuestionData
	{
		/// <summary>"Q1", "Q2"…; answers name it.</summary>
		public string Key { get; set; }
		public string Text { get; set; }
		public List<string> Refs { get; set; } = new List<string>();
	}

	public class KnowledgePracticeScenarioData
	{
		/// <summary>Ready, or why not: Excluded, KeptLocal, NoContent, NotFound, Refused, BudgetExhausted, Busy, Unavailable or Unconfigured.</summary>
		public string Outcome { get; set; }

		/// <summary>Send it back with Score.</summary>
		public string ExchangeId { get; set; }

		public int ProtocolId { get; set; }
		public int ProtocolVersionId { get; set; }
		public string VersionLabel { get; set; }
		public string Scenario { get; set; }
		public List<KnowledgePracticeQuestionData> Questions { get; set; } = new List<KnowledgePracticeQuestionData>();
		public List<KnowledgeCitationData> Citations { get; set; } = new List<KnowledgeCitationData>();
		public string PromptVersion { get; set; }
	}

	public class KnowledgePracticeScenarioResult : StandardApiResponseV4Base
	{
		public KnowledgePracticeScenarioData Data { get; set; } = new KnowledgePracticeScenarioData();
	}

	public class KnowledgePracticeAnswerInput
	{
		public string Key { get; set; }

		/// <summary>The member's answer in their own words, at most 1,000 characters. Do not include patient or personal details.</summary>
		public string Text { get; set; }
	}

	public class KnowledgePracticeScoreInput
	{
		public string ExchangeId { get; set; }
		public List<KnowledgePracticeAnswerInput> Answers { get; set; } = new List<KnowledgePracticeAnswerInput>();
	}

	public class KnowledgePracticeItemData
	{
		public string Key { get; set; }

		/// <summary>0, 1 or 2; null when the item could not be scored.</summary>
		public int? Score { get; set; }

		public int MaxScore { get; set; }
		public string Feedback { get; set; }
		public List<string> Refs { get; set; } = new List<string>();
	}

	public class KnowledgePracticeResultData
	{
		/// <summary>Scored, or why not: Stale, NotFound, Refused, KeptLocal, BudgetExhausted, Busy, Unavailable, Unconfigured or Invalid.</summary>
		public string Outcome { get; set; }
		public string ExchangeId { get; set; }
		public int Total { get; set; }
		public int Max { get; set; }
		public List<KnowledgePracticeItemData> Items { get; set; } = new List<KnowledgePracticeItemData>();
		public List<KnowledgeCitationData> Citations { get; set; } = new List<KnowledgeCitationData>();
		/// <summary>Feedback quotes a dose (verbatim from the protocol); show DosingToolUrl when set. Nothing is calculated.</summary>
		public bool QuotesDose { get; set; }
		public string DosingToolUrl { get; set; }
		public string PromptVersion { get; set; }
	}

	public class KnowledgePracticeScoreResult : StandardApiResponseV4Base
	{
		public KnowledgePracticeResultData Data { get; set; } = new KnowledgePracticeResultData();
	}
}
