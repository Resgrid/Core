using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;

namespace Resgrid.Services.AiDispatch
{
	/// <summary>
	/// AI dispatch, Enrich mode (ai-dispatch-template-plan.md §3.2; enhanced-ai-addon-plan.md §4). The call was created by
	/// GenericTemplate and dispatched before this runs. Enrichment only fills fields that are still empty, with values that appear
	/// verbatim in the message, and adds a clearly labelled AI note; priority and duplicates are suggestions in that note. It never
	/// dispatches, re-pages, merges, closes or deletes. Every failure leaves the call exactly as dispatched. With a decision model
	/// configured (Clef / Clef-Flash, AiDispatchDecisions) it classifies dispatch-or-not, call type, priority and duplicate with
	/// per-field probabilities, and the generative model keeps extraction, title and summary; each side degrades to the other.
	/// </summary>
	public sealed class AiDispatchEnrichmentService : IAiDispatchEnrichmentService
	{
		/// <summary>The subject EmailController substitutes when a dispatch email has none; only this placeholder name is ever replaced.</summary>
		public const string PlaceholderCallName = "Dispatch Email";
		/// <summary>AiUsageLedger feature name for AI dispatch rows.</summary>
		public const string Feature = "AiDispatch";

		private readonly IEnhancedAiAccessService _enhancedAi;
		private readonly IFeatureToggleService _flags;
		private readonly IDepartmentsService _departments;
		private readonly ICallsService _calls;
		private readonly IGeoLocationProvider _geo;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IAiDispatchAuditRepository _audits;
		private readonly IAiDispatchConfigRepository _settings;
		private readonly IAiBackgroundAdmission _admission;
		private readonly IAiUsageMeter _usage;
		private readonly Lazy<ILlmClient> _client;
		private readonly Lazy<IDecisionClient> _decisions;
		private readonly IEventAggregator _events;
		private readonly TimeProvider _clock;
		private readonly IDispatcherDeskConfigService _deskConfig;
		private readonly Lazy<IIntakeConversationOperations> _conversations;
		private readonly Lazy<IIntakeScreeningService> _screening;

		/// <param name="conversations">Intake SMS conversations (plan §8): the transcript a conversation refresh reads. Lazy: the conversation
		/// service queues this enrichment.</param>
		/// <param name="screening">Intake spam screening (plan §9, M0326): a confident "not a dispatch" reading labels the intake call
		/// LikelyNotIncident, which the dispatcher desk does not offer. Lazy for the same reason.</param>
		public AiDispatchEnrichmentService(IEnhancedAiAccessService enhancedAi, IFeatureToggleService flags, IDepartmentsService departments, ICallsService calls,
			IGeoLocationProvider geo, IDepartmentDataProtectionService protection, IAiDispatchAuditRepository audits, IAiDispatchConfigRepository settings,
			IAiBackgroundAdmission admission, IAiUsageMeter usage, Lazy<ILlmClient> client, Lazy<IDecisionClient> decisions, IEventAggregator events, TimeProvider clock,
			IDispatcherDeskConfigService deskConfig, Lazy<IIntakeConversationOperations> conversations = null, Lazy<IIntakeScreeningService> screening = null)
		{
			_deskConfig = deskConfig;
			_conversations = conversations;
			_screening = screening;
			_enhancedAi = enhancedAi;
			_flags = flags;
			_departments = departments;
			_calls = calls;
			_geo = geo;
			_protection = protection;
			_audits = audits;
			_settings = settings;
			_admission = admission;
			_usage = usage;
			_client = client;
			_decisions = decisions;
			_events = events;
			_clock = clock;
		}

