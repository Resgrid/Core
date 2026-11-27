using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Where a case flow stands.</summary>
	public enum ProtocolCaseStatuses
	{
		/// <summary>A question on the path is not answered yet (<see cref="ProtocolCaseResult.CurrentNodeKey"/>).</summary>
		InProgress = 0,

		/// <summary>The path reached an outcome.</summary>
		Complete = 1,

		/// <summary>The graph could not be walked (no single root, a pointer to nothing, a loop) or an answer was not one of the node's.</summary>
		Invalid = 2
	}

	/// <summary>Why a case input was refused. Not persisted.</summary>
	public enum ProtocolCaseErrors
	{
		None = 0,
		/// <summary>The determinant differs from the one the case flow reached (or none was reached) and no reason was given.</summary>
		OverrideReasonRequired = 1,
		/// <summary>The override is not a valid determinant code.</summary>
		DeterminantInvalid = 2,
		/// <summary>The protocol version has no usable case flow.</summary>
		NoCaseFlow = 3,
		/// <summary>An answer was not one of its question's answers.</summary>
		AnswerInvalid = 4
	}

	/// <summary>What a client sends for one protocol's case: the answers by node key, and an optional determinant override with its reason.</summary>
	public sealed class ProtocolCaseInput
	{
		/// <summary>Node key → answer: the answer key (yes/no, single choice), a number (invariant culture), or the free text as said.</summary>
		[JsonProperty("answers")]
		public Dictionary<string, string> Answers { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

		/// <summary>The determinant the dispatcher chose instead of the one the case flow reached; null to keep it.</summary>
		[JsonProperty("overrideDeterminant")]
		public string OverrideDeterminant { get; set; }

		/// <summary>Why: required whenever <see cref="OverrideDeterminant"/> differs from the computed determinant.</summary>
		[JsonProperty("overrideReason")]
		public string OverrideReason { get; set; }

		/// <summary>The priority the call was saved with (the outcome's suggestion when the dispatcher applied it).</summary>
		[JsonProperty("appliedPriority")]
		public int? AppliedPriority { get; set; }

		/// <summary>An Enhanced AI suggestion the dispatcher confirmed (ProtocolAiSuggestions id); the server checks it is real.</summary>
		[JsonProperty("aiSuggestionId")]
		public int? AiSuggestionId { get; set; }

		public static ProtocolCaseInput Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;
			try
			{
				var input = JsonConvert.DeserializeObject<ProtocolCaseInput>(json, new JsonSerializerSettings { MaxDepth = 8 });
				if (input != null)
					input.Answers = new Dictionary<string, string>(input.Answers ?? new Dictionary<string, string>(), StringComparer.Ordinal);
				return input;
			}
			catch (JsonException)
			{
				return null;
			}
		}
	}

	/// <summary>One step the case walked through, in words (the text as it stood in the cited version).</summary>
	public sealed class ProtocolCaseStep
	{
		[JsonProperty("key")] public string NodeKey { get; set; }
		[JsonProperty("kind")] public int Kind { get; set; }
		[JsonProperty("stage")] public int? Stage { get; set; }
		[JsonProperty("text")] public string Text { get; set; }
		/// <summary>The answer key (yes/no, choice, number range) or null for free text and instructions.</summary>
		[JsonProperty("answerKey")] public string AnswerKey { get; set; }
		/// <summary>The answer in words: the label, the number, or the free text.</summary>
		[JsonProperty("answer")] public string Answer { get; set; }
	}

	/// <summary>The case flow walked with a set of answers (<see cref="ProtocolCaseFlow.Run"/>).</summary>
	public sealed class ProtocolCaseResult
	{
		public ProtocolCaseStatuses Status { get; set; }
		/// <summary>The question waiting for an answer (InProgress), else null.</summary>
		public string CurrentNodeKey { get; set; }
		public List<ProtocolCaseStep> Steps { get; set; } = new List<ProtocolCaseStep>();
		/// <summary>Instruction steps passed on the way (read-aloud scripts), in order.</summary>
		public List<string> InstructionKeys { get; set; } = new List<string>();
		public ProtocolDecisionNode Outcome { get; set; }
		public ProtocolCaseErrors Error { get; set; }
		public string ErrorNodeKey { get; set; }
	}

	/// <summary>A text sent to the caller from the case (P4 caller follow-up), logged on the call's case record.</summary>
	public sealed class ProtocolCallerMessage
	{
		[JsonProperty("on")] public DateTime SentOn { get; set; }
		[JsonProperty("by")] public string ByUserId { get; set; }
		[JsonProperty("node")] public string NodeKey { get; set; }
		[JsonProperty("text")] public string Text { get; set; }
		/// <summary><see cref="IntakeConversationActionResults"/> name.</summary>
		[JsonProperty("result")] public string Result { get; set; }
	}

	/// <summary>
	/// The case recorded on a call (CallProtocols.CaseJson, P4): the version it was run against, the path in words, the outcome the flow
	/// reached, the determinant in force and why it differs (override), the priority suggested and applied, where it came from (AI or a
	/// person), and the texts sent to the caller. The server builds it; the client only sends answers and an override.
	/// </summary>
	public sealed class CallProtocolCase
	{
		[JsonProperty("versionId")] public int? ProtocolVersionId { get; set; }
		/// <summary>The protocol's code and name when the case was recorded.</summary>
		[JsonProperty("code")] public string ProtocolCode { get; set; }
		[JsonProperty("protocol")] public string ProtocolName { get; set; }
		[JsonProperty("status")] public int Status { get; set; }
		[JsonProperty("steps")] public List<ProtocolCaseStep> Steps { get; set; } = new List<ProtocolCaseStep>();
		[JsonProperty("outcome")] public string OutcomeNodeKey { get; set; }
		[JsonProperty("outcomeLabel")] public string OutcomeLabel { get; set; }
		/// <summary>What the case flow reached (null when it did not reach an outcome).</summary>
		[JsonProperty("computed")] public string ComputedDeterminant { get; set; }
		/// <summary>The determinant in force: the computed one, or the dispatcher's override.</summary>
		[JsonProperty("determinant")] public string Determinant { get; set; }
		[JsonProperty("overridden")] public bool Overridden { get; set; }
		[JsonProperty("overrideReason")] public string OverrideReason { get; set; }
		[JsonProperty("overriddenBy")] public string OverriddenByUserId { get; set; }
		[JsonProperty("overriddenOn")] public DateTime? OverriddenOn { get; set; }
		[JsonProperty("suggestedPriority")] public int? SuggestedPriority { get; set; }
		[JsonProperty("suggestedPriorityName")] public string SuggestedPriorityName { get; set; }
		[JsonProperty("appliedPriority")] public int? AppliedPriority { get; set; }
		[JsonProperty("response")] public string ResponseHint { get; set; }
		[JsonProperty("postDispatch")] public string PostDispatch { get; set; }
		/// <summary>Instruction nodes on the path (their text is in <see cref="Steps"/>).</summary>
		[JsonProperty("instructions")] public List<string> InstructionKeys { get; set; } = new List<string>();
		/// <summary><see cref="CallProtocolSources"/> of the case answers: Manual (a person) or Ai (a confirmed Enhanced AI prefill).</summary>
		[JsonProperty("source")] public int Source { get; set; }
		[JsonProperty("aiSuggestionId")] public int? AiSuggestionId { get; set; }
		[JsonProperty("completedOn")] public DateTime? CompletedOn { get; set; }
		[JsonProperty("completedBy")] public string CompletedByUserId { get; set; }
		[JsonProperty("caller")] public List<ProtocolCallerMessage> CallerMessages { get; set; } = new List<ProtocolCallerMessage>();

		public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

		public static CallProtocolCase Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;
			try
			{
				var record = JsonConvert.DeserializeObject<CallProtocolCase>(json, new JsonSerializerSettings { MaxDepth = 16 });
				if (record != null)
				{
					record.Steps ??= new List<ProtocolCaseStep>();
					record.InstructionKeys ??= new List<string>();
					record.CallerMessages ??= new List<ProtocolCallerMessage>();
				}
				return record;
			}
			catch (JsonException)
			{
				return null;
			}
		}
	}

	/// <summary>
	/// The case flow state machine (P4): entry questions → key questions → determinant → priority suggested → instructions. Pure; the
	/// server re-walks the cited version's graph with the posted answers on every save, so the client never decides the determinant.
	/// </summary>
	public static class ProtocolCaseFlow
	{
		public const int OverrideReasonMaxLength = 500;
		public const int FreeTextMaxLength = 500;
		public const int MaxCallerMessages = 50;

		/// <summary>
		/// Walks the graph from its root with the answers: a question without an answer stops the walk there (InProgress); an instruction
		/// is passed through; an outcome completes the case. Answers to nodes off the path are ignored (a branch the dispatcher backed
		/// out of). An answer that is not one of the question's makes the case Invalid at that node.
		/// </summary>
		public static ProtocolCaseResult Run(ProtocolDecisionGraph graph, IReadOnlyDictionary<string, string> answers)
		{
			var result = new ProtocolCaseResult { Status = ProtocolCaseStatuses.Invalid };
			var node = graph?.Root();
			if (node == null)
			{
				result.Error = ProtocolCaseErrors.NoCaseFlow;
				return result;
			}

			answers ??= new Dictionary<string, string>();
			var visited = new HashSet<string>(StringComparer.Ordinal);

			while (node != null)
			{
				if (!visited.Add(node.Key ?? string.Empty) || visited.Count > ProtocolDecisionGraphs.MaxNodes)
				{
					result.Error = ProtocolCaseErrors.NoCaseFlow;
					return result;
				}

				if (node.IsOutcome)
				{
					result.Outcome = node;
					result.Status = ProtocolCaseStatuses.Complete;
					result.Steps.Add(new ProtocolCaseStep { NodeKey = node.Key, Kind = node.Kind, Text = node.Label });
					return result;
				}

				if (node.IsInstruction)
				{
					result.InstructionKeys.Add(node.Key);
					result.Steps.Add(new ProtocolCaseStep { NodeKey = node.Key, Kind = node.Kind, Text = node.Text });
					node = graph.Find(node.Next);
					continue;
				}

				if (!node.IsQuestion)
				{
					result.Error = ProtocolCaseErrors.NoCaseFlow;
					return result;
				}

				if (!answers.TryGetValue(node.Key, out var raw) || string.IsNullOrWhiteSpace(raw))
				{
					result.Status = ProtocolCaseStatuses.InProgress;
					result.CurrentNodeKey = node.Key;
					return result;
				}

				var step = new ProtocolCaseStep { NodeKey = node.Key, Kind = node.Kind, Stage = node.Stage, Text = node.Text };
				string next;
				switch ((ProtocolQuestionTypes)(node.QuestionType ?? 0))
				{
					case ProtocolQuestionTypes.FreeText:
						var text = raw.Trim();
						step.Answer = text.Length > FreeTextMaxLength ? text.Substring(0, FreeTextMaxLength) : text;
						next = node.Next;
						break;
					case ProtocolQuestionTypes.Number:
						if (!decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
							return Fail(result, node);
						var range = ProtocolDecisionGraphs.RangeFor(node, number);
						if (range == null)
							return Fail(result, node);
						step.AnswerKey = range.Key;
						step.Answer = number.ToString("0.##", CultureInfo.InvariantCulture);
						next = range.Next;
						break;
					case ProtocolQuestionTypes.YesNo:
					case ProtocolQuestionTypes.SingleChoice:
						var answer = node.Answers?.FirstOrDefault(a => a != null && string.Equals(a.Key, raw.Trim(), StringComparison.OrdinalIgnoreCase));
						if (answer == null)
							return Fail(result, node);
						step.AnswerKey = answer.Key;
						step.Answer = string.IsNullOrWhiteSpace(answer.Label) ? answer.Key : answer.Label;
						next = answer.Next;
						break;
					default:
						return Fail(result, node);
				}

				result.Steps.Add(step);
				node = graph.Find(next);
				if (node == null)
				{
					result.Error = ProtocolCaseErrors.NoCaseFlow;
					return result;
				}
			}

			result.Error = ProtocolCaseErrors.NoCaseFlow;
			return result;
		}

		private static ProtocolCaseResult Fail(ProtocolCaseResult result, ProtocolDecisionNode node)
		{
			result.Status = ProtocolCaseStatuses.Invalid;
			result.Error = ProtocolCaseErrors.AnswerInvalid;
			result.ErrorNodeKey = node.Key;
			return result;
		}

		/// <summary>
		/// The case record for a call from a walk and the dispatcher's input: the computed determinant, and the override when the posted one
		/// differs (which needs a reason). Returns the error instead of a record when the override is refused.
		/// </summary>
		public static (CallProtocolCase Record, ProtocolCaseErrors Error) Record(ProtocolCaseResult result, ProtocolCaseInput input, int? protocolVersionId,
			CallProtocolSources source, string userId, DateTime nowUtc)
		{
			if (result == null || result.Error == ProtocolCaseErrors.NoCaseFlow)
				return (null, ProtocolCaseErrors.NoCaseFlow);
			if (result.Status == ProtocolCaseStatuses.Invalid)
				return (null, result.Error == ProtocolCaseErrors.None ? ProtocolCaseErrors.AnswerInvalid : result.Error);

			var outcome = result.Outcome;
			var record = new CallProtocolCase
			{
				ProtocolVersionId = protocolVersionId,
				Status = (int)result.Status,
				Steps = result.Steps,
				InstructionKeys = result.InstructionKeys,
				OutcomeNodeKey = outcome?.Key,
				OutcomeLabel = outcome?.Label,
				ComputedDeterminant = outcome?.Determinant,
				Determinant = outcome?.Determinant,
				SuggestedPriority = outcome?.PriorityId,
				SuggestedPriorityName = outcome?.PriorityName,
				AppliedPriority = input?.AppliedPriority,
				ResponseHint = outcome?.ResponseHint,
				PostDispatch = outcome?.PostDispatch,
				Source = (int)source,
				AiSuggestionId = source == CallProtocolSources.Ai ? input?.AiSuggestionId : null,
				CompletedOn = result.Status == ProtocolCaseStatuses.Complete ? nowUtc : (DateTime?)null,
				CompletedByUserId = result.Status == ProtocolCaseStatuses.Complete ? userId : null
			};

			var error = ApplyOverride(record, input?.OverrideDeterminant, input?.OverrideReason, userId, nowUtc);
			return error == ProtocolCaseErrors.None ? (record, ProtocolCaseErrors.None) : (null, error);
		}

		/// <summary>
		/// Sets the determinant in force. A value equal to the computed determinant clears an override; anything else is an override and
		/// needs a reason (P4). A blank value leaves the record as it is.
		/// </summary>
		public static ProtocolCaseErrors ApplyOverride(CallProtocolCase record, string determinant, string reason, string userId, DateTime nowUtc)
		{
			if (record == null)
				return ProtocolCaseErrors.NoCaseFlow;

			var chosen = ProtocolDecisionGraphs.NormalizeDeterminant(determinant);
			if (chosen == null)
				return ProtocolCaseErrors.None;

			if (!ProtocolDecisionGraphs.IsValidDeterminant(chosen))
				return ProtocolCaseErrors.DeterminantInvalid;

			if (record.ComputedDeterminant != null && ProtocolDecisionGraphs.SameDeterminant(chosen, record.ComputedDeterminant))
			{
				record.Determinant = record.ComputedDeterminant;
				record.Overridden = false;
				record.OverrideReason = null;
				record.OverriddenByUserId = null;
				record.OverriddenOn = null;
				return ProtocolCaseErrors.None;
			}

			var why = (reason ?? string.Empty).Trim();
			if (why.Length == 0)
				return ProtocolCaseErrors.OverrideReasonRequired;

			record.Determinant = chosen;
			record.Overridden = true;
			record.OverrideReason = why.Length > OverrideReasonMaxLength ? why.Substring(0, OverrideReasonMaxLength) : why;
			record.OverriddenByUserId = userId;
			record.OverriddenOn = nowUtc;
			return ProtocolCaseErrors.None;
		}
	}
}
