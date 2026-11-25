using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// <see cref="IProtocolAiSuggestionService"/> (Protocols P4 on the intake desk seam): the model reads the call's text or its intake
	/// thread and picks one of the department's published Dispatch protocols with a case flow, prefilling its answers. Nothing is attached
	/// here: the suggestion is stored and shown to the dispatcher, who confirms it on the case flow. Fails closed (no add-on, Advanced Data
	/// Protection, no model, no candidates); licensed and "no AI" protocols never reach the prompt (<see cref="ProtocolLicensing"/>).
	/// </summary>
	public class ProtocolAiSuggestionService : IProtocolAiSuggestionService
	{
		/// <summary>Below this the model's pick is not offered.</summary>
		public const decimal MinimumConfidence = 0.5m;

		/// <summary>Longest text read from the call (nature, notes, thread), newest thread messages kept.</summary>
		public const int MaxInputLength = 6000;

		private readonly IEnhancedAiAccessService _enhancedAi;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IProtocolsService _protocols;
		private readonly IProtocolCaseService _cases;
		private readonly Lazy<ICallsService> _calls;
		private readonly Lazy<IIntakeConversationService> _conversations;
		private readonly IProtocolAiSuggestionsRepository _suggestions;
		private readonly IProtocolCaseSuggester _suggester;
		private readonly IEventAggregator _events;

		public ProtocolAiSuggestionService(IEnhancedAiAccessService enhancedAi, IDepartmentDataProtectionService protection, IProtocolsService protocols,
			IProtocolCaseService cases, Lazy<ICallsService> calls, Lazy<IIntakeConversationService> conversations, IProtocolAiSuggestionsRepository suggestions,
			IProtocolCaseSuggester suggester, IEventAggregator events)
		{
			_enhancedAi = enhancedAi;
			_protection = protection;
			_protocols = protocols;
			_cases = cases;
			_calls = calls;
			_conversations = conversations;
			_suggestions = suggestions;
			_suggester = suggester;
			_events = events;
		}

		public async Task<bool> IsAvailableAsync(int departmentId) => await GateAsync(departmentId) == null;

		/// <summary>Null when suggestions may run; otherwise why not. Any failure reading the gate is "not entitled" (fails closed).</summary>
		private async Task<ProtocolAiSuggestionOutcomes?> GateAsync(int departmentId)
		{
			if (departmentId <= 0)
				return ProtocolAiSuggestionOutcomes.NotEntitled;
			if (_suggester == null || !_suggester.IsConfigured)
				return ProtocolAiSuggestionOutcomes.Unconfigured;

			try
			{
				if (!await _enhancedAi.CanUseAsync(departmentId, FeatureFlagKeys.AiIntake))
					return ProtocolAiSuggestionOutcomes.NotEntitled;
				// Call content may reach inference only through a future ai-inference broker purpose (enhanced AI plan D9).
				if (await _protection.ShouldEncryptNewWritesAsync(departmentId))
					return ProtocolAiSuggestionOutcomes.ProtectionUnsupported;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"AI protocol suggestion availability could not be read for department {departmentId}.");
				return ProtocolAiSuggestionOutcomes.NotEntitled;
			}

			return null;
		}

		public async Task<ProtocolAiSuggestionResult> SuggestAsync(int departmentId, int callId, string userId, CancellationToken cancellationToken = default)
		{
			var gate = await GateAsync(departmentId);
			if (gate.HasValue)
				return ProtocolAiSuggestionResult.Of(gate.Value);

			var call = callId > 0 ? await _calls.Value.GetCallByIdAsync(callId) : null;
			if (call == null || call.DepartmentId != departmentId || call.IsDeleted)
				return ProtocolAiSuggestionResult.Of(ProtocolAiSuggestionOutcomes.NotFound);

			var candidates = await CandidatesAsync(departmentId);
			if (candidates.Count == 0)
				return ProtocolAiSuggestionResult.Of(ProtocolAiSuggestionOutcomes.NoCandidates);

			var (text, kind) = await InputAsync(departmentId, call, userId);
			if (string.IsNullOrWhiteSpace(text))
				return ProtocolAiSuggestionResult.Of(ProtocolAiSuggestionOutcomes.NoInput);

			ProtocolAiPick pick;
			try
			{
				pick = await _suggester.SuggestAsync(departmentId, text, candidates, cancellationToken);
			}
			catch (Exception ex) when (ex is TimeoutException || ex is LlmUnavailableException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
			{
				return ProtocolAiSuggestionResult.Of(ProtocolAiSuggestionOutcomes.Unavailable);
			}

			var validated = Validate(pick, candidates);
			if (validated == null)
				return ProtocolAiSuggestionResult.Of(ProtocolAiSuggestionOutcomes.NoMatch);

			var candidate = candidates.First(c => c.ProtocolId == validated.ProtocolId);
			var suggestion = new ProtocolAiSuggestion
			{
				DepartmentId = departmentId,
				CallId = call.CallId,
				DispatchProtocolId = candidate.ProtocolId,
				ProtocolVersionId = candidate.ProtocolVersionId,
				AnswersJson = JsonConvert.SerializeObject(validated.Answers),
				Confidence = Math.Round(Math.Clamp(validated.Confidence, 0m, 1m), 4),
				Rationale = Truncate(validated.Rationale, ProtocolAiSuggestion.RationaleMaxLength),
				InputKind = (int)kind,
				ModelRevision = Truncate(validated.ModelRevision, 100),
				CreatedOn = DateTime.UtcNow,
				RequestedByUserId = userId
			};

			suggestion = await _suggestions.SaveOrUpdateAsync(suggestion, cancellationToken, true);
			return new ProtocolAiSuggestionResult { Outcome = ProtocolAiSuggestionOutcomes.Suggested, Suggestion = suggestion, ProtocolCode = candidate.Code, ProtocolName = candidate.Name };
		}

		/// <summary>
		/// The protocols the model may see: published, enabled Dispatch protocols with a valid case flow that are allowed in AI (never licensed
		/// content and never "do not use for AI"). Only codes, names and the case flow's questions go to the model, not the protocol text.
		/// </summary>
		private async Task<List<ProtocolAiCandidate>> CandidatesAsync(int departmentId)
		{
			var allowed = (await _protocols.GetProtocolSummariesForDepartmentAsync(departmentId, true) ?? new List<DispatchProtocol>())
				.Where(ProtocolLicensing.CanUseForAi)
				.Select(p => p.DispatchProtocolId)
				.ToHashSet();

			return (await _cases.GetDefinitionsAsync(departmentId))
				.Where(d => allowed.Contains(d.ProtocolId))
				.Select(d => new ProtocolAiCandidate { ProtocolId = d.ProtocolId, ProtocolVersionId = d.ProtocolVersionId, Code = d.Code, Name = d.Name, Graph = d.Graph })
				.ToList();
		}

		private async Task<(string Text, ProtocolAiSuggestionInputs Kind)> InputAsync(int departmentId, Call call, string userId)
		{
			var text = new StringBuilder();
			Append(text, call.NatureOfCall);
			Append(text, call.Notes);
			var kind = ProtocolAiSuggestionInputs.CallText;

			try
			{
				var conversation = await _conversations.Value.GetForCallAsync(departmentId, call.CallId);
				if (conversation != null)
				{
					var view = await _conversations.Value.GetViewAsync(departmentId, conversation.IntakeConversationId, userId);
					foreach (var message in view?.Messages ?? new List<IntakeConversationEntry>())
						Append(text, message?.Text);
					kind = ProtocolAiSuggestionInputs.IntakeThread;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The intake thread of call {call.CallId} could not be read for an AI protocol suggestion.");
			}

			var all = text.ToString().Trim();
			return (all.Length > MaxInputLength ? all.Substring(all.Length - MaxInputLength) : all, kind);
		}

		private static void Append(StringBuilder text, string value)
		{
			var plain = DispatchTriggerEngine.Flatten(value);
			if (plain.Length > 0)
				text.AppendLine(plain);
		}

		/// <summary>
		/// The model's pick checked against the candidates: a protocol that was offered, a confidence at or above the floor, and only answers
		/// that are valid for that protocol's questions (keys of its answers, numbers inside a range, free text trimmed). Null when nothing
		/// usable is left. The dispatcher still walks the case and confirms.
		/// </summary>
		public static ProtocolAiPick Validate(ProtocolAiPick pick, IReadOnlyList<ProtocolAiCandidate> candidates)
		{
			if (pick == null || candidates == null || pick.Confidence < MinimumConfidence)
				return null;

			var candidate = candidates.FirstOrDefault(c => c.ProtocolId == pick.ProtocolId);
			if (candidate?.Graph == null)
				return null;

			var answers = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var pair in pick.Answers ?? new Dictionary<string, string>())
			{
				var node = candidate.Graph.Find(pair.Key);
				if (node == null || !node.IsQuestion || string.IsNullOrWhiteSpace(pair.Value))
					continue;

				var walked = ProtocolCaseFlow.Run(new ProtocolDecisionGraph { Nodes = new List<ProtocolDecisionNode> { Rooted(node), } }, new Dictionary<string, string> { [node.Key] = pair.Value });
				if (walked.Error == ProtocolCaseErrors.AnswerInvalid)
					continue;

				answers[node.Key] = node.QuestionType == (int)ProtocolQuestionTypes.FreeText
					? Truncate(pair.Value.Trim(), ProtocolCaseFlow.FreeTextMaxLength)
					: pair.Value.Trim();
			}

			return new ProtocolAiPick
			{
				ProtocolId = candidate.ProtocolId,
				Answers = answers,
				Confidence = pick.Confidence,
				Rationale = pick.Rationale,
				ModelRevision = pick.ModelRevision
			};
		}

		/// <summary>The question alone as a one-step graph, so its answer can be checked with the case flow's own rules.</summary>
		private static ProtocolDecisionNode Rooted(ProtocolDecisionNode node) => new ProtocolDecisionNode
		{
			Key = node.Key,
			IsRoot = true,
			Kind = node.Kind,
			QuestionType = node.QuestionType,
			Answers = node.Answers,
			Next = node.Next
		};

		public async Task<ProtocolAiSuggestion> GetConfirmableAsync(int departmentId, int callId, int protocolId, int protocolAiSuggestionId)
		{
			if (departmentId <= 0 || callId <= 0 || protocolAiSuggestionId <= 0)
				return null;

			var suggestion = await _suggestions.GetByIdForDepartmentAsync(departmentId, protocolAiSuggestionId);
			return suggestion != null && suggestion.DepartmentId == departmentId && suggestion.CallId == callId && suggestion.DispatchProtocolId == protocolId
				   && !suggestion.ConfirmedOn.HasValue
				? suggestion
				: null;
		}

		public async Task<bool> ConfirmAsync(int departmentId, int protocolAiSuggestionId, string userId, CancellationToken cancellationToken = default)
		{
			var confirmed = await _suggestions.TryConfirmAsync(departmentId, protocolAiSuggestionId, userId, DateTime.UtcNow, cancellationToken);
			if (confirmed)
			{
				_events?.SendMessage<AuditEvent>(new AuditEvent
				{
					DepartmentId = departmentId,
					UserId = userId,
					Type = AuditLogTypes.ProtocolAiSuggestionConfirmed,
					After = JsonConvert.SerializeObject(new { ProtocolAiSuggestionId = protocolAiSuggestionId }),
					Successful = true,
					ServerName = Environment.MachineName
				});
			}

			return confirmed;
		}

		private static string Truncate(string value, int max) =>
			string.IsNullOrEmpty(value) ? value : value.Length > max ? value.Substring(0, max) : value;
	}

	/// <summary>
	/// <see cref="IProtocolCaseSuggester"/> over the operator's generative model (the same endpoint and pinning rules as AI destination
	/// advice): one tool call, <c>suggest_protocol</c>, with the protocol id, the answers by step key, a confidence and a one-line reason.
	/// </summary>
	public class LlmProtocolCaseSuggester : IProtocolCaseSuggester
	{
		public const string ToolName = "suggest_protocol";
		public const int TimeoutSeconds = 20;

		private readonly Lazy<ILlmClient> _client;

		public LlmProtocolCaseSuggester(Lazy<ILlmClient> client)
		{
			_client = client;
		}

		public bool IsConfigured => AiDestination.AiDestinationAdvisorService.OperatorConfigured();

		public async Task<ProtocolAiPick> SuggestAsync(int departmentId, string callText, IReadOnlyList<ProtocolAiCandidate> candidates, CancellationToken cancellationToken)
		{
			var request = new LlmRequest(AiConfig.Model, Messages(callText, candidates), new[] { Tool() }, 600,
				AiCacheIsolation.SaltFor(departmentId, AiConfig.SendCacheSalt, AiConfig.CacheSaltKey, AiConfig.AuditHmacKey));

			LlmResult result;
			using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
			{
				deadline.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
				try
				{
					result = await _client.Value.CompleteAsync(request, deadline.Token);
				}
				catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
				{
					throw new TimeoutException("Protocol suggestion inference timed out.");
				}
			}

			return Parse(result);
		}

		public static IReadOnlyList<LlmMessage> Messages(string callText, IReadOnlyList<ProtocolAiCandidate> candidates)
		{
			var catalog = candidates.Select(c => new
			{
				id = c.ProtocolId,
				code = c.Code,
				name = c.Name,
				questions = (c.Graph?.Nodes ?? new List<ProtocolDecisionNode>()).Where(n => n != null && n.IsQuestion).Select(n => new
				{
					step = n.Key,
					text = n.Text,
					type = ((ProtocolQuestionTypes)(n.QuestionType ?? 0)).ToString(),
					answers = n.QuestionType == (int)ProtocolQuestionTypes.FreeText ? null : (n.Answers ?? new List<ProtocolDecisionAnswer>()).Select(a => new
					{
						key = a.Key,
						label = a.Label,
						min = a.Min,
						max = a.Max
					})
				})
			});

			var system = "You help an emergency dispatcher. From the caller's report, pick the ONE dispatch protocol below that fits best and prefill " +
						 "answers to its questions ONLY where the report states them. Never guess an answer; leave a question out when the report does " +
						 "not answer it. Answer with the suggest_protocol tool: the protocol id, answers as step key to answer key (a number for number " +
						 "questions, the caller's words for free text), a confidence from 0 to 1, and a one-sentence reason. If none fits, use confidence 0.";
			var user = "Protocols:\n" + JsonConvert.SerializeObject(catalog) + "\n\nCaller report:\n" + callText;
			return new List<LlmMessage> { new LlmMessage("system", system), new LlmMessage("user", user) };
		}

		public static LlmTool Tool()
		{
			const string schema = "{\"type\":\"object\",\"properties\":{\"protocolId\":{\"type\":\"integer\"},\"confidence\":{\"type\":\"number\"}," +
								  "\"reason\":{\"type\":\"string\"},\"answers\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"step\":{\"type\":\"string\"}," +
								  "\"answer\":{\"type\":\"string\"}},\"required\":[\"step\",\"answer\"]}}},\"required\":[\"protocolId\",\"confidence\"]}";
			using var document = JsonDocument.Parse(schema);
			return new LlmTool(ToolName, "Suggest the dispatch protocol and prefill its case answers.", document.RootElement.Clone());
		}

		/// <summary>The tool call's arguments as a pick; null when the model did not call the tool or the arguments are unreadable.</summary>
		public static ProtocolAiPick Parse(LlmResult result)
		{
			var call = result?.ToolCalls?.FirstOrDefault(t => t != null && string.Equals(t.Name, ToolName, StringComparison.Ordinal));
			if (call == null || string.IsNullOrWhiteSpace(call.Arguments))
				return null;

			try
			{
				using var document = JsonDocument.Parse(call.Arguments);
				var root = document.RootElement;
				if (!root.TryGetProperty("protocolId", out var id) || !id.TryGetInt32(out var protocolId))
					return null;

				var pick = new ProtocolAiPick
				{
					ProtocolId = protocolId,
					Confidence = root.TryGetProperty("confidence", out var confidence) && confidence.TryGetDecimal(out var c) ? c : 0m,
					Rationale = root.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null,
					ModelRevision = AiConfig.ModelRevision
				};

				if (root.TryGetProperty("answers", out var answers) && answers.ValueKind == JsonValueKind.Array)
				{
					foreach (var item in answers.EnumerateArray())
					{
						if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("step", out var step) || !item.TryGetProperty("answer", out var answer))
							continue;
						var key = step.ValueKind == JsonValueKind.String ? step.GetString() : null;
						var value = answer.ValueKind == JsonValueKind.String ? answer.GetString() : answer.ValueKind == JsonValueKind.Number ? answer.GetRawText() : null;
						if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value) && !pick.Answers.ContainsKey(key.Trim()))
							pick.Answers[key.Trim()] = value;
					}
				}

				return pick;
			}
			catch (System.Text.Json.JsonException)
			{
				return null;
			}
		}
	}
}