		public async Task<string> EnrichAsync(AiDispatchQueueItem item, CancellationToken cancellationToken)
		{
			if (item == null || item.DepartmentId <= 0 || item.CallId <= 0 || !AiDispatchConfig.EnrichEnabled)
				return "Disabled";
			// A conversation's AI turn is not an enrichment; the queue consumer hands it to the conversation service.
			if (item.Channel == AiDispatchChannels.IntakeConversation)
				return "NotEnrichment";
			var departmentId = item.DepartmentId;
			var selfHosted = AiOperatorSettings.IsListedDepartment(AiConfig.SelfHostedDepartmentIds, departmentId);
			// Intake calls (pending, from an intake channel) are gated by Ai.Intake / Ai.VoiceIntake, not by the department's email format.
			var intake = AiDispatchChannels.IsIntake(item.Channel);
			if (intake ? !await IsIntakeAvailableAsync(departmentId, item.Channel) : !await IsAvailableAsync(departmentId) || !await UsesAiFormatAsync(departmentId, item.Channel))
				return "NotEntitled";
			if (!OperatorConfigured() && !DecisionConfigured())
				return "Unconfigured";

			// An intake SMS conversation re-enriches its pending call from the whole transcript after each Enhanced AI turn (plan §8 D16).
			// The call's one audit row (unique per call) belongs to its first enrichment; the conversation turn is the refresh's claim.
			var refresh = AiDispatchChannels.IsConversationRefresh(item);

			// The claim row is written before any work so an at-least-once redelivery can never enrich a call twice.
			var audit = new AiDispatchAuditRow
			{
				AiDispatchAuditId = Guid.NewGuid().ToString("D"), DepartmentId = departmentId, CallId = item.CallId, Mode = "Enrich",
				Outcome = AiDispatchOutcomes.InProgress, PromptVersion = AiDispatchPrompt.Version, ModelName = AiConfig.Model,
				ModelRevision = AiConfig.ModelRevision, RuntimeDigest = AiConfig.RuntimeDigest, CreatedOnUtc = _clock.GetUtcNow().UtcDateTime
			};
			if (!refresh && !await _audits.TryClaimAsync(audit, cancellationToken))
				return "Duplicate";

			var settings = await _settings.GetAsync(departmentId, cancellationToken) ?? new DepartmentAiDispatchConfig { DepartmentId = departmentId };
			try
			{
				audit.Outcome = item.SenderNotAllowed ? AiDispatchOutcomes.SenderNotAllowed : await RunAsync(item, audit, settings, selfHosted, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				audit.Outcome = AiDispatchOutcomes.Unavailable;
				throw;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"AI dispatch enrichment failed for call {item.CallId}.");
				audit.Outcome = AiDispatchOutcomes.Unavailable;
			}
			finally
			{
				audit.CompletedOnUtc = _clock.GetUtcNow().UtcDateTime;
				if (!refresh)
					await _audits.CompleteAsync(audit, CancellationToken.None);
				await PruneAsync(departmentId, settings);
			}
			return audit.Outcome;
		}

		public async Task<bool> IsAvailableAsync(int departmentId)
		{
			if (departmentId <= 0 || !AiDispatchConfig.EnrichEnabled)
				return false;
			try
			{
				return AiOperatorSettings.IsListedDepartment(AiConfig.SelfHostedDepartmentIds, departmentId)
					? await _enhancedAi.IsEnabledAsync(departmentId) && (await _flags.EvaluateFreshAsync(FeatureFlagKeys.AiDispatchTemplate, departmentId))?.IsEnabled == true
					: await _enhancedAi.CanUseAsync(departmentId, FeatureFlagKeys.AiDispatchTemplate);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}
		}

		/// <summary>
		/// Intake enrichment (call-intake-and-ai-destinations-plan.md §3): the host switches, then the Ai.Intake (voice: Ai.VoiceIntake)
		/// capability, paid or operator-listed self-hosted. ADP departments are still skipped in RunAsync (D9).
		/// </summary>
		public async Task<bool> IsIntakeAvailableAsync(int departmentId, int channel)
		{
			if (departmentId <= 0 || !AiDispatchConfig.EnrichEnabled || !IntakeConfig.Enabled || !AiDispatchChannels.IsIntake(channel))
				return false;
			var flag = AiDispatchChannels.IntakeCapabilityFlag(channel);
			try
			{
				return AiOperatorSettings.IsListedDepartment(AiConfig.SelfHostedDepartmentIds, departmentId)
					? await _enhancedAi.IsEnabledAsync(departmentId) && (await _flags.EvaluateFreshAsync(flag, departmentId))?.IsEnabled == true
					: await _enhancedAi.CanUseAsync(departmentId, flag);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}
		}

