using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Knowledge
{
	/// <summary>
	/// Department knowledge answers with citations (K1, protocols-training-knowledge-plan §16.2–16.5), behind <c>Ai.Knowledge</c> through the
	/// Enhanced AI entitlement (flags fresh, add-on window, module switch; fails closed). One question:
	/// 1. the reader's scope is built first (each source's live read rule), then the question is embedded with a short deadline (an outage
	///    falls back to keywords and says so) and the department's chunks are scored exactly after the department and audience filters;
	/// 2. nothing adequate → "not covered" with the nearest items, and a knowledge gap is stored (no model call);
	/// 3. Advanced Data Protection in any state but Disabled keeps the question local (admin-assist-question-routing): sources only;
	/// 4. otherwise the grounded prompt runs on the operator's model (the Assistant's ai-text route) and its output is validated: every part
	///    cites shown sources only, and no number appears that the cited sources do not contain (no model-computed doses). A refused output
	///    is shown as the sources alone.
	/// Each exchange is logged to AiGenerations (prompt version, model revision and digest, the source ids) under the Enhanced AI audit rules.
	/// </summary>
	public class AiKnowledgeService : IAiKnowledgeService
	{
		public const string Feature = "Knowledge";
		public const int ReservedTokens = 8192;
		public const int MaxAdmissionWaitSeconds = 8;
		public const int NearestShown = 3;

		private readonly IEnhancedAiAccessService _enhancedAi;
		private readonly IFeatureToggleService _flags;
		private readonly IDepartmentsService _departments;
		private readonly IPersonnelRolesService _roles;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IEnumerable<IKnowledgeCorpusSource> _sources;
		private readonly IKnowledgeRetriever _retriever;
		private readonly IEmbeddingProvider _embeddings;
		private readonly IKnowledgeIndexService _index;
		private readonly IKnowledgeIndexRepository _indexRepository;
		private readonly IKnowledgeGapsService _gaps;
		private readonly IKnowledgeGapsRepository _gapsRepository;
		private readonly IAdminAssistConversationProtection _conversationProtection;
		private readonly IAiBackgroundAdmission _admission;
		private readonly IAiUsageMeter _usage;
		private readonly Lazy<ILlmClient> _client;
		private readonly IDepartmentSettingsRepository _settingsRepository;
		private readonly IDepartmentSettingsService _settingsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IAuthorizationService _authorization;
		private readonly Lazy<ICallsService> _calls;
		private readonly IProtocolVersionsRepository _versions;
		private readonly IEnumerable<IKnowledgeVersionContentSource> _versionSources;
		private readonly IKnowledgeCallContextService _callContext;
		private readonly TimeProvider _clock;

		public AiKnowledgeService(IEnhancedAiAccessService enhancedAi, IFeatureToggleService flags, IDepartmentsService departments, IPersonnelRolesService roles,
			IDepartmentDataProtectionService protection, IEnumerable<IKnowledgeCorpusSource> sources, IKnowledgeRetriever retriever, IEmbeddingProvider embeddings,
			IKnowledgeIndexService index, IKnowledgeIndexRepository indexRepository, IKnowledgeGapsService gaps, IKnowledgeGapsRepository gapsRepository,
			IAdminAssistConversationProtection conversationProtection, IAiBackgroundAdmission admission, IAiUsageMeter usage, Lazy<ILlmClient> client,
			IDepartmentSettingsRepository settingsRepository, IDepartmentSettingsService settingsService, IEventAggregator eventAggregator,
			IAuthorizationService authorization, Lazy<ICallsService> calls, IProtocolVersionsRepository versions, IEnumerable<IKnowledgeVersionContentSource> versionSources,
			IKnowledgeCallContextService callContext, TimeProvider clock)
		{
			_enhancedAi = enhancedAi;
			_flags = flags;
			_departments = departments;
			_roles = roles;
			_protection = protection;
			_sources = sources ?? Enumerable.Empty<IKnowledgeCorpusSource>();
			_retriever = retriever;
			_embeddings = embeddings;
			_index = index;
			_indexRepository = indexRepository;
			_gaps = gaps;
			_gapsRepository = gapsRepository;
			_conversationProtection = conversationProtection;
			_admission = admission;
			_usage = usage;
			_client = client;
			_settingsRepository = settingsRepository;
			_settingsService = settingsService;
			_eventAggregator = eventAggregator;
			_authorization = authorization;
			_calls = calls;
			_versions = versions;
			_versionSources = versionSources ?? Enumerable.Empty<IKnowledgeVersionContentSource>();
			_callContext = callContext;
			_clock = clock ?? TimeProvider.System;
		}

		#region Gate and status

		public async Task<bool> IsEntitledAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;
			try
			{
				// An operator-listed self-hosted department needs the rollout flags and the module switch, not a purchase (the Intake precedent).
				if (AiOperatorSettings.IsListedDepartment(AiConfig.SelfHostedDepartmentIds, departmentId))
					return await _enhancedAi.IsEnabledAsync(departmentId) && (await _flags.EvaluateFreshAsync(FeatureFlagKeys.AiKnowledge, departmentId))?.IsEnabled == true;
				return await _enhancedAi.CanUseAsync(departmentId, FeatureFlagKeys.AiKnowledge);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Knowledge entitlement could not be read for department {departmentId}.");
				return false;
			}
		}

		/// <summary>The operator's model is a valid, reviewed, pinned endpoint (the rule every Enhanced AI feature uses).</summary>
		public static bool OperatorConfigured() => Intake.IntakeConversationAssistant.OperatorConfigured();

		public async Task<KnowledgeStatus> GetStatusAsync(KnowledgeReader reader, CancellationToken cancellationToken = default)
		{
			var status = new KnowledgeStatus { Reason = KnowledgeOutcomes.Invalid };
			if (reader == null || reader.DepartmentId <= 0 || string.IsNullOrWhiteSpace(reader.UserId))
				return status;
			if (!await IsEntitledAsync(reader.DepartmentId))
				return new KnowledgeStatus { Reason = KnowledgeOutcomes.NotEntitled };
			if (!OperatorConfigured())
				return new KnowledgeStatus { Reason = KnowledgeOutcomes.Unconfigured };

			var (member, admin) = await MembershipAsync(reader.DepartmentId, reader.UserId);
			if (!member)
				return new KnowledgeStatus { Reason = KnowledgeOutcomes.NotEntitled };

			return new KnowledgeStatus
			{
				Available = true,
				IsAdmin = admin,
				CanSeeGaps = await _gaps.CanSeeGapsAsync(reader.DepartmentId, reader.UserId),
				QuestionsKeptLocal = await IsProtectedAsync(reader.DepartmentId)
			};
		}

		private async Task<(bool Member, bool Admin)> MembershipAsync(int departmentId, string userId)
		{
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			if (department == null || department.DepartmentId != departmentId)
				return (false, false);
			var owner = string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase);
			var member = owner || DepartmentMemberStateHelper.IsCurrentMember(await _departments.GetDepartmentMemberAsync(userId, departmentId, false), departmentId);
			return (member, member && (owner || department.IsUserAnAdmin(userId)));
		}

		/// <summary>Any Advanced Data Protection state but Disabled keeps the question local; an unreadable state does too.</summary>
		private async Task<bool> IsProtectedAsync(int departmentId)
		{
			try
			{
				return await _protection.GetStateAsync(departmentId, true) != DepartmentDataProtectionState.Disabled;
			}
			catch (Exception)
			{
				return true;
			}
		}

		/// <summary>
		/// The reader's retrievable items, before any scoring: current membership and department administration, then each source's own live
		/// read rule. A source that fails adds nothing (unknown access is no access).
		/// </summary>
		public async Task<KnowledgeReaderScope> BuildScopeAsync(KnowledgeReader reader, CancellationToken cancellationToken = default)
		{
			var scope = new KnowledgeReaderScope { DepartmentId = reader?.DepartmentId ?? 0, UserId = reader?.UserId };
			if (reader == null || reader.DepartmentId <= 0 || string.IsNullOrWhiteSpace(reader.UserId))
				return scope;

			var (member, admin) = await MembershipAsync(reader.DepartmentId, reader.UserId);
			scope.IsCurrentMember = member;
			scope.IsDepartmentAdmin = admin;
			if (!member)
				return scope;

			foreach (var source in _sources)
			{
				try
				{
					await source.AddReadableAsync(reader, scope, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "A knowledge source could not decide what the reader may open.");
				}
			}

			return scope;
		}

		#endregion Gate and status

		#region Ask

		public async Task<KnowledgeAnswer> AskAsync(KnowledgeAskRequest request, CancellationToken cancellationToken = default)
		{
			var reader = request?.Reader;
			var question = CleanQuestion(request?.Question);
			if (reader == null || reader.DepartmentId <= 0 || string.IsNullOrWhiteSpace(reader.UserId) || question == null)
				return Refused(KnowledgeOutcomes.Invalid);

			var departmentId = reader.DepartmentId;
			if (!await IsEntitledAsync(departmentId))
				return Refused(KnowledgeOutcomes.NotEntitled);
			if (!OperatorConfigured())
				return Refused(KnowledgeOutcomes.Unconfigured);

			var scope = await BuildScopeAsync(reader, cancellationToken);
			if (!scope.IsCurrentMember)
				return Refused(KnowledgeOutcomes.NotEntitled);

			await EnsureIndexAsync(departmentId, cancellationToken);
			var keptLocal = await IsProtectedAsync(departmentId);
			var actor = new AdminAssistActor(departmentId, reader.UserId, reader.Locale ?? "en");

			// "Ask about this call's protocol": the version the call cites, built now (it may be older than the one indexed).
			var retrieval = new KnowledgeRetrievalRequest
			{
				Scope = scope,
				Question = question,
				TopK = KnowledgeConfig.TopK,
				MaxPerItem = KnowledgeConfig.MaxChunksPerItem,
				OnlyKind = request.ScopeKind,
				OnlyItemId = request.ScopeItemId,
				// Round 5F: answers come from an approved translation in the asker's language when the item has one.
				Locale = reader.Locale
			};
			var contextKind = request.ScopeKind;
			var contextItem = request.ScopeItemId;
			string callContext = null;
			if (request.CallId.HasValue && !request.CallProtocolId.HasValue)
			{
				// K3 "Ask about this call": every version the call cites (or, citing none, the reader's own corpus) and its operational fields.
				var wide = await CallWideScopeAsync(scope, request.CallId.Value, cancellationToken);
				if (wide == null)
					return Refused(KnowledgeOutcomes.Invalid);
				if (wide.Value.Chunks != null)
				{
					retrieval.Scope = wide.Value.Scope;
					retrieval.Chunks = wide.Value.Chunks;
				}
				retrieval.OnlyKind = null;
				retrieval.OnlyItemId = null;
				callContext = wide.Value.Context?.IsEmpty == false ? wide.Value.Context.ToPromptText() : null;
				var terms = wide.Value.Context?.RetrievalTerms();
				if (!string.IsNullOrWhiteSpace(terms))
					retrieval.Question = question + "\n" + terms;
				contextKind = wide.Value.ProtocolIds.Count == 1 ? (int)KnowledgeSourceKinds.Protocol : null;
				contextItem = wide.Value.ProtocolIds.Count == 1 ? wide.Value.ProtocolIds[0] : null;
			}
			else if (request.CallId.HasValue || request.CallProtocolId.HasValue)
			{
				var call = await CallScopeAsync(scope, request.CallId, request.CallProtocolId, cancellationToken);
				if (call == null)
					return Refused(KnowledgeOutcomes.Invalid);
				retrieval.Scope = call.Value.Scope;
				retrieval.Chunks = call.Value.Chunks;
				retrieval.OnlyKind = null;
				retrieval.OnlyItemId = null;
				contextKind = (int)KnowledgeSourceKinds.Protocol;
				contextItem = call.Value.ProtocolId;
			}
			else if (request.ScopeKind.HasValue && !string.IsNullOrWhiteSpace(request.ScopeItemId) && VersionSourceFor(request.ScopeKind.Value) != null)
			{
				// K3 "Ask this course": the version the reader takes, built now by the kind's own source and gates (the LMS for courses).
				var kind = (KnowledgeSourceKinds)request.ScopeKind.Value;
				KnowledgeVersionContent content;
				try
				{
					content = await VersionSourceFor(request.ScopeKind.Value).LoadForReaderAsync(reader, kind, request.ScopeItemId.Trim(), request.ScopeVersionId, cancellationToken);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					Logging.LogException(ex, "A knowledge version source could not build the reader's version.");
					content = null;
				}
				if (content == null || content.Chunks == null)
					return Refused(content?.Outcome == KnowledgeOutcomes.NotEntitled ? KnowledgeOutcomes.NotEntitled : KnowledgeOutcomes.Invalid);
				var versionScope = CopyScope(scope);
				versionScope.Allow(kind, new[] { content.ItemId });
				retrieval.Scope = versionScope;
				retrieval.Chunks = content.Chunks;
				retrieval.OnlyKind = null;
				retrieval.OnlyItemId = null;
				contextItem = content.ItemId;
			}

			// The question's embedding: in the request, with a short deadline; never sent while Advanced Data Protection keeps it local.
			var embeddingsUnavailable = false;
			if (!keptLocal)
			{
				if (_embeddings?.IsConfigured == true)
				{
					try
					{
						retrieval.QueryVector = (await _embeddings.EmbedAsync(new[] { retrieval.Question },
							TimeSpan.FromMilliseconds(Math.Clamp(KnowledgeConfig.QueryEmbeddingTimeoutMilliseconds, 100, 10000)), cancellationToken))?.FirstOrDefault();
						retrieval.QueryModel = _embeddings.ModelId;
					}
					catch (EmbeddingUnavailableException)
					{
						embeddingsUnavailable = true;
					}
				}
				else
				{
					embeddingsUnavailable = true;
				}
			}

			var found = await _retriever.RetrieveAsync(retrieval, cancellationToken) ?? new KnowledgeRetrievalResult { Mode = KnowledgeRetrievalModes.KeywordOnly };
			var citations = Citations(found.Selected, request.OfflineVersions);
			var texts = found.Selected.Select((s, i) => (Ref: "S" + (i + 1).ToString(CultureInfo.InvariantCulture), s.Chunk.Text)).ToDictionary(x => x.Ref, x => x.Text ?? string.Empty);
			var answer = new KnowledgeAnswer
			{
				RetrievalMode = found.Mode,
				EmbeddingsUnavailable = embeddingsUnavailable,
				QuestionKeptLocal = keptLocal,
				PromptVersion = callContext != null ? AiKnowledgePrompt.CallVersion : AiKnowledgePrompt.Version,
				ModelRevision = AiConfig.ModelRevision,
				CallContextUsed = request.CallId.HasValue && !request.CallProtocolId.HasValue
			};
			var promptVersion = answer.PromptVersion;

			if (!found.Adequate)
				return await NotCoveredAsync(answer, actor, request, question, keptLocal, citations, contextKind, contextItem, 0, 0, cancellationToken);

			if (keptLocal)
			{
				answer.Outcome = KnowledgeOutcomes.Matched;
				answer.Citations = citations;
				answer.OfflineCopyStale = citations.Any(c => c.OfflineCopyStale);
				answer.ExchangeId = await LogExchangeAsync(actor, question, citations, KnowledgeOutcomes.Matched, 0, 0, cancellationToken, promptVersion);
				return answer;
			}

			var admission = await ReserveAsync(departmentId, cancellationToken);
			if (admission.Outcome != null)
			{
				answer.Outcome = admission.Outcome;
				answer.Citations = citations;
				return answer;
			}

			var used = 0;
			var ledger = "Unavailable";
			try
			{
				try
				{
					// Fail closed: an exchange that cannot be audited is never sent to the model.
					await _conversationProtection.PreflightAsync(actor, cancellationToken);
				}
				catch (UnauthorizedAccessException)
				{
					answer.Outcome = KnowledgeOutcomes.Unconfigured;
					answer.Citations = citations;
					return answer;
				}

				var sources = citations.Select(c => new AiKnowledgeSource(c.Ref, c.Title, c.VersionLabel, AiKnowledgePrompt.Date(c.EffectiveOn), c.HeadingPath, texts[c.Ref])).ToList();
				LlmResult result;
				using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(KnowledgeConfig.AnswerTimeoutSeconds, 5, 120)));
					try
					{
						result = await _client.Value.CompleteAsync(new LlmRequest(AiConfig.Model, AiKnowledgePrompt.Messages(sources, question, callContext), new[] { AiKnowledgePrompt.Tool() },
							Math.Clamp(KnowledgeConfig.AnswerMaxOutputTokens, 128, 4096),
							AiCacheIsolation.SaltFor(departmentId, AiConfig.SendCacheSalt, AiConfig.CacheSaltKey, AiConfig.AuditHmacKey)), deadline.Token);
					}
					catch (Exception ex) when (ex is LlmUnavailableException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
					{
						answer.Outcome = KnowledgeOutcomes.Unavailable;
						answer.Citations = citations;
						return answer;
					}
				}

				used = Math.Min(admission.Reservation.Tokens, Math.Max(0, result.InputTokens + result.OutputTokens));
				AiKnowledgeAnswer validated;
				try
				{
					validated = AiKnowledgePrompt.Validate(AiKnowledgePrompt.Parse(result), sources, callContext);
				}
				catch (Exception ex) when (ex is ArgumentException || ex is JsonException)
				{
					// The model's text is not shown; its sources are.
					ledger = "InvalidOutput";
					answer.Outcome = KnowledgeOutcomes.Matched;
					answer.Citations = citations;
					answer.ExchangeId = await LogExchangeAsync(actor, question, citations, KnowledgeOutcomes.Matched, result.InputTokens, result.OutputTokens, cancellationToken, promptVersion);
					return answer;
				}

				if (validated.NotCovered)
				{
					ledger = "Abstained";
					return await NotCoveredAsync(answer, actor, request, question, false, citations, contextKind, contextItem, result.InputTokens, result.OutputTokens, cancellationToken);
				}

				ledger = "Answered";
				var cited = new HashSet<string>(validated.Parts.SelectMany(p => p.Refs).Concat(validated.Conflicts.SelectMany(c => c.Refs)), StringComparer.Ordinal);
				answer.Outcome = KnowledgeOutcomes.Answered;
				answer.Parts = validated.Parts.Select(p => new KnowledgeAnswerPart { Text = p.Text, Refs = p.Refs.ToList() }).ToList();
				answer.Conflicts = validated.Conflicts.Select(c => new KnowledgeConflict { Note = c.Note, Refs = c.Refs.ToList() }).ToList();
				answer.Citations = citations.Where(c => cited.Contains(c.Ref)).ToList();
				answer.QuotesDose = validated.QuotesDose;
				answer.DosingToolUrl = validated.QuotesDose ? (await GetSettingsAsync(departmentId)).DosingToolUrl : null;
				answer.OfflineCopyStale = answer.Citations.Any(c => c.OfflineCopyStale);
				answer.ExchangeId = await LogExchangeAsync(actor, question, answer.Citations, KnowledgeOutcomes.Answered, result.InputTokens, result.OutputTokens, cancellationToken, promptVersion);
				return answer;
			}
			finally
			{
				// Failed inference is not charged: the GPU and its outages are the operator's, not the department's.
				try { await _usage.CompleteAsync(admission.Reservation, used, ledger, CancellationToken.None); }
				catch (Exception ex) { Logging.LogException(ex); }
			}
		}

		private async Task<KnowledgeAnswer> NotCoveredAsync(KnowledgeAnswer answer, AdminAssistActor actor, KnowledgeAskRequest request, string question, bool keptLocal,
			List<KnowledgeCitation> citations, int? contextKind, string contextItem, int inputTokens, int outputTokens, CancellationToken cancellationToken)
		{
			answer.Outcome = KnowledgeOutcomes.NotCovered;
			answer.Parts = new List<KnowledgeAnswerPart>();
			answer.Citations = new List<KnowledgeCitation>();
			answer.Nearest = citations.Take(NearestShown).ToList();
			answer.ExchangeId = await LogExchangeAsync(actor, question, answer.Nearest, KnowledgeOutcomes.NotCovered, inputTokens, outputTokens, cancellationToken, answer.PromptVersion);
			await RecordGapAsync(actor, question, keptLocal, KnowledgeGapReasons.NotCovered, request.Surface, contextKind, contextItem, answer.Nearest, answer.ExchangeId, cancellationToken);
			return answer;
		}

		private static KnowledgeAnswer Refused(string outcome) => new KnowledgeAnswer { Outcome = outcome, PromptVersion = AiKnowledgePrompt.Version };

		public static string CleanQuestion(string question)
		{
			if (string.IsNullOrWhiteSpace(question))
				return null;
			var clean = new string(question.Where(ch => !char.IsControl(ch) || ch == '\n').ToArray()).Trim();
			return clean.Length == 0 || clean.Length > KnowledgeAskRequest.QuestionMaxLength ? null : clean;
		}

		private static List<KnowledgeCitation> Citations(IEnumerable<KnowledgeScoredChunk> selected, Dictionary<string, string> offlineVersions)
		{
			var list = new List<KnowledgeCitation>();
			var i = 0;
			foreach (var scored in selected ?? Enumerable.Empty<KnowledgeScoredChunk>())
			{
				var chunk = scored.Chunk;
				i++;
				var held = offlineVersions != null && offlineVersions.TryGetValue(chunk.ItemKey, out var version) ? version : null;
				list.Add(new KnowledgeCitation
				{
					Ref = "S" + i.ToString(CultureInfo.InvariantCulture),
					SourceKind = chunk.SourceKind,
					ItemId = chunk.ItemId,
					VersionId = chunk.VersionId,
					VersionLabel = chunk.VersionLabel,
					Anchor = chunk.Anchor,
					HeadingPath = chunk.HeadingPath,
					Title = chunk.Title,
					EffectiveOn = chunk.EffectiveOn,
					Excerpt = Excerpt(chunk.Text),
					OfflineCopyStale = held != null && !string.Equals(held, chunk.VersionId, StringComparison.Ordinal)
				});
			}
			return list;
		}

		private static string Excerpt(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return string.Empty;
			return text.Length <= 240 ? text : text.Substring(0, 240).TrimEnd() + "…";
		}

		/// <summary>First use (no index state) or a department whose index went inactive: queue a rebuild for worker 89.</summary>
		private async Task EnsureIndexAsync(int departmentId, CancellationToken cancellationToken)
		{
			try
			{
				var state = await _indexRepository.GetStateAsync(departmentId, cancellationToken);
				if (state == null || !state.Active)
					await _index.RequestRebuildAsync(departmentId, null, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The knowledge index of department {departmentId} could not be activated.");
			}
		}

		private sealed record Admission(AiUsageReservation Reservation, string Outcome);

		/// <summary>The shared inference slot and the department's monthly budget (the Enhanced AI admission), waited for briefly.</summary>
		private async Task<Admission> ReserveAsync(int departmentId, CancellationToken cancellationToken)
		{
			var tier = AiOperatorSettings.IsListedDepartment(AiConfig.SelfHostedDepartmentIds, departmentId) ? AdminAssistAskTiers.SelfHosted : AdminAssistAskTiers.EnhancedAi;
			var giveUp = _clock.GetUtcNow().UtcDateTime.AddSeconds(MaxAdmissionWaitSeconds);
			while (true)
			{
				var now = _clock.GetUtcNow().UtcDateTime;
				if (await _usage.RemainingAsync(departmentId, now, AiConfig.MonthlyTokenLimit, cancellationToken) < ReservedTokens)
					return new Admission(null, KnowledgeOutcomes.BudgetExhausted);
				var reservation = await _admission.ReserveBackgroundAsync(departmentId, Feature, tier, now, ReservedTokens, AiConfig.MonthlyTokenLimit, cancellationToken);
				if (reservation != null)
					return new Admission(reservation, null);
				if (now >= giveUp)
					return new Admission(null, KnowledgeOutcomes.Busy);
				await Task.Delay(TimeSpan.FromMilliseconds(500), _clock, cancellationToken);
			}
		}

		private readonly record struct CallScope(KnowledgeReaderScope Scope, IReadOnlyList<KnowledgeChunk> Chunks, string ProtocolId);

		private readonly record struct CallWideScope(KnowledgeReaderScope Scope, IReadOnlyList<KnowledgeChunk> Chunks, KnowledgeCallContext Context, IReadOnlyList<string> ProtocolIds);

		private IKnowledgeVersionContentSource VersionSourceFor(int kind) =>
			Enum.IsDefined(typeof(KnowledgeSourceKinds), kind) ? _versionSources.FirstOrDefault(s => s.VersionKinds != null && s.VersionKinds.Contains((KnowledgeSourceKinds)kind)) : null;

		/// <summary>The reader's membership and facts without any readable item (a scope read through a call or a version source).</summary>
		private static KnowledgeReaderScope CopyScope(KnowledgeReaderScope scope) => new KnowledgeReaderScope
		{
			DepartmentId = scope.DepartmentId,
			UserId = scope.UserId,
			IsCurrentMember = scope.IsCurrentMember,
			IsDepartmentAdmin = scope.IsDepartmentAdmin,
			Facts = scope.Facts
		};

		/// <summary>
		/// K3 "Ask about this call": every protocol version the call cites (removed rows and rows without a version are skipped; a protocol
		/// excluded from AI or licensed adds no chunks), read through the call like the per-protocol ask, and the call's operational fields
		/// (<see cref="KnowledgeCallContext"/>, an allow-list). A call citing no version leaves the chunks null: the reader's own corpus is
		/// searched, with the call's fields. Null when the reader cannot view the call.
		/// </summary>
		private async Task<CallWideScope?> CallWideScopeAsync(KnowledgeReaderScope scope, int callId, CancellationToken cancellationToken)
		{
			if (callId <= 0 || !await _authorization.CanUserViewCallAsync(scope.UserId, callId))
				return null;
			var call = await _calls.Value.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != scope.DepartmentId || call.IsDeleted)
				return null;

			KnowledgeCallContext context = null;
			try
			{
				context = _callContext == null ? null : await _callContext.BuildAsync(call, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, $"The operational context of call {callId} could not be read.");
			}

			var rows = (await _calls.Value.GetCallProtocolsByCallIdAsync(callId) ?? new List<CallProtocol>())
				.Where(r => r != null && r.CallId == callId && !r.IsRemoved && r.ProtocolVersionId.HasValue)
				.GroupBy(r => r.ProtocolVersionId.Value).Select(g => g.OrderBy(r => r.CallProtocolId).First()).OrderBy(r => r.CallProtocolId).ToList();
			if (rows.Count == 0)
				return new CallWideScope(scope, null, context, new List<string>());

			var headers = await _indexRepository.GetProtocolHeadersAsync(scope.DepartmentId, cancellationToken) ?? new List<DispatchProtocol>();
			var versions = await _versions.GetByIdsAsync(scope.DepartmentId, rows.Select(r => r.ProtocolVersionId.Value).ToList(), true) ?? new List<ProtocolVersion>();
			var callScope = CopyScope(scope);
			var chunks = new List<KnowledgeChunk>();
			var protocolIds = new List<string>();
			foreach (var row in rows)
			{
				var protocol = headers.FirstOrDefault(p => p.DispatchProtocolId == row.DispatchProtocolId && p.DepartmentId == scope.DepartmentId);
				var version = versions.FirstOrDefault(v => v.ProtocolVersionId == row.ProtocolVersionId.Value && v.DispatchProtocolId == row.DispatchProtocolId && v.DepartmentId == scope.DepartmentId);
				if (protocol == null || version == null)
					continue;
				var protocolId = protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture);
				callScope.Allow(KnowledgeSourceKinds.Protocol, new[] { protocolId });
				if (!protocolIds.Contains(protocolId))
					protocolIds.Add(protocolId);
				if (protocol.ExcludeFromAi || protocol.IsLicensedContent)
					continue;
				chunks.AddRange(await CitedVersionChunksAsync(scope.DepartmentId, protocol, version, chunks.Count, cancellationToken));
			}

			return new CallWideScope(callScope, chunks, context, protocolIds);
		}

		/// <summary>A cited version's chunks, unstored, numbered from <paramref name="firstOrdinal"/>; stored vectors are reused where the text is indexed.</summary>
		private async Task<List<KnowledgeChunk>> CitedVersionChunksAsync(int departmentId, DispatchProtocol protocol, ProtocolVersion version, int firstOrdinal, CancellationToken cancellationToken)
		{
			var protocolId = protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture);
			var chunks = ProtocolKnowledgeSource.BuildVersionChunks(departmentId, protocol, version, null);
			var indexed = (await _indexRepository.GetItemChunksAsync(departmentId, (int)KnowledgeSourceKinds.Protocol, protocolId, cancellationToken) ?? new List<KnowledgeChunk>())
				.Where(c => c.Embedding != null).GroupBy(c => c.TextHash, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
			var ordinal = firstOrdinal;
			foreach (var chunk in chunks)
			{
				chunk.Ordinal = ordinal++;
				chunk.AccessFlags = (int)KnowledgeAccessFlags.None;
				chunk.AudienceJson = null;
				chunk.TextHash = KnowledgeChunker.Hash(KnowledgeChunker.EmbeddingInput(chunk));
				chunk.KnowledgeChunkId = -chunk.Ordinal - 1;
				if (indexed.TryGetValue(chunk.TextHash, out var stored))
				{
					chunk.Embedding = stored.Embedding;
					chunk.EmbeddingModel = stored.EmbeddingModel;
				}
			}
			return chunks;
		}

		/// <summary>
		/// The cited version of a call's protocol, readable through the call (P1: a call shows the version it cites, older or not). Null
		/// when the reader cannot view the call or the row does not cite a version of the department. A protocol excluded from AI or
		/// licensed yields no chunks.
		/// </summary>
		private async Task<CallScope?> CallScopeAsync(KnowledgeReaderScope scope, int? callId, int? callProtocolId, CancellationToken cancellationToken)
		{
			if (!callId.HasValue || !callProtocolId.HasValue || callId.Value <= 0 || callProtocolId.Value <= 0)
				return null;
			if (!await _authorization.CanUserViewCallAsync(scope.UserId, callId.Value))
				return null;

			var call = await _calls.Value.GetCallByIdAsync(callId.Value);
			if (call == null || call.DepartmentId != scope.DepartmentId)
				return null;
			var row = (await _calls.Value.GetCallProtocolsByCallIdAsync(callId.Value) ?? new List<CallProtocol>())
				.FirstOrDefault(r => r.CallProtocolId == callProtocolId.Value && r.CallId == callId.Value && !r.IsRemoved && r.ProtocolVersionId.HasValue);
			if (row == null)
				return null;

			var protocol = (await _indexRepository.GetProtocolHeadersAsync(scope.DepartmentId, cancellationToken) ?? new List<DispatchProtocol>())
				.FirstOrDefault(p => p.DispatchProtocolId == row.DispatchProtocolId && p.DepartmentId == scope.DepartmentId);
			var version = (await _versions.GetByIdsAsync(scope.DepartmentId, new[] { row.ProtocolVersionId.Value }, true) ?? new List<ProtocolVersion>())
				.FirstOrDefault(v => v.ProtocolVersionId == row.ProtocolVersionId.Value && v.DispatchProtocolId == row.DispatchProtocolId && v.DepartmentId == scope.DepartmentId);
			if (protocol == null || version == null)
				return null;

			var protocolId = protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture);
			var callScope = new KnowledgeReaderScope
			{
				DepartmentId = scope.DepartmentId,
				UserId = scope.UserId,
				IsCurrentMember = scope.IsCurrentMember,
				IsDepartmentAdmin = scope.IsDepartmentAdmin,
				Facts = scope.Facts
			};
			callScope.Allow(KnowledgeSourceKinds.Protocol, new[] { protocolId });
			if (protocol.ExcludeFromAi || protocol.IsLicensedContent)
				return new CallScope(callScope, new List<KnowledgeChunk>(), protocolId);

			// Read through the call: no audience on these chunks. The stored vectors are reused where the cited version's text is indexed.
			var chunks = ProtocolKnowledgeSource.BuildVersionChunks(scope.DepartmentId, protocol, version, null);
			var indexed = (await _indexRepository.GetItemChunksAsync(scope.DepartmentId, (int)KnowledgeSourceKinds.Protocol, protocolId, cancellationToken) ?? new List<KnowledgeChunk>())
				.Where(c => c.Embedding != null).GroupBy(c => c.TextHash, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
			var ordinal = 0;
			foreach (var chunk in chunks)
			{
				chunk.Ordinal = ordinal++;
				chunk.AccessFlags = (int)KnowledgeAccessFlags.None;
				chunk.AudienceJson = null;
				chunk.TextHash = KnowledgeChunker.Hash(KnowledgeChunker.EmbeddingInput(chunk));
				chunk.KnowledgeChunkId = -chunk.Ordinal - 1;
				if (indexed.TryGetValue(chunk.TextHash, out var stored))
				{
					chunk.Embedding = stored.Embedding;
					chunk.EmbeddingModel = stored.EmbeddingModel;
				}
			}

			return new CallScope(callScope, chunks, protocolId);
		}

		#endregion Ask

		#region Audit, feedback and gaps

		/// <summary>
		/// One AiGenerations row per exchange (its own single-turn conversation): the question and the cited source ids, encrypted by the
		/// conversation protection (and enveloped under Advanced Data Protection), with the prompt version, model revision and digest,
		/// tokens and outcome. A failure to log is reported and returns null; it never turns an answer into an error.
		/// </summary>
		private async Task<string> LogExchangeAsync(AdminAssistActor actor, string question, IEnumerable<KnowledgeCitation> sources, string outcome,
			int inputTokens, int outputTokens, CancellationToken cancellationToken, string promptVersion = null)
		{
			try
			{
				var id = Guid.NewGuid().ToString("D");
				var evidence = (sources ?? Enumerable.Empty<KnowledgeCitation>())
					.Select(c => JsonSerializer.Serialize(new StoredSource(c.SourceKind, c.ItemId, c.VersionId, c.VersionLabel, c.Anchor, c.Title))).ToArray();
				var row = new AiGenerationRow
				{
					Id = id,
					ConversationId = id,
					DepartmentId = actor.DepartmentId,
					UserId = actor.UserId,
					Revision = 1,
					CreatedOnUtc = _clock.GetUtcNow().UtcDateTime,
					Content = JsonSerializer.Serialize(new AskStoredContent(question, Array.Empty<AskToolInput>(), evidence)),
					PromptVersion = string.IsNullOrWhiteSpace(promptVersion) ? AiKnowledgePrompt.Version : promptVersion,
					ModelRevision = AiConfig.ModelRevision ?? string.Empty,
					RuntimeDigest = AiConfig.RuntimeDigest ?? string.Empty,
					RequestDigest = RequestDigest(actor, question),
					InputTokens = Math.Max(0, inputTokens),
					OutputTokens = Math.Max(0, outputTokens),
					Outcome = outcome
				};
				await _conversationProtection.ProtectAsync(actor, row, cancellationToken);
				await _gapsRepository.InsertGenerationAsync(row, cancellationToken);
				return id;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"A knowledge exchange could not be logged for department {actor.DepartmentId}.");
				return null;
			}
		}

		/// <summary>A source as kept in the logged exchange (and read back for a thumbs-down).</summary>
		private sealed record StoredSource(int SourceKind, string ItemId, string VersionId, string VersionLabel, string Anchor, string Title);

		private static string RequestDigest(AdminAssistActor actor, string question)
		{
			var payload = Encoding.UTF8.GetBytes(actor.DepartmentId.ToString(CultureInfo.InvariantCulture) + ":" + actor.UserId + ":" + question);
			try
			{
				if (!string.IsNullOrWhiteSpace(AiConfig.AuditHmacKey))
					return Convert.ToHexString(HMACSHA256.HashData(Convert.FromBase64String(AiConfig.AuditHmacKey), payload)).ToLowerInvariant();
			}
			catch (FormatException)
			{
				// A mistyped key falls back to a plain digest rather than losing the audit row.
			}
			return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
		}

		private async Task RecordGapAsync(AdminAssistActor actor, string question, bool keptLocal, KnowledgeGapReasons reason, int surface, int? contextKind, string contextItem,
			IEnumerable<KnowledgeCitation> nearest, string exchangeId, CancellationToken cancellationToken)
		{
			try
			{
				var roles = ((await _roles.GetRolesForUserAsync(actor.UserId, actor.DepartmentId)) ?? new List<PersonnelRole>())
					.Where(r => r != null && r.DepartmentId == actor.DepartmentId).Select(r => r.Name).Where(n => !string.IsNullOrWhiteSpace(n)).OrderBy(n => n, StringComparer.CurrentCulture);
				await _gaps.RecordAsync(new KnowledgeGap
				{
					DepartmentId = actor.DepartmentId,
					UserId = actor.UserId,
					AskerRole = Truncate(string.Join(", ", roles), KnowledgeGap.AskerRoleMaxLength),
					// Advanced Data Protection keeps the question local: the gap records the reason and the nearest sources only.
					Question = keptLocal ? null : Truncate(question, KnowledgeGap.QuestionMaxLength),
					Reason = (int)reason,
					Surface = Enum.IsDefined(typeof(KnowledgeAskSurfaces), surface) ? surface : (int)KnowledgeAskSurfaces.Api,
					Status = (int)KnowledgeGapStatuses.Open,
					AskedOn = _clock.GetUtcNow().UtcDateTime,
					NearestSourcesJson = NearestJson(nearest),
					ContextKind = contextKind.HasValue && Enum.IsDefined(typeof(KnowledgeSourceKinds), contextKind.Value) ? contextKind : null,
					ContextItemId = Truncate(contextItem, KnowledgeChunk.ItemIdMaxLength),
					AiGenerationId = exchangeId
				}, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"A knowledge gap could not be stored for department {actor.DepartmentId}.");
			}
		}

		public static string NearestJson(IEnumerable<KnowledgeCitation> nearest)
		{
			var list = (nearest ?? Enumerable.Empty<KnowledgeCitation>()).Select(c => new KnowledgeGapSource
			{
				SourceKind = c.SourceKind,
				ItemId = c.ItemId,
				VersionId = c.VersionId,
				VersionLabel = c.VersionLabel,
				Anchor = c.Anchor,
				Title = Truncate(c.Title, 120)
			}).ToList();
			while (list.Count > 0)
			{
				var json = Newtonsoft.Json.JsonConvert.SerializeObject(list);
				if (json.Length <= KnowledgeGap.NearestSourcesMaxLength)
					return json;
				list.RemoveAt(list.Count - 1);
			}
			return null;
		}

		private static string Truncate(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);

		public async Task<bool> FeedbackAsync(KnowledgeReader reader, string exchangeId, bool helpful, int surface, CancellationToken cancellationToken = default)
		{
			if (reader == null || reader.DepartmentId <= 0 || string.IsNullOrWhiteSpace(reader.UserId) || !Guid.TryParseExact(exchangeId, "D", out _))
				return false;
			if (!await IsEntitledAsync(reader.DepartmentId))
				return false;

			// Only the asker's own exchange; a thumbs-up needs nothing stored.
			var row = await _gapsRepository.GetGenerationAsync(reader.DepartmentId, reader.UserId, exchangeId, cancellationToken);
			if (row == null || !AiKnowledgePrompt.AnswerVersions.Contains(row.PromptVersion, StringComparer.Ordinal))
				return false;
			if (helpful)
				return true;
			if (await _gapsRepository.GetByExchangeAsync(reader.DepartmentId, exchangeId, cancellationToken) != null)
				return true;

			var actor = new AdminAssistActor(reader.DepartmentId, reader.UserId, reader.Locale ?? "en");
			AskStoredContent stored = null;
			try
			{
				stored = await _conversationProtection.ReadAsync(actor, row, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, "A knowledge exchange could not be read back for feedback.");
			}

			var nearest = (stored?.EvidenceIds ?? Array.Empty<string>()).Select(ReadStoredSource).Where(s => s != null).ToList();
			await RecordGapAsync(actor, stored?.Question, await IsProtectedAsync(reader.DepartmentId) || stored == null, KnowledgeGapReasons.ThumbsDown, surface, null, null,
				nearest, exchangeId, cancellationToken);
			return true;
		}

		private static KnowledgeCitation ReadStoredSource(string json)
		{
			try
			{
				var source = JsonSerializer.Deserialize<StoredSource>(json);
				return source == null ? null : new KnowledgeCitation
				{
					SourceKind = source.SourceKind,
					ItemId = source.ItemId,
					VersionId = source.VersionId,
					VersionLabel = source.VersionLabel,
					Anchor = source.Anchor,
					Title = source.Title
				};
			}
			catch (JsonException)
			{
				return null;
			}
		}

		#endregion Audit, feedback and gaps

		#region Settings

		public Task<KnowledgeSettings> GetSettingsAsync(int departmentId) => KnowledgeSettingsReader.ReadAsync(_settingsRepository, departmentId);

		public async Task<KnowledgeSettings> SaveSettingsAsync(int departmentId, KnowledgeSettings settings, string userId, CancellationToken cancellationToken = default)
		{
			var before = await GetSettingsAsync(departmentId);
			var normalized = (settings ?? new KnowledgeSettings()).Normalize();
			await _settingsService.SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(normalized), DepartmentSettingTypes.KnowledgeSettings, cancellationToken);
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.KnowledgeSettingsChanged,
				Before = Newtonsoft.Json.JsonConvert.SerializeObject(before),
				After = Newtonsoft.Json.JsonConvert.SerializeObject(normalized),
				Successful = true,
				ServerName = Environment.MachineName
			});

			// Documents joining or leaving the corpus changes the whole department's index.
			if (before.IncludeDocuments != normalized.IncludeDocuments)
				await _index.RequestRebuildAsync(departmentId, null, cancellationToken);
			return normalized;
		}

		#endregion Settings
	}
}
