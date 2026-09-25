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
	/// dispatches, re-pages, merges, closes or deletes. Every failure leaves the call exactly as dispatched.
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
		private readonly IEventAggregator _events;
		private readonly TimeProvider _clock;

		public AiDispatchEnrichmentService(IEnhancedAiAccessService enhancedAi, IFeatureToggleService flags, IDepartmentsService departments, ICallsService calls,
			IGeoLocationProvider geo, IDepartmentDataProtectionService protection, IAiDispatchAuditRepository audits, IAiDispatchConfigRepository settings,
			IAiBackgroundAdmission admission, IAiUsageMeter usage, Lazy<ILlmClient> client, IEventAggregator events, TimeProvider clock)
		{
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
			_events = events;
			_clock = clock;
		}

		public async Task<string> EnrichAsync(AiDispatchQueueItem item, CancellationToken cancellationToken)
		{
			if (item == null || item.DepartmentId <= 0 || item.CallId <= 0 || !AiDispatchConfig.EnrichEnabled)
				return "Disabled";
			var departmentId = item.DepartmentId;
			var selfHosted = AiOperatorSettings.IsListedDepartment(AiConfig.SelfHostedDepartmentIds, departmentId);
			if (!await IsAvailableAsync(departmentId) || !await UsesAiFormatAsync(departmentId))
				return "NotEntitled";
			if (!OperatorConfigured())
				return "Unconfigured";

			// The claim row is written before any work so an at-least-once redelivery can never enrich a call twice.
			var audit = new AiDispatchAuditRow
			{
				AiDispatchAuditId = Guid.NewGuid().ToString("D"), DepartmentId = departmentId, CallId = item.CallId, Mode = "Enrich",
				Outcome = AiDispatchOutcomes.InProgress, PromptVersion = AiDispatchPrompt.Version, ModelName = AiConfig.Model,
				ModelRevision = AiConfig.ModelRevision, RuntimeDigest = AiConfig.RuntimeDigest, CreatedOnUtc = _clock.GetUtcNow().UtcDateTime
			};
			if (!await _audits.TryClaimAsync(audit, cancellationToken))
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

		// The department may have switched its import format back since this call was queued.
		private async Task<bool> UsesAiFormatAsync(int departmentId)
		{
			try
			{
				return (await _departments.GetDepartmentEmailSettingsAsync(departmentId))?.FormatType == (int)CallEmailTypes.AI;
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

		private async Task<string> RunAsync(AiDispatchQueueItem item, AiDispatchAuditRow audit, DepartmentAiDispatchConfig settings, bool selfHosted, CancellationToken cancellationToken)
		{
			var departmentId = item.DepartmentId;

			// Protected fields may reach inference only through a future ai-inference broker purpose; until then enrolled departments are skipped.
			if (await _protection.ShouldEncryptNewWritesAsync(departmentId))
				return AiDispatchOutcomes.ProtectionUnsupported;

			var call = await _calls.GetCallByIdAsync(item.CallId, true);
			if (!IsEnrichable(call, departmentId))
				return AiDispatchOutcomes.CallNotActive;

			var context = await BuildContextAsync(call, cancellationToken);

			var reservation = await ReserveAsync(departmentId, selfHosted, settings.MonthlyTokenCap, cancellationToken);
			if (reservation.Outcome != null)
				return reservation.Outcome;

			var usedTokens = 0;
			var ledgerOutcome = "Unavailable";
			try
			{
				var stopwatch = Stopwatch.StartNew();
				LlmResult result;
				using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(AiDispatchConfig.RequestTimeoutSeconds, 5, 300)));
					try
					{
						result = await _client.Value.CompleteAsync(new LlmRequest(AiConfig.Model, AiDispatchPrompt.Messages(context), new[] { AiDispatchPrompt.Tool(context) }, 512), deadline.Token);
					}
					catch (Exception ex) when (ex is LlmUnavailableException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
					{
						return AiDispatchOutcomes.Unavailable;
					}
				}
				audit.LatencyMs = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
				audit.InputTokens = result.InputTokens;
				audit.OutputTokens = result.OutputTokens;
				usedTokens = Math.Min(reservation.Reservation.Tokens, result.InputTokens + result.OutputTokens);

				AiDispatchEnrichment enrichment;
				try
				{
					enrichment = AiDispatchPrompt.Validate(AiDispatchPrompt.Parse(result), context);
				}
				catch (Exception ex) when (ex is ArgumentException || ex is System.Text.Json.JsonException)
				{
					ledgerOutcome = "InvalidOutput";
					return AiDispatchOutcomes.InvalidOutput;
				}

				ledgerOutcome = "Answered";
				audit.Confidence = (decimal)Math.Round(enrichment.Confidence, 4);
				audit.RejectedCount = enrichment.RejectedCount;
				if (enrichment.Confidence < AiDispatchSettingsPolicy.EffectiveMinimumConfidence(settings, AiDispatchConfig.MinimumConfidence))
					return AiDispatchOutcomes.LowConfidence;

				return await ApplyAsync(call, enrichment, audit, settings, cancellationToken);
			}
			finally
			{
				// Failed inference is not charged: the GPU and its outages are the operator's, not the department's.
				try { await _usage.CompleteAsync(reservation.Reservation, usedTokens, ledgerOutcome, CancellationToken.None); }
				catch (Exception ex) { Framework.Logging.LogException(ex); }
			}
		}

		private static bool IsEnrichable(Call call, int departmentId) =>
			call != null && call.DepartmentId == departmentId && !call.IsDeleted && call.State == (int)CallStates.Active;

		private async Task<AiDispatchContext> BuildContextAsync(Call call, CancellationToken cancellationToken)
		{
			var departmentId = call.DepartmentId;
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
			return new AiDispatchContext(AiDispatchPrompt.MessageText(call.Name, call.NatureOfCall, Math.Clamp(AiDispatchConfig.MaxMessageCharacters, 200, 12000)),
				types, priorities, candidates);
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

		private async Task<string> ApplyAsync(Call original, AiDispatchEnrichment enrichment, AiDispatchAuditRow audit, DepartmentAiDispatchConfig settings, CancellationToken cancellationToken)
		{
			// Re-read so a dispatcher's edit made while the model was thinking always wins.
			var call = await _calls.GetCallByIdAsync(original.CallId, true);
			if (!IsEnrichable(call, original.DepartmentId))
				return AiDispatchOutcomes.CallNotActive;

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
				if (settings.RenamePlaceholder && string.Equals(call.Name?.Trim(), PlaceholderCallName, StringComparison.Ordinal) && enrichment.Title != null) { call.Name = enrichment.Title; applied.Add(nameof(Call.Name)); }
			}

			if (applied.Count > 0)
				call = await _calls.SaveCallAsync(call, cancellationToken);

			var note = BuildNote(call, enrichment, settings);
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

			audit.AppliedFields = applied.Count == 0 ? null : string.Join(",", applied);
			if (applied.Count == 0)
				return AiDispatchOutcomes.NoChange;

			// Boards and apps refresh like any edit; nothing is re-broadcast, so no one is paged again.
			_events.SendMessage(new CallUpdatedEvent { DepartmentId = call.DepartmentId, Call = call });
			return AiDispatchOutcomes.Applied;
		}

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