		// The department may have switched its import format back since this call was queued. The dispatcher desk (setting 128) also
		// asks for AI without the AI format, department-wide: EnhancedAi email call mode (emailed pending calls) or EnhancedAi text
		// overflow (the pending calls the desk makes from overflowed texts, channel 1, SourceIdentifier "DQ:{itemId}"). Group
		// dispatch addresses (channel 2) are not the dispatch email address the desk setting covers, so they stay format-only.
		private async Task<bool> UsesAiFormatAsync(int departmentId, int channel)
		{
			try
			{
				if ((await _departments.GetDepartmentEmailSettingsAsync(departmentId))?.FormatType == (int)CallEmailTypes.AI)
					return true;
				if (channel == 2)
					return false;
				var desk = await _deskConfig.GetConfigAsync(departmentId);
				return desk != null && (desk.GetEmailCallMode() == DispatcherEmailCallModes.EnhancedAi ||
										desk.GetTextOverflowMode() == DispatcherTextOverflowModes.EnhancedAi);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return false;
			}
		}

		// The department's audit retention: rows older than it go on every write, so no sweep worker is needed.
		private async Task PruneAsync(int departmentId, DepartmentAiDispatchConfig settings)
		{
			try
			{
				var days = Math.Clamp(settings.AuditRetentionDays, AiDispatchSettingsPolicy.MinimumRetentionDays, AiDispatchSettingsPolicy.MaximumRetentionDays);
				await _audits.PruneAsync(departmentId, _clock.GetUtcNow().UtcDateTime.AddDays(-days), CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}
		}

		public static bool OperatorConfigured()
		{
			try
			{
				OperatorEndpointPolicy.ValidateUri(AiConfig.Endpoint, AiConfig.AllowPrivateEndpoint);
			}
			catch (LlmUnavailableException)
			{
				return false;
			}
			return !string.IsNullOrWhiteSpace(AiConfig.ApiKey) && AiOperatorSettings.IsReviewedModel(AiConfig.Model) &&
				AiOperatorSettings.IsPinned(AiConfig.ModelRevision, AiConfig.RuntimeDigest);
		}

		/// <summary>The host opted AI dispatch into the decision model and it is a valid, reviewed, pinned self-hosted endpoint.</summary>
		public static bool DecisionConfigured() => DecisionTarget() != null;

		// Call messages and active calls carry PII and often PHI, so they are scored only by the operator's own model: a third-party
		// host (Workers AI) is never a target here, and the transport refuses department content for it as well.
		private static AiDecisionEndpoint DecisionTarget()
		{
			if (!AiDispatchConfig.DecisionsEnabled)
				return null;
			var target = AiDecisionSettings.Resolve(AiConfig.DecisionProvider, AiConfig.DecisionEndpoint, AiConfig.AllowPrivateEndpoint, AiConfig.DecisionApiKey,
				AiConfig.DecisionModel, AiConfig.DecisionModelRevision, AiConfig.DecisionRuntimeDigest, AiConfig.DecisionGatewayId);
			return target is { Remote: false } ? target : null;
		}

		private async Task<string> RunAsync(AiDispatchQueueItem item, AiDispatchAuditRow audit, DepartmentAiDispatchConfig settings, bool selfHosted, CancellationToken cancellationToken)
		{
			var departmentId = item.DepartmentId;

			// Protected fields may reach inference only through a future ai-inference broker purpose; until then enrolled departments are skipped.
			if (await _protection.ShouldEncryptNewWritesAsync(departmentId))
				return AiDispatchOutcomes.ProtectionUnsupported;

			var call = await _calls.GetCallByIdAsync(item.CallId, true);
			if (!IsEnrichable(call, departmentId))
				return AiDispatchOutcomes.CallNotActive;

			var context = await BuildContextAsync(call, item, cancellationToken);

			var reservation = await ReserveAsync(departmentId, selfHosted, settings.MonthlyTokenCap, cancellationToken);
			if (reservation.Outcome != null)
				return reservation.Outcome;

			var usedTokens = 0;
			var ledgerOutcome = "Unavailable";
			try
			{
				var minimum = AiDispatchSettingsPolicy.EffectiveMinimumConfidence(settings, AiDispatchConfig.MinimumConfidence);
				var decision = await DecideAsync(context, audit, cancellationToken);
				if (decision != null)
					usedTokens = Math.Min(reservation.Reservation.Tokens, decision.InputTokens);

				// A message the decision model is sure is not a dispatch would have nothing filled, so the generative model is not called.
				var generate = OperatorConfigured() && (decision == null ||
					1 - decision.DispatchProbability < Math.Clamp(AiDispatchConfig.SkipGenerationNotDispatchProbability, 0.5, 1.0));
				AiDispatchEnrichment generated = null;
				if (generate)
				{
					var stopwatch = Stopwatch.StartNew();
					LlmResult result = null;
					using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
					{
						deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(AiDispatchConfig.RequestTimeoutSeconds, 5, 300)));
						try
						{
							result = await _client.Value.CompleteAsync(new LlmRequest(AiConfig.Model, AiDispatchPrompt.Messages(context), new[] { AiDispatchPrompt.Tool(context) }, 512,
								AiCacheIsolation.SaltFor(departmentId, AiConfig.SendCacheSalt, AiConfig.CacheSaltKey, AiConfig.AuditHmacKey)), deadline.Token);
						}
						catch (Exception ex) when (ex is LlmUnavailableException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
						{
							// With the decision model's answers the classification still stands; without them nothing does.
							if (decision == null)
								return AiDispatchOutcomes.Unavailable;
						}
					}
					if (result != null)
					{
						audit.LatencyMs = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
						audit.InputTokens = result.InputTokens;
						audit.OutputTokens = result.OutputTokens;
						usedTokens = Math.Min(reservation.Reservation.Tokens, usedTokens + result.InputTokens + result.OutputTokens);

						try
						{
							generated = AiDispatchPrompt.Validate(AiDispatchPrompt.Parse(result), context);
						}
						catch (Exception ex) when (ex is ArgumentException || ex is System.Text.Json.JsonException)
						{
							if (decision == null)
							{
								ledgerOutcome = "InvalidOutput";
								return AiDispatchOutcomes.InvalidOutput;
							}
						}
					}
				}
				else
				{
					// The audit names only models that were actually asked.
					audit.ModelName = audit.ModelRevision = audit.RuntimeDigest = null;
				}

				// Only the decision model is configured and it did not answer.
				if (decision == null && generated == null)
					return AiDispatchOutcomes.Unavailable;

				var enrichment = decision != null ? AiDispatchDecisions.Merge(generated, decision, minimum) : generated;
				ledgerOutcome = "Answered";
				audit.Confidence = (decimal)Math.Round(enrichment.Confidence, 4);
				audit.RejectedCount = enrichment.RejectedCount;
				if (enrichment.Confidence < minimum)
					return AiDispatchOutcomes.LowConfidence;

				return await ApplyAsync(call, enrichment, audit, settings, item, cancellationToken);
			}
			finally
			{
				// Failed inference is not charged: the GPU and its outages are the operator's, not the department's.
				try { await _usage.CompleteAsync(reservation.Reservation, usedTokens, ledgerOutcome, CancellationToken.None); }
				catch (Exception ex) { Framework.Logging.LogException(ex); }
			}
		}

