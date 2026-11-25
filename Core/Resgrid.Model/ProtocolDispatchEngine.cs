using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// The licensing guardrail (Protocols P4, plan §5 P4 and decision 6): Resgrid ships the decision engine and open templates, never
	/// MPDS/IntelliComm or other licensed protocol content. A department may hold licensed content in its own space only; the
	/// <see cref="DispatchProtocol.IsLicensedContent"/> flag is its attestation, and while it is set the content never leaves the department:
	/// not shared to linked departments or a regional set (P5), not on a public page, not into the AI corpus (K1) or an AI prompt.
	/// Every sharing, publishing and AI path asks here.
	/// </summary>
	public static class ProtocolLicensing
	{
		/// <summary>May the protocol be shared outside its department (linked departments, protocol sets, the template library)?</summary>
		public static bool CanShare(DispatchProtocol protocol) => protocol != null && !protocol.IsDeleted && !protocol.IsLicensedContent;

		/// <summary>May the protocol appear on a public page (opt-in, P5)? Never licensed content, never a draft.</summary>
		public static bool CanPublishPublicly(DispatchProtocol protocol) => CanShare(protocol) && protocol.IsPublished;

		/// <summary>May the protocol's content reach the AI corpus or a prompt (K1 index, P4 AI suggestion)?</summary>
		public static bool CanUseForAi(DispatchProtocol protocol) => protocol != null && !protocol.IsDeleted && !protocol.ExcludeFromAi && !protocol.IsLicensedContent;
	}

	/// <summary>
	/// An Enhanced AI protocol suggestion for a call (Protocols P4, M0400): the protocol the model picked from the intake transcript or SMS
	/// thread and the case answers it prefilled. The dispatcher confirms it on the case flow; only then is it attached, and the call
	/// records Source = AI with this row's id. Operational data (protocol content and call answers are not ADP, plan §4).
	/// </summary>
	[Table("ProtocolAiSuggestions")]
	public class ProtocolAiSuggestion : IEntity
	{
		public const int RationaleMaxLength = 1000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ProtocolAiSuggestionId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		public int DispatchProtocolId { get; set; }

		/// <summary>The version whose graph the answers were prefilled against.</summary>
		public int? ProtocolVersionId { get; set; }

		/// <summary>Node key → answer, validated against the version's graph (<see cref="ProtocolCaseInput.Answers"/> shape).</summary>
		public string AnswersJson { get; set; }

		public decimal Confidence { get; set; }

		public string Rationale { get; set; }

		/// <summary><see cref="ProtocolAiSuggestionInputs"/>: what the model read.</summary>
		public int InputKind { get; set; }

		public string ModelRevision { get; set; }

		public DateTime CreatedOn { get; set; }

		public string RequestedByUserId { get; set; }

		/// <summary>Set when a dispatcher attached the protocol with this suggestion (it is used once).</summary>
		public DateTime? ConfirmedOn { get; set; }

		public string ConfirmedByUserId { get; set; }

		[NotMapped]
		public Dictionary<string, string> Answers
		{
			get
			{
				if (string.IsNullOrWhiteSpace(AnswersJson))
					return new Dictionary<string, string>(StringComparer.Ordinal);
				try
				{
					return new Dictionary<string, string>(JsonConvert.DeserializeObject<Dictionary<string, string>>(AnswersJson) ?? new Dictionary<string, string>(), StringComparer.Ordinal);
				}
				catch (JsonException)
				{
					return new Dictionary<string, string>(StringComparer.Ordinal);
				}
			}
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProtocolAiSuggestionId; }
			set { ProtocolAiSuggestionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ProtocolAiSuggestions";

		[NotMapped]
		public string IdName => "ProtocolAiSuggestionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Answers" };
	}

	/// <summary>What an AI protocol suggestion was made from. Stored; never renumber.</summary>
	public enum ProtocolAiSuggestionInputs
	{
		/// <summary>The call's nature and notes (a voice intake transcript lands there).</summary>
		CallText = 0,

		/// <summary>The call's intake SMS/chat thread.</summary>
		IntakeThread = 1
	}

	/// <summary>Why no AI suggestion was made (fails closed). Not persisted.</summary>
	public enum ProtocolAiSuggestionOutcomes
	{
		Suggested = 0,
		/// <summary>No Enhanced AI add-on, the Ai.Intake capability off, or the AI module switched off.</summary>
		NotEntitled = 1,
		/// <summary>Advanced Data Protection: call content may not reach inference.</summary>
		ProtectionUnsupported = 2,
		/// <summary>The operator has no generative model configured.</summary>
		Unconfigured = 3,
		/// <summary>No published Dispatch protocol with a case flow is allowed in AI (licensed or "no AI" ones never are).</summary>
		NoCandidates = 4,
		/// <summary>Nothing to read: the call has no text and no intake thread.</summary>
		NoInput = 5,
		/// <summary>The model gave no usable answer (none fits, malformed, below the confidence floor).</summary>
		NoMatch = 6,
		/// <summary>Inference timed out or is down.</summary>
		Unavailable = 7,
		NotFound = 8
	}

	/// <summary>The result of asking for an AI suggestion: the stored suggestion, or why there is none.</summary>
	public sealed class ProtocolAiSuggestionResult
	{
		public ProtocolAiSuggestionOutcomes Outcome { get; set; }
		public ProtocolAiSuggestion Suggestion { get; set; }
		public string ProtocolName { get; set; }
		public string ProtocolCode { get; set; }

		public static ProtocolAiSuggestionResult Of(ProtocolAiSuggestionOutcomes outcome) => new ProtocolAiSuggestionResult { Outcome = outcome };
	}

	/// <summary>One candidate protocol shown to the model: code, name and its case flow's questions with answer keys (no content text).</summary>
	public sealed class ProtocolAiCandidate
	{
		public int ProtocolId { get; set; }
		public int? ProtocolVersionId { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public ProtocolDecisionGraph Graph { get; set; }
	}

	/// <summary>What the model answered, before validation.</summary>
	public sealed class ProtocolAiPick
	{
		public int ProtocolId { get; set; }
		public Dictionary<string, string> Answers { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
		public decimal Confidence { get; set; }
		public string Rationale { get; set; }
		public string ModelRevision { get; set; }
	}

	/// <summary>
	/// The determinant and protocol name in dispatch texts (P4 surfaces): the push title, the dispatch text message and the voice prompt.
	/// Used only when the department turned <see cref="ProtocolLibrarySettings.ShowDeterminantInDispatch"/> on; off, every text is
	/// exactly as before.
	/// </summary>
	public static class ProtocolDispatchText
	{
		/// <summary>The first protocol on the call with a determinant (attach order), or null.</summary>
		public static CallProtocol Lead(IEnumerable<CallProtocol> protocols) =>
			(protocols ?? Enumerable.Empty<CallProtocol>())
				.Where(p => p != null && !p.IsRemoved && !string.IsNullOrWhiteSpace(p.Determinant))
				.OrderBy(p => p.AttachedOn ?? DateTime.MaxValue).ThenBy(p => p.CallProtocolId)
				.FirstOrDefault();

		private static string Name(CallProtocol protocol)
		{
			var name = protocol?.Protocol?.Name;
			if (string.IsNullOrWhiteSpace(name))
			{
				var answers = CallProtocolCase.Parse(protocol?.CaseJson);
				name = answers?.ProtocolName;
			}

			return string.IsNullOrWhiteSpace(name) ? protocol?.Data : name.Trim();
		}

		/// <summary>"{determinant} {protocol name}", or null when the setting is off or no protocol has a determinant.</summary>
		public static string Summary(IEnumerable<CallProtocol> protocols, bool enabled)
		{
			if (!enabled)
				return null;

			var lead = Lead(protocols);
			if (lead == null)
				return null;

			var name = Name(lead);
			return string.IsNullOrWhiteSpace(name) ? lead.Determinant.Trim() : $"{lead.Determinant.Trim()} {name}";
		}

		/// <summary>The push title with the summary after it ("Call 1234 · 10-D-1 Chest pain"); the title unchanged when off.</summary>
		public static string PushTitle(string title, IEnumerable<CallProtocol> protocols, bool enabled)
		{
			var summary = Summary(protocols, enabled);
			return summary == null ? title : $"{title} · {summary}";
		}

		/// <summary>
		/// The protocol part of the dispatch text message: the codes as before ("CODE1,CODE2"), with the determinant and name of the lead
		/// protocol when the setting is on. Empty when the call has no protocols.
		/// </summary>
		public static string SmsProtocols(IEnumerable<CallProtocol> protocols, bool enabled)
		{
			var codes = string.Join(",", (protocols ?? Enumerable.Empty<CallProtocol>()).Where(p => p != null && !p.IsRemoved && !string.IsNullOrWhiteSpace(p.Data)).Select(p => p.Data));
			var summary = Summary(protocols, enabled);
			if (summary == null)
				return codes;

			return string.IsNullOrWhiteSpace(codes) ? summary : $"{codes}; {summary}";
		}

		/// <summary>The spoken sentence for the voice prompt ("Determinant, 10-D-1. Protocol, Chest pain."), or null when off.</summary>
		public static string Spoken(IEnumerable<CallProtocol> protocols, bool enabled)
		{
			if (!enabled)
				return null;

			var lead = Lead(protocols);
			if (lead == null)
				return null;

			var name = Name(lead);
			return string.IsNullOrWhiteSpace(name)
				? $"Determinant, {lead.Determinant.Trim()}."
				: $"Determinant, {lead.Determinant.Trim()}. Protocol, {name}.";
		}
	}
}
