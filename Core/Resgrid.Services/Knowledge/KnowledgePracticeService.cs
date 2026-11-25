using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Knowledge
{
	/// <summary>
	/// Scenario practice from a protocol version (K3 tutor; protocols-training-knowledge-plan §13a, §16.3, §17.2 row "AI"). In order:
	/// 1. the gate: K1's (<see cref="IAiKnowledgeService.IsEntitledAsync"/>, Enhanced AI with Ai.Knowledge) and the LMS add-on (the tutor needs
	///    both), a current member who may view protocols, the operator's model configured; fails closed;
	/// 2. the protocol must be readable by the member (the library's rule), published, not "no AI" and not licensed (decision 14); the
	///    version in force is chunked by block exactly as the knowledge index cuts it;
	/// 3. Advanced Data Protection in any state but Disabled keeps department content local: no practice;
	/// 4. the model writes a short scenario and questions citing the sections; the output is validated (K1's number-in-source rule);
	/// 5. the member's answers are scored against the cited sections only, re-read from the stored scenario (never from the client), and
	///    refused as stale when the protocol changed in between.
	/// Practice only: this service holds no writer of learning records, certifications, quiz attempts or enrollments, so it cannot write one
	/// (guard tested). Each scenario and each scoring is logged to AiGenerations; the member's answers are not stored.
	/// </summary>
	public class KnowledgePracticeService : IKnowledgePracticeService
	{
		public const string Feature = "Tutor";
		public const int ReservedTokens = 8192;
		public const int MaxSources = 8;
		public const int QuestionCount = 3;
		public const int MinimumContentCharacters = 80;

		private readonly IAiKnowledgeService _knowledge;
		private readonly ILmsAccessService _lms;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IProtocolAccessService _protocolAccess;
		private readonly IProtocolVersionsRepository _versions;
		private readonly IAdminAssistConversationProtection _conversationProtection;
		private readonly IAiBackgroundAdmission _admission;
		private readonly IAiUsageMeter _usage;
		private readonly Lazy<ILlmClient> _client;
		private readonly IKnowledgeGapsRepository _generations;
		private readonly IDepartmentSettingsRepository _settings;
		private readonly TimeProvider _clock;

		public KnowledgePracticeService(IAiKnowledgeService knowledge, ILmsAccessService lms, IDepartmentsService departments, IDepartmentDataProtectionService protection,
			IProtocolAccessService protocolAccess, IProtocolVersionsRepository versions, IAdminAssistConversationProtection conversationProtection,
			IAiBackgroundAdmission admission, IAiUsageMeter usage, Lazy<ILlmClient> client, IKnowledgeGapsRepository generations, IDepartmentSettingsRepository settings,
			TimeProvider clock)
		{
			_knowledge = knowledge;
			_lms = lms;
			_departments = departments;
			_protection = protection;
			_protocolAccess = protocolAccess;
			_versions = versions;
			_conversationProtection = conversationProtection;
			_admission = admission;
			_usage = usage;
			_client = client;
			_generations = generations;
			_settings = settings;
			_clock = clock ?? TimeProvider.System;
		}

		#region Gate

		/// <summary>The tutor's gate, cheapest first; null when open, else the refusal.</summary>
		private async Task<string> GateAsync(KnowledgeReader reader)
		{
			if (reader == null || reader.DepartmentId <= 0 || string.IsNullOrWhiteSpace(reader.UserId))
				return KnowledgeOutcomes.Invalid;
			if (!reader.CanViewProtocols)
				return KnowledgeOutcomes.NotEntitled;
			if (!await _knowledge.IsEntitledAsync(reader.DepartmentId))
				return KnowledgeOutcomes.NotEntitled;
			if (!await LmsOpenAsync(reader.DepartmentId))
				return KnowledgeOutcomes.NotEntitled;
			if (!await KnowledgeModelCalls.IsCurrentMemberAsync(_departments, reader.DepartmentId, reader.UserId))
				return KnowledgeOutcomes.NotEntitled;
			if (!AiKnowledgeService.OperatorConfigured())
				return KnowledgeOutcomes.Unconfigured;
			return null;
		}

		/// <summary>The LMS half of the tutor's gate (§17.2: the tutor needs both Enhanced AI and the LMS): the flag and an active add-on.</summary>
		private async Task<bool> LmsOpenAsync(int departmentId)
		{
			try
			{
				return await _lms.CanRunItemAsync(departmentId, LmsRunnableOrigins.Department);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The LMS gate could not be read for department {departmentId}.");
				return false;
			}
		}

		public async Task<KnowledgePracticeStatus> GetStatusAsync(KnowledgeReader reader, CancellationToken cancellationToken = default)
		{
			var refused = await GateAsync(reader);
			if (refused != null)
				return new KnowledgePracticeStatus { Reason = refused };
			if (await KnowledgeModelCalls.IsProtectedAsync(_protection, reader.DepartmentId))
				return new KnowledgePracticeStatus { Reason = KnowledgePracticeOutcomes.KeptLocal };
			return new KnowledgePracticeStatus { Available = true };
		}

		#endregion Gate

		#region Sources

		private sealed record LoadedVersion(DispatchProtocol Protocol, ProtocolVersion Version, List<KnowledgeChunk> Chunks, string Outcome);

		/// <summary>The protocol as the member may open it and a version of it, chunked as the index cuts it.</summary>
		private async Task<LoadedVersion> LoadAsync(KnowledgeReader reader, int protocolId, int? versionId)
		{
			if (protocolId <= 0)
				return new LoadedVersion(null, null, null, KnowledgePracticeOutcomes.NotFound);
			var protocol = await _protocolAccess.GetReadableAsync(reader.DepartmentId, reader.UserId, protocolId);
			if (protocol == null || protocol.DepartmentId != reader.DepartmentId || protocol.DispatchProtocolId != protocolId || protocol.IsDeleted)
				return new LoadedVersion(null, null, null, KnowledgePracticeOutcomes.NotFound);
			if (protocol.ExcludeFromAi || protocol.IsLicensedContent)
				return new LoadedVersion(protocol, null, null, KnowledgePracticeOutcomes.Excluded);

			var id = versionId ?? protocol.CurrentVersionId;
			if (!id.HasValue)
				return new LoadedVersion(protocol, null, null, KnowledgePracticeOutcomes.NotFound);
			var version = (await _versions.GetByIdsAsync(reader.DepartmentId, new[] { id.Value }, true) ?? new List<ProtocolVersion>())
				.FirstOrDefault(v => v.ProtocolVersionId == id.Value && v.DispatchProtocolId == protocolId && v.DepartmentId == reader.DepartmentId);
			// A new scenario is written from the version in force only; a scenario already written is scored on its own version.
			if (version == null || (!versionId.HasValue && !ProtocolKnowledgeSource.IsInForce(version, _clock.GetUtcNow().UtcDateTime)) ||
				(versionId.HasValue && version.Status != (int)ProtocolVersionStatuses.Published && version.Status != (int)ProtocolVersionStatuses.Superseded))
				return new LoadedVersion(protocol, null, null, KnowledgePracticeOutcomes.NotFound);

			var chunks = ProtocolKnowledgeSource.BuildVersionChunks(reader.DepartmentId, protocol, version, null)
				.Where(c => !string.IsNullOrWhiteSpace(c.Text)).ToList();
			return new LoadedVersion(protocol, version, chunks, null);
		}

		private static List<AiKnowledgeSource> Sources(IReadOnlyList<KnowledgeChunk> chunks) =>
			chunks.Select((c, i) => new AiKnowledgeSource("S" + (i + 1).ToString(CultureInfo.InvariantCulture), c.Title, c.VersionLabel, AiKnowledgePrompt.Date(c.EffectiveOn),
				c.HeadingPath, c.Text)).ToList();

		private static KnowledgeCitation Citation(string reference, KnowledgeChunk chunk) => new KnowledgeCitation
		{
			Ref = reference,
			SourceKind = (int)KnowledgeSourceKinds.Protocol,
			ItemId = chunk.ItemId,
			VersionId = chunk.VersionId,
			VersionLabel = chunk.VersionLabel,
			Anchor = chunk.Anchor,
			HeadingPath = chunk.HeadingPath,
			Title = chunk.Title,
			EffectiveOn = chunk.EffectiveOn,
			Excerpt = string.IsNullOrWhiteSpace(chunk.Text) ? string.Empty : chunk.Text.Length <= 240 ? chunk.Text : chunk.Text.Substring(0, 240).TrimEnd() + "…"
		};

		/// <summary>What is kept (encrypted, in AiGenerations) to score against: the version, the scenario, the questions and each source's anchor and text hash.</summary>
		private sealed class StoredScenario
		{
			public int P { get; set; }
			public int V { get; set; }
			public string S { get; set; }
			public List<StoredQuestion> Q { get; set; } = new List<StoredQuestion>();
			public List<StoredSource> Src { get; set; } = new List<StoredSource>();
		}

		private sealed class StoredQuestion
		{
			public string T { get; set; }
			public List<string> R { get; set; } = new List<string>();
		}

		private sealed class StoredSource
		{
			public string R { get; set; }
			public string A { get; set; }
			public string H { get; set; }
		}

		#endregion Sources

		#region Start

		public async Task<KnowledgePracticeScenario> StartAsync(KnowledgeReader reader, int protocolId, CancellationToken cancellationToken = default)
		{
			var refused = await GateAsync(reader);
			if (refused != null)
				return new KnowledgePracticeScenario { Outcome = refused, PromptVersion = AiTutorPrompt.ScenarioVersion };

			var loaded = await LoadAsync(reader, protocolId, null);
			if (loaded.Outcome != null)
				return new KnowledgePracticeScenario { Outcome = loaded.Outcome, ProtocolId = protocolId, PromptVersion = AiTutorPrompt.ScenarioVersion };

			var chunks = loaded.Chunks.Take(MaxSources).ToList();
			var scenario = new KnowledgePracticeScenario
			{
				ProtocolId = protocolId,
				ProtocolVersionId = loaded.Version.ProtocolVersionId,
				Title = loaded.Protocol.Name,
				VersionLabel = loaded.Version.Label,
				PromptVersion = AiTutorPrompt.ScenarioVersion
			};
			if (chunks.Sum(c => c.Text.Trim().Length) < MinimumContentCharacters)
			{
				scenario.Outcome = KnowledgePracticeOutcomes.NoContent;
				return scenario;
			}
			if (await KnowledgeModelCalls.IsProtectedAsync(_protection, reader.DepartmentId))
			{
				scenario.Outcome = KnowledgePracticeOutcomes.KeptLocal;
				return scenario;
			}

			var actor = new AdminAssistActor(reader.DepartmentId, reader.UserId, reader.Locale ?? "en");
			var admission = await KnowledgeModelCalls.ReserveAsync(_usage, _admission, _clock, reader.DepartmentId, Feature, ReservedTokens, cancellationToken);
			if (admission.Outcome != null)
			{
				scenario.Outcome = admission.Outcome;
				return scenario;
			}

			var used = 0;
			var ledger = "Unavailable";
			scenario.Outcome = KnowledgeOutcomes.Unavailable;
			try
			{
				try
				{
					// Fail closed: an exchange that cannot be audited is never sent to the model.
					await _conversationProtection.PreflightAsync(actor, cancellationToken);
				}
				catch (UnauthorizedAccessException)
				{
					scenario.Outcome = KnowledgeOutcomes.Unconfigured;
					return scenario;
				}

				var sources = Sources(chunks);
				var result = await CompleteAsync(reader.DepartmentId, AiTutorPrompt.ScenarioMessages(sources, QuestionCount), AiTutorPrompt.Tool(0), cancellationToken);
				if (result == null)
					return scenario;
				used = Math.Min(admission.Reservation.Tokens, Math.Max(0, result.InputTokens + result.OutputTokens));

				AiPracticeScenario validated;
				try
				{
					using var document = AiTutorPrompt.Parse(result, 0);
					validated = AiTutorPrompt.ValidateScenario(document, sources);
				}
				catch (Exception ex) when (ex is ArgumentException || ex is JsonException)
				{
					// Nothing the model wrote is shown.
					ledger = "InvalidOutput";
					scenario.Outcome = KnowledgePracticeOutcomes.Refused;
					scenario.ExchangeId = await KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, actor, AiTutorPrompt.ScenarioVersion,
						"practice-scenario protocol:" + protocolId.ToString(CultureInfo.InvariantCulture), Evidence(chunks), KnowledgePracticeOutcomes.Refused,
						result.InputTokens, result.OutputTokens, cancellationToken);
					return scenario;
				}

				ledger = "Answered";
				var stored = new StoredScenario
				{
					P = protocolId,
					V = loaded.Version.ProtocolVersionId,
					S = validated.Scenario,
					Q = validated.Questions.Select(q => new StoredQuestion { T = q.Text, R = q.Refs.ToList() }).ToList(),
					Src = sources.Select((s, i) => new StoredSource { R = s.Ref, A = chunks[i].Anchor, H = KnowledgeChunker.Hash(chunks[i].Text) }).ToList()
				};
				scenario.Outcome = KnowledgePracticeOutcomes.Ready;
				scenario.Scenario = validated.Scenario;
				scenario.Questions = validated.Questions.Select((q, i) => new KnowledgePracticeQuestion
				{
					Key = "Q" + (i + 1).ToString(CultureInfo.InvariantCulture),
					Text = q.Text,
					Refs = q.Refs.ToList()
				}).ToList();
				var cited = new HashSet<string>(validated.Questions.SelectMany(q => q.Refs), StringComparer.Ordinal);
				scenario.Citations = sources.Select((s, i) => (s.Ref, Chunk: chunks[i])).Where(x => cited.Contains(x.Ref)).Select(x => Citation(x.Ref, x.Chunk)).ToList();
				scenario.ExchangeId = await KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, actor, AiTutorPrompt.ScenarioVersion,
					JsonSerializer.Serialize(stored), Evidence(chunks), KnowledgePracticeOutcomes.Ready, result.InputTokens, result.OutputTokens, cancellationToken);
				if (scenario.ExchangeId == null)
				{
					// A scenario that cannot be read back cannot be scored: it is not offered.
					scenario.Outcome = KnowledgeOutcomes.Unavailable;
					scenario.Questions = new List<KnowledgePracticeQuestion>();
					scenario.Scenario = null;
				}
				return scenario;
			}
			finally
			{
				await KnowledgeModelCalls.CompleteAsync(_usage, admission.Reservation, used, ledger);
			}
		}

		private static IEnumerable<string> Evidence(IEnumerable<KnowledgeChunk> chunks) =>
			chunks.Select(c => "protocol:" + c.ItemId + ":" + c.VersionId + ":" + (c.Anchor ?? string.Empty));

		/// <summary>One model call with the answer deadline; null when the model is unavailable (nothing is charged).</summary>
		private async Task<LlmResult> CompleteAsync(int departmentId, IReadOnlyList<LlmMessage> messages, LlmTool tool, CancellationToken cancellationToken)
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(KnowledgeConfig.AnswerTimeoutSeconds * 2, 10, 180)));
			try
			{
				return await _client.Value.CompleteAsync(new LlmRequest(AiConfig.Model, messages, new[] { tool }, Math.Clamp(KnowledgeConfig.AnswerMaxOutputTokens * 2, 512, 4096),
					AiCacheIsolation.SaltFor(departmentId, AiConfig.SendCacheSalt, AiConfig.CacheSaltKey, AiConfig.AuditHmacKey)), deadline.Token);
			}
			catch (Exception ex) when (ex is LlmUnavailableException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
			{
				return null;
			}
		}

		#endregion Start

		#region Score

		public async Task<KnowledgePracticeResult> ScoreAsync(KnowledgeReader reader, string exchangeId, IReadOnlyList<KnowledgePracticeAnswer> answers,
			CancellationToken cancellationToken = default)
		{
			var practice = new KnowledgePracticeResult { PromptVersion = AiTutorPrompt.ScoreVersion };
			var refused = await GateAsync(reader);
			if (refused != null)
			{
				practice.Outcome = refused;
				return practice;
			}
			if (!Guid.TryParseExact(exchangeId, "D", out _) || answers == null)
			{
				practice.Outcome = KnowledgeOutcomes.Invalid;
				return practice;
			}
			if (await KnowledgeModelCalls.IsProtectedAsync(_protection, reader.DepartmentId))
			{
				practice.Outcome = KnowledgePracticeOutcomes.KeptLocal;
				return practice;
			}

			// The member's own scenario, as stored: never what the client sends back.
			var actor = new AdminAssistActor(reader.DepartmentId, reader.UserId, reader.Locale ?? "en");
			var row = await _generations.GetGenerationAsync(reader.DepartmentId, reader.UserId, exchangeId, cancellationToken);
			if (row == null || !string.Equals(row.PromptVersion, AiTutorPrompt.ScenarioVersion, StringComparison.Ordinal) ||
				!string.Equals(row.Outcome, KnowledgePracticeOutcomes.Ready, StringComparison.Ordinal))
			{
				practice.Outcome = KnowledgePracticeOutcomes.NotFound;
				return practice;
			}

			StoredScenario stored;
			try
			{
				var content = await _conversationProtection.ReadAsync(actor, row, cancellationToken);
				stored = string.IsNullOrWhiteSpace(content?.Question) ? null : JsonSerializer.Deserialize<StoredScenario>(content.Question);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, "A practice scenario could not be read back.");
				stored = null;
			}
			if (stored == null || stored.Q == null || stored.Q.Count == 0 || stored.Src == null || stored.Src.Count == 0)
			{
				practice.Outcome = KnowledgePracticeOutcomes.NotFound;
				return practice;
			}

			// Scored against the same version's sections, unchanged since the scenario was written (and still readable, still allowed to AI).
			var loaded = await LoadAsync(reader, stored.P, stored.V);
			if (loaded.Outcome != null)
			{
				practice.Outcome = loaded.Outcome;
				return practice;
			}
			var chunks = new List<KnowledgeChunk>();
			foreach (var source in stored.Src)
			{
				var chunk = loaded.Chunks.FirstOrDefault(c => string.Equals(c.Anchor, source.A, StringComparison.Ordinal) && string.Equals(KnowledgeChunker.Hash(c.Text), source.H, StringComparison.Ordinal));
				if (chunk == null)
				{
					practice.Outcome = KnowledgePracticeOutcomes.Stale;
					return practice;
				}
				chunks.Add(chunk);
			}
			var sources = Sources(chunks);

			var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var answer in answers.Where(a => a != null && !string.IsNullOrWhiteSpace(a.Key)).Take(AiTutorPrompt.MaxQuestions + 4))
			{
				var text = CleanAnswer(answer.Text);
				if (text != null)
					byKey[answer.Key.Trim()] = text;
			}
			var answered = stored.Q.Take(AiTutorPrompt.MaxQuestions).Select((q, i) =>
			{
				var key = "Q" + (i + 1).ToString(CultureInfo.InvariantCulture);
				return new AiPracticeAnswered(key, q.T, (q.R ?? new List<string>()).Where(r => sources.Any(s => s.Ref == r)).ToList(), byKey.TryGetValue(key, out var a) ? a : string.Empty);
			}).ToList();
			if (answered.All(a => a.Answer.Length == 0))
			{
				practice.Outcome = KnowledgeOutcomes.Invalid;
				return practice;
			}

			var admission = await KnowledgeModelCalls.ReserveAsync(_usage, _admission, _clock, reader.DepartmentId, Feature, ReservedTokens, cancellationToken);
			if (admission.Outcome != null)
			{
				practice.Outcome = admission.Outcome;
				return practice;
			}

			var used = 0;
			var ledger = "Unavailable";
			practice.Outcome = KnowledgeOutcomes.Unavailable;
			try
			{
				try
				{
					await _conversationProtection.PreflightAsync(actor, cancellationToken);
				}
				catch (UnauthorizedAccessException)
				{
					practice.Outcome = KnowledgeOutcomes.Unconfigured;
					return practice;
				}

				var result = await CompleteAsync(reader.DepartmentId, AiTutorPrompt.ScoreMessages(sources, stored.S, answered), AiTutorPrompt.Tool(1), cancellationToken);
				if (result == null)
					return practice;
				used = Math.Min(admission.Reservation.Tokens, Math.Max(0, result.InputTokens + result.OutputTokens));

				IReadOnlyList<AiPracticeScore> scores;
				try
				{
					using var document = AiTutorPrompt.Parse(result, 1);
					scores = AiTutorPrompt.ValidateScores(document, sources, answered);
				}
				catch (Exception ex) when (ex is ArgumentException || ex is JsonException)
				{
					ledger = "InvalidOutput";
					practice.Outcome = KnowledgePracticeOutcomes.Refused;
					practice.ExchangeId = await KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, actor, AiTutorPrompt.ScoreVersion,
						"practice-score " + exchangeId, Evidence(chunks), KnowledgePracticeOutcomes.Refused, result.InputTokens, result.OutputTokens, cancellationToken);
					return practice;
				}

				ledger = "Answered";
				practice.Outcome = KnowledgePracticeOutcomes.Scored;
				practice.Items = scores.Select(s => new KnowledgePracticeItemScore { Key = s.Key, Score = s.Score, Feedback = s.Feedback, Refs = s.Refs.ToList() }).ToList();
				var cited = new HashSet<string>(practice.Items.SelectMany(i => i.Refs).Concat(answered.SelectMany(a => a.Refs)), StringComparer.Ordinal);
				practice.Citations = sources.Select((s, i) => (s.Ref, Chunk: chunks[i])).Where(x => cited.Contains(x.Ref)).Select(x => Citation(x.Ref, x.Chunk)).ToList();
				practice.QuotesDose = practice.Items.Any(i => AiKnowledgePrompt.QuotesDose(i.Feedback));
				practice.DosingToolUrl = practice.QuotesDose ? (await KnowledgeSettingsReader.ReadAsync(_settings, reader.DepartmentId)).DosingToolUrl : null;
				// The answers themselves are not stored: the row names the scenario it scored.
				practice.ExchangeId = await KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, actor, AiTutorPrompt.ScoreVersion,
					"practice-score " + exchangeId, Evidence(chunks), KnowledgePracticeOutcomes.Scored, result.InputTokens, result.OutputTokens, cancellationToken);
				return practice;
			}
			finally
			{
				await KnowledgeModelCalls.CompleteAsync(_usage, admission.Reservation, used, ledger);
			}
		}

		public static string CleanAnswer(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;
			var clean = new string(text.Where(ch => !char.IsControl(ch) || ch == '\n').ToArray()).Trim();
			if (clean.Length == 0)
				return null;
			return clean.Length <= KnowledgePracticeAnswer.TextMaxLength ? clean : clean.Substring(0, KnowledgePracticeAnswer.TextMaxLength);
		}

		#endregion Score
	}
}