		/// <summary>
		/// The decision model's classification, or null when it is not configured, unavailable, too slow or answered out of bounds;
		/// the generative model's own classification then stands. Its provenance is audited only when it answered.
		/// </summary>
		private async Task<AiDispatchDecision> DecideAsync(AiDispatchContext context, AiDispatchAuditRow audit, CancellationToken cancellationToken)
		{
			var target = DecisionTarget();
			if (target == null)
				return null;
			var stopwatch = Stopwatch.StartNew();
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(AiDispatchConfig.DecisionTimeoutMilliseconds, 250, 60000)));
			try
			{
				var result = await _decisions.Value.DecideAsync(AiDispatchDecisions.Request(target.WireModel, context), deadline.Token);
				var decision = AiDispatchDecisions.Read(result, context);
				audit.DecisionPromptVersion = AiDispatchDecisions.Version;
				audit.DecisionModelName = target.AuditModel;
				audit.DecisionModelRevision = target.Revision;
				audit.DecisionRuntimeDigest = target.Digest;
				audit.DecisionInputTokens = decision.InputTokens;
				audit.DecisionLatencyMs = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
				return decision;
			}
			catch (Exception ex) when (ex is LlmUnavailableException || ex is ArgumentException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
			{
				return null;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				// A resolution or wiring fault must not take the generative path down with it.
				Framework.Logging.LogException(ex, "AI dispatch decision model failed; using the generative model's classification.");
				return null;
			}
		}

