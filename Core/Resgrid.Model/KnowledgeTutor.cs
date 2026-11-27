using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Resgrid.Model
{
	/// <summary>
	/// The operational fields of a call that a call-context answer may use (K3, protocols-training-knowledge-plan §16.5 "Ask about this
	/// call"): the call type, the priority, the dispatch determinant (P4, when there is one) and the type of the location the call was picked
	/// from (a point of interest's type such as "School"). This is an allow-list: nothing else about a call is ever read into it — never the
	/// caller, the address, the nature, notes, contacts, form data or anything about a patient. A guard test pins the property list.
	/// </summary>
	public sealed class KnowledgeCallContext
	{
		public const int FieldMaxLength = 100;

		/// <summary>The property names this class may ever carry (the guard test compares the class with it).</summary>
		public static readonly IReadOnlyList<string> AllowedFields = new[] { nameof(CallType), nameof(Priority), nameof(Determinant), nameof(LocationType) };

		/// <summary>The call type's name.</summary>
		public string CallType { get; set; }

		/// <summary>The priority's name.</summary>
		public string Priority { get; set; }

		/// <summary>The dispatch determinant (P4), e.g. "09-E-01"; null when the call has none.</summary>
		public string Determinant { get; set; }

		/// <summary>The type of the point of interest the call's location was picked from (never its name or address).</summary>
		public string LocationType { get; set; }

		public bool IsEmpty => string.IsNullOrWhiteSpace(CallType) && string.IsNullOrWhiteSpace(Priority) && string.IsNullOrWhiteSpace(Determinant) &&
			string.IsNullOrWhiteSpace(LocationType);

		/// <summary>A field as stored: one line, trimmed, capped; null when blank.</summary>
		public static string Clean(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var line = new string(value.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
			if (line.Length == 0)
				return null;
			return line.Length <= FieldMaxLength ? line : line.Substring(0, FieldMaxLength);
		}

		/// <summary>The CALL block the prompt shows (English field names, the department's own values).</summary>
		public string ToPromptText()
		{
			var text = new StringBuilder();
			void Line(string label, string value)
			{
				var clean = Clean(value);
				if (clean != null)
					text.Append(label).Append(": ").Append(clean).Append('\n');
			}

			Line("Call type", CallType);
			Line("Priority", Priority);
			Line("Determinant", Determinant);
			Line("Location type", LocationType);
			return text.ToString().TrimEnd('\n');
		}

		/// <summary>Words added to the retrieval query so a short question ("what first?") still finds the call's sections.</summary>
		public string RetrievalTerms() =>
			string.Join(" ", new[] { CallType, Determinant, LocationType }.Select(Clean).Where(v => v != null));
	}

	/// <summary>
	/// One version of an item built for one reader at request time (K3): the course version a learner takes ("Ask this course"). The chunks
	/// are scored like a call's cited versions, never stored. Null chunks with an outcome when the reader may not use it.
	/// </summary>
	public sealed class KnowledgeVersionContent
	{
		/// <summary>Null when usable; otherwise <see cref="KnowledgeOutcomes.NotEntitled"/> (a gate is closed) or <see cref="KnowledgeOutcomes.Invalid"/>.</summary>
		public string Outcome { get; set; }

		public KnowledgeSourceKinds Kind { get; set; }

		public string ItemId { get; set; }

		public string VersionId { get; set; }

		public string Title { get; set; }

		public List<KnowledgeChunk> Chunks { get; set; } = new List<KnowledgeChunk>();

		public static KnowledgeVersionContent Refused(string outcome) => new KnowledgeVersionContent { Outcome = outcome ?? KnowledgeOutcomes.Invalid, Chunks = null };
	}

	/// <summary>The outcomes of scenario practice beyond the shared <see cref="KnowledgeOutcomes"/> (K3 tutor).</summary>
	public static class KnowledgePracticeOutcomes
	{
		/// <summary>A scenario and its questions, every question citing the protocol's sections.</summary>
		public const string Ready = "Ready";

		/// <summary>The answers were scored against the cited content.</summary>
		public const string Scored = "Scored";

		/// <summary>The protocol is marked "no AI" or is licensed content.</summary>
		public const string Excluded = "Excluded";

		/// <summary>Advanced Data Protection keeps department content local: no practice.</summary>
		public const string KeptLocal = "KeptLocal";

		public const string NotFound = "NotFound";

		/// <summary>The version holds too little text to practise on.</summary>
		public const string NoContent = "NoContent";

		/// <summary>The model's output failed validation (an uncited question, a number the protocol does not hold): nothing is shown.</summary>
		public const string Refused = "Refused";

		/// <summary>The protocol changed since the scenario was written: start a new one.</summary>
		public const string Stale = "Stale";
	}

	/// <summary>Whether scenario practice is offered to this member right now.</summary>
	public sealed class KnowledgePracticeStatus
	{
		public bool Available { get; set; }

		/// <summary>Why not: NotEntitled, Unconfigured, KeptLocal or Invalid.</summary>
		public string Reason { get; set; }
	}

	/// <summary>One practice question and the sources it is asked from.</summary>
	public sealed class KnowledgePracticeQuestion
	{
		/// <summary>"Q1", "Q2"…; the answers name it.</summary>
		public string Key { get; set; }

		public string Text { get; set; }

		public List<string> Refs { get; set; } = new List<string>();
	}

	/// <summary>A practice scenario (K3 tutor): a short situation and questions written only from one protocol version. Practice only.</summary>
	public sealed class KnowledgePracticeScenario
	{
		/// <summary><see cref="KnowledgePracticeOutcomes"/> or <see cref="KnowledgeOutcomes"/>.</summary>
		public string Outcome { get; set; }

		/// <summary>AiGenerations.Id of the scenario; the answers are scored against it.</summary>
		public string ExchangeId { get; set; }

		public int ProtocolId { get; set; }

		public int ProtocolVersionId { get; set; }

		public string Title { get; set; }

		public string VersionLabel { get; set; }

		public string Scenario { get; set; }

		public List<KnowledgePracticeQuestion> Questions { get; set; } = new List<KnowledgePracticeQuestion>();

		public List<KnowledgeCitation> Citations { get; set; } = new List<KnowledgeCitation>();

		public string PromptVersion { get; set; }
	}

	/// <summary>A learner's free-text answer to one practice question.</summary>
	public sealed class KnowledgePracticeAnswer
	{
		public const int TextMaxLength = 1000;

		public string Key { get; set; }

		public string Text { get; set; }
	}

	/// <summary>The score of one answer: 0 (wrong or missing), 1 (partly), 2 (matches the protocol), with feedback citing the protocol.</summary>
	public sealed class KnowledgePracticeItemScore
	{
		public const int MaxScore = 2;

		public string Key { get; set; }

		/// <summary>Null when the item could not be scored (its feedback failed validation).</summary>
		public int? Score { get; set; }

		public string Feedback { get; set; }

		public List<string> Refs { get; set; } = new List<string>();
	}

	/// <summary>The scored practice (never a learning record, a certification or a quiz attempt: practice only).</summary>
	public sealed class KnowledgePracticeResult
	{
		public string Outcome { get; set; }

		public string ExchangeId { get; set; }

		public List<KnowledgePracticeItemScore> Items { get; set; } = new List<KnowledgePracticeItemScore>();

		public int Total => Items.Where(i => i.Score.HasValue).Sum(i => i.Score.Value);

		public int Max => Items.Count * KnowledgePracticeItemScore.MaxScore;

		public List<KnowledgeCitation> Citations { get; set; } = new List<KnowledgeCitation>();

		/// <summary>Feedback quotes a dose (verbatim from the protocol): the department's dosing tool, when one is set.</summary>
		public bool QuotesDose { get; set; }

		public string DosingToolUrl { get; set; }

		public string PromptVersion { get; set; }
	}
}