		/// <summary>
		/// Active calls, and pending ones held for a dispatcher: intake calls wait pending, and so do the dispatcher desk's emailed
		/// and overflowed-text calls (setting 128). Enrichment never dispatches either way.
		/// </summary>
		public static bool IsEnrichable(Call call, int departmentId) =>
			call != null && call.DepartmentId == departmentId && !call.IsDeleted && (call.State == (int)CallStates.Active || call.State == (int)CallStates.Pending);

		/// <summary>
		/// Trusted description of the message for the model: where it came from and, read from the call as loaded (a dispatcher may
		/// have sent an intake call before the enrichment ran), whether anyone has been dispatched. The system prompt states neither.
		/// </summary>
		public static string IntakeSource(int channel, Call call)
		{
			// Intake lines are public (M0326): the reading that the message reports no incident labels the call for the dispatcher.
			var state = (call?.State == (int)CallStates.Pending
				? " The call is pending: a dispatcher reviews it and nobody has been dispatched yet."
				: " A dispatcher has dispatched the call.") + IntakeScreeningHint;
			switch (channel)
			{
				case AiDispatchChannels.IntakeVoice:
					return "answers a phone caller gave to the department's automated intake line (speech recognition, so words may be misheard). " +
						"Lines are labelled Nature, Location, Caller name and Callback number." + state;
				case AiDispatchChannels.IntakeSms:
					return "a text message sent to the department's intake number." + state;
				case AiDispatchChannels.IntakeConversation:
					return "the text messages a member of the public sent in an SMS conversation with the department's dispatch number, oldest first, " +
						"one per line (their answers to the department's follow-up questions)." + state;
				case AiDispatchChannels.IntakeEmail:
					return "an email sent to the department's intake address." + state;
				case AiDispatchChannels.IntakeChat:
					return "a chat message posted in a channel the department linked for intake." + state;
				case AiDispatchChannels.IntakeWebhook:
					return "a request another system posted to the department's intake webhook." + state;
				case AiDispatchChannels.PostmarkEmail:
				case AiDispatchChannels.GroupDispatchEmail:
				case AiDispatchChannels.ImapImport:
					return "a dispatch email (CAD page or text) the department received; the call was created from it and dispatched automatically.";
				default:
					return null;
			}
		}

		private async Task<AiDispatchContext> BuildContextAsync(Call call, AiDispatchQueueItem item, CancellationToken cancellationToken)
		{
			var departmentId = call.DepartmentId;
			var channel = item.Channel;
			var maxCharacters = Math.Clamp(AiDispatchConfig.MaxMessageCharacters, 200, 12000);
			string transcript = null;
			if (AiDispatchChannels.IsConversationRefresh(item) && _conversations != null)
			{
				try
				{
					transcript = await _conversations.Value.GetParticipantTranscriptAsync(departmentId, item.IntakeConversationId, maxCharacters);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					Framework.Logging.LogException(ex, $"AI dispatch could not read the conversation transcript for call {call.CallId}; using the call's text.");
				}
			}
			var types = (await _calls.GetCallTypesForDepartmentAsync(departmentId) ?? new List<CallType>())
				.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Type)).Take(60).Select(t => new AiDispatchChoice(t.CallTypeId, t.Type.Trim())).ToList();
			var priorities = (await _calls.GetActiveCallPrioritiesForDepartmentAsync(departmentId) ?? new List<DepartmentCallPriority>())
				.Where(p => p != null && !p.IsDeleted && !string.IsNullOrWhiteSpace(p.Name)).OrderBy(p => p.Sort).Take(20)
				.Select(p => new AiDispatchChoice(p.DepartmentCallPriorityId, p.Name.Trim())).ToList();
			var now = _clock.GetUtcNow().UtcDateTime;
			var candidates = (await _calls.GetActiveCallsByDepartmentAsync(departmentId) ?? new List<Call>())
				.Where(c => c != null && c.CallId != call.CallId && !c.IsDeleted && c.State == (int)CallStates.Active)
				.OrderByDescending(c => c.LoggedOn).Take(Math.Clamp(AiDispatchConfig.MaxCandidateCalls, 0, 20))
				.Select(c => new AiDispatchCandidateCall(c.CallId, c.Number, Truncate(c.Name, 80), Truncate(c.Address, 120), (int)Math.Max(0, (now - c.LoggedOn).TotalMinutes)))
				.ToList();
			// A conversation refresh reads what the texter said across the whole thread; the call's own name and nature otherwise.
			return transcript != null
				? new AiDispatchContext(AiDispatchPrompt.MessageText(null, transcript, maxCharacters), types, priorities, candidates) { Source = IntakeSource(AiDispatchChannels.IntakeConversation, call) }
				: new AiDispatchContext(AiDispatchPrompt.MessageText(call.Name, call.NatureOfCall, maxCharacters), types, priorities, candidates) { Source = IntakeSource(channel, call) };
		}

		private sealed record Admission(AiUsageReservation Reservation, string Outcome);

		/// <summary>Background priority: waits for a moment when no interactive turn is live, up to the configured bound.</summary>
		private async Task<Admission> ReserveAsync(int departmentId, bool selfHosted, int? dispatchCap, CancellationToken cancellationToken)
		{
			var tokens = Math.Clamp(AiDispatchConfig.ReservedTokens, 1024, 32768);
			var tier = selfHosted ? AdminAssistAskTiers.SelfHosted : AdminAssistAskTiers.EnhancedAi;
			var giveUp = _clock.GetUtcNow().UtcDateTime.AddSeconds(Math.Clamp(AiDispatchConfig.AdmissionWaitSeconds, 0, 900));
			while (true)
			{
				var now = _clock.GetUtcNow().UtcDateTime;
				if (await _usage.RemainingAsync(departmentId, now, AiConfig.MonthlyTokenLimit, cancellationToken) < tokens)
					return new Admission(null, AiDispatchOutcomes.BudgetExhausted);
				if (dispatchCap.HasValue && await _admission.GetFeatureUsageAsync(departmentId, Feature, now, cancellationToken) + tokens > dispatchCap.Value)
					return new Admission(null, AiDispatchOutcomes.DispatchCapReached);
				var reservation = await _admission.ReserveBackgroundAsync(departmentId, Feature, tier, now, tokens, AiConfig.MonthlyTokenLimit, cancellationToken, dispatchCap);
				if (reservation != null)
					return new Admission(reservation, null);
				if (now >= giveUp)
					return new Admission(null, AiDispatchOutcomes.Busy);
				await Task.Delay(TimeSpan.FromSeconds(3), _clock, cancellationToken);
			}
		}

		private async Task<string> ApplyAsync(Call original, AiDispatchEnrichment enrichment, AiDispatchAuditRow audit, DepartmentAiDispatchConfig settings,
			AiDispatchQueueItem item, CancellationToken cancellationToken)
		{
			// Re-read so a dispatcher's edit made while the model was thinking always wins.
			var call = await _calls.GetCallByIdAsync(original.CallId, true);
			if (!IsEnrichable(call, original.DepartmentId))
				return AiDispatchOutcomes.CallNotActive;

			// Intake: the "{channel}: {sender}" fallback name is the placeholder, unless a dispatcher renamed the call meanwhile. Only the
			// fingerprint taken at enqueue proves the name is still the placeholder: comparing with the name the worker first read would
			// miss a rename made before that read. No fingerprint (no usable AiConfig.AuditHmacKey, or an item queued before it existed)
			// means the name is left alone.
			var placeholder = AiDispatchChannels.IsIntake(item.Channel)
				? item.NameIsPlaceholder && !string.IsNullOrEmpty(item.PlaceholderNameFingerprint) &&
				  string.Equals(PlaceholderFingerprint(call.DepartmentId, call.CallId, call.Name), item.PlaceholderNameFingerprint, StringComparison.Ordinal)
				: string.Equals(call.Name?.Trim(), PlaceholderCallName, StringComparison.Ordinal);

			var applied = new List<string>();
			if (enrichment.IsDispatch)
			{
				if (settings.FillCallType && string.IsNullOrWhiteSpace(call.Type) && enrichment.CallType != null) { call.Type = enrichment.CallType.Name; applied.Add(nameof(Call.Type)); }
				if (settings.FillAddress && string.IsNullOrWhiteSpace(call.Address) && enrichment.Address != null)
				{
					call.Address = enrichment.Address;
					applied.Add(nameof(Call.Address));
					if (string.IsNullOrWhiteSpace(call.GeoLocationData) || call.GeoLocationData.Length <= 1)
					{
						var location = await _geo.GetLatLonFromAddress(enrichment.Address);
						if (!string.IsNullOrWhiteSpace(location) && location.Length > 1) { call.GeoLocationData = location; applied.Add(nameof(Call.GeoLocationData)); }
					}
				}
				if (settings.FillContact && string.IsNullOrWhiteSpace(call.ContactName) && enrichment.ContactName != null) { call.ContactName = enrichment.ContactName; applied.Add(nameof(Call.ContactName)); }
				if (settings.FillContact && string.IsNullOrWhiteSpace(call.ContactNumber) && enrichment.ContactNumber != null) { call.ContactNumber = enrichment.ContactNumber; applied.Add(nameof(Call.ContactNumber)); }
				if (settings.FillIncidentNumber && string.IsNullOrWhiteSpace(call.IncidentNumber) && enrichment.IncidentNumber != null) { call.IncidentNumber = enrichment.IncidentNumber; applied.Add(nameof(Call.IncidentNumber)); }
				if (settings.RenamePlaceholder && placeholder && enrichment.Title != null) { call.Name = enrichment.Title; applied.Add(nameof(Call.Name)); }
			}

			var saved = applied.Count > 0;
			if (saved)
			{
				call = await _calls.SaveCallAsync(call, cancellationToken);
				// Recorded as soon as the fields are committed, so a failed note below cannot leave the audit without them.
				audit.AppliedFields = string.Join(",", applied);
			}

			// Intake (M0326): the reading labels the call either way, so a later turn that finds a real request clears the label. Only a
			// confident reading gets here (LowConfidence returned earlier). Written before the note, so a note that fails (and ends the
			// enrichment when no field was saved) cannot lose it; a label that cannot be written never fails the enrichment.
			var screened = AiDispatchChannels.IsIntake(item.Channel) && await ScreenAsync(call, !enrichment.IsDispatch);

			try
			{
				// A conversation refresh after each AI turn only fills fields; its AI note (summary, suggested priority, related calls) waits for the
				// conversation's last turn so the call is not flooded with one note per text.
				var note = AiDispatchChannels.IsConversationRefresh(item) && !item.ConversationComplete ? null : BuildNote(call, enrichment, settings);
				var managingUserId = (await _departments.GetDepartmentByIdAsync(call.DepartmentId, false))?.ManagingUserId;
				var noted = false;
				if (note != null && !string.IsNullOrWhiteSpace(managingUserId))
				{
					await _calls.SaveCallNoteAsync(Note(call.CallId, managingUserId, note), cancellationToken);
					applied.Add("Note");
					noted = true;
				}
				if (settings.FlagRelatedCalls && enrichment.RelatedCall != null && enrichment.IsDispatch && noted)
				{
					audit.RelatedCallId = enrichment.RelatedCall.CallId;
					var related = await _calls.GetCallByIdAsync(enrichment.RelatedCall.CallId, true);
					if (IsEnrichable(related, call.DepartmentId))
						await _calls.SaveCallNoteAsync(Note(related.CallId, managingUserId, $"AI suggestion (Enhanced AI): call {call.Number} may be about this same incident. Please review."), cancellationToken);
				}
			}
			// Once fields (or the intake label) are saved, a failed note must not hide them from the audit or keep boards from refreshing.
			catch (Exception ex) when ((saved || screened) && ex is not OperationCanceledException)
			{
				Framework.Logging.LogException(ex, $"AI dispatch note failed for call {call.CallId}; the applied fields were saved.");
			}

			if (screened)
				applied.Add("Screening");

			audit.AppliedFields = applied.Count == 0 ? null : string.Join(",", applied);
			if (applied.Count == 0)
				return AiDispatchOutcomes.NoChange;

			// Boards and apps refresh like any edit; nothing is re-broadcast, so no one is paged again.
			_events.SendMessage(new CallUpdatedEvent { DepartmentId = call.DepartmentId, Call = call });
			return AiDispatchOutcomes.Applied;
		}

		private async Task<bool> ScreenAsync(Call call, bool likelyNotIncident)
		{
			if (_screening == null)
				return false;

			try
			{
				return await _screening.Value.SetAiScreeningAsync(call.DepartmentId, call.CallId, likelyNotIncident, CancellationToken.None);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Framework.Logging.LogException(ex, $"AI dispatch could not record the intake screening of call {call.CallId}.");
				return false;
			}
		}

		/// <summary>
		/// <see cref="AiDispatchQueueItem.PlaceholderNameFingerprint"/>: HMAC-SHA256 under AiConfig.AuditHmacKey of the department,
		/// call and exact name, so the queue item proves "the name is still what it was" without carrying the sender. Null when there is
		/// no name or no usable key (32+ bytes of base64): the worker then leaves the call's name alone (a dispatcher's rename always wins).
		/// </summary>
		public static string PlaceholderFingerprint(int departmentId, int callId, string name)
		{
			if (name == null || departmentId <= 0 || callId <= 0 || string.IsNullOrWhiteSpace(AiConfig.AuditHmacKey))
				return null;

			byte[] key;
			try
			{
				key = Convert.FromBase64String(AiConfig.AuditHmacKey);
			}
			catch (FormatException)
			{
				return null;
			}
			if (key.Length < 32)
				return null;

			var data = System.Text.Encoding.UTF8.GetBytes("resgrid-intake-placeholder-v1:" + departmentId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
				callId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + name);
			return Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(key, data)).ToLowerInvariant();
		}

		/// <summary>The intake-only isDispatch guidance (the dispatch email paths keep their own wording in the system prompt).</summary>
		public const string IntakeScreeningHint = " Set isDispatch false when the message reports no incident and asks " +
			"nothing of the department (spam, advertising, a wrong number, a test).";

		private CallNote Note(int callId, string userId, string text) => new CallNote
		{
			CallId = callId, UserId = userId, Note = text, Source = (int)CallNoteSources.System, Timestamp = _clock.GetUtcNow().UtcDateTime
		};

		/// <summary>The AI note is labelled as a suggestion; priority and possible duplicates are only ever suggested here.</summary>
		internal static string BuildNote(Call call, AiDispatchEnrichment enrichment, DepartmentAiDispatchConfig settings)
		{
			var lines = new List<string>();
			if (settings.FlagRelatedCalls && !enrichment.IsDispatch)
				lines.Add("This message may not be a dispatch (for example an automated reply or a test page). Please review.");
			if (settings.AddSummaryNote && enrichment.Summary != null)
				lines.Add("Summary: " + enrichment.Summary);
			if (settings.AddSummaryNote && enrichment.IsDispatch && enrichment.Priority != null && enrichment.Priority.Id != call.Priority)
				lines.Add("Suggested priority: " + enrichment.Priority.Name + ".");
			if (settings.FlagRelatedCalls && enrichment.IsDispatch && enrichment.RelatedCall != null)
				lines.Add($"May be about the same incident as call {enrichment.RelatedCall.Number} ({enrichment.RelatedCall.Name}). Please review.");
			return lines.Count == 0 ? null : "AI (Enhanced AI, verify before relying on it): " + string.Join(" ", lines);
		}

		private static string Truncate(string value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value.Trim() : value.Substring(0, max).Trim();
	}
}
