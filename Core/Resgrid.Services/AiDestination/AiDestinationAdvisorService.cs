using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Events;
using Resgrid.Model.Messages;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.AiDestination
{
	/// <summary>
	/// Enhanced AI destination advice (call-intake-and-ai-destinations-plan.md D4, D9; M0323, worker 81). The model sees the call's type,
	/// priority, nature, note and newest person-written call notes beside the department's rule-ranked destinations (capabilities,
	/// availability, ranking tier, drive time) and its capability catalog, and recommends one of those destinations or keeping the current
	/// one. It never changes the call: the advice is shown on the call; a confident change also becomes a System call note, a live
	/// <c>aiDestinationAdviceUpdated</c> event, and a push to the dispatcher who logged the call and to the units on it; a person applies
	/// or dismisses it. Call content goes only to the operator's self-hosted model; departments enrolled in Advanced Data Protection are
	/// skipped until the ai-inference broker purpose exists. Every failure leaves the call as it was.
	/// </summary>
	public sealed class AiDestinationAdvisorService : IAiDestinationAdvisorService
	{
		/// <summary>AiUsageLedger feature name for destination advice rows.</summary>
		public const string Feature = "AiDestinationAdvice";

		/// <summary>Prefix of the System call notes this service writes (and the trigger in CallsService never reacts to).</summary>
		public const string NotePrefix = "Enhanced AI destination advice";

		private static readonly Regex Tags = new Regex("<[^>]{0,200}>", RegexOptions.Compiled);
		private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);

		private readonly IEnhancedAiAccessService _enhancedAi;
		private readonly IFeatureToggleService _flags;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly ICallsService _calls;
		private readonly IMappingService _mapping;
		private readonly INearestDestinationService _nearest;
		private readonly IDestinationRulesService _rules;
		private readonly IDepartmentSettingsService _settings;
		private readonly IDepartmentsService _departments;
		private readonly IUserProfileService _profiles;
		private readonly IPushService _push;
		private readonly IAiDestinationAdvicesRepository _advices;
		private readonly IAiDestinationReviewsRepository _reviews;
		private readonly IAiBackgroundAdmission _admission;
		private readonly IAiUsageMeter _usage;
		private readonly Lazy<ILlmClient> _client;
		private readonly IEventAggregator _events;
		private readonly AiDestinationAvailabilityCache _availability;
		private readonly TimeProvider _clock;

		public AiDestinationAdvisorService(IEnhancedAiAccessService enhancedAi, IFeatureToggleService flags, IDepartmentDataProtectionService protection,
			ICallsService calls, IMappingService mapping, INearestDestinationService nearest, IDestinationRulesService rules, IDepartmentSettingsService settings,
			IDepartmentsService departments, IUserProfileService profiles, IPushService push, IAiDestinationAdvicesRepository advices,
			IAiDestinationReviewsRepository reviews, IAiBackgroundAdmission admission, IAiUsageMeter usage, Lazy<ILlmClient> client, IEventAggregator events,
			AiDestinationAvailabilityCache availability, TimeProvider clock)
		{
			_enhancedAi = enhancedAi;
			_flags = flags;
			_protection = protection;
			_calls = calls;
			_mapping = mapping;
			_nearest = nearest;
			_rules = rules;
			_settings = settings;
			_departments = departments;
			_profiles = profiles;
			_push = push;
			_advices = advices;
			_reviews = reviews;
			_admission = admission;
			_usage = usage;
			_client = client;
			_events = events;
			_availability = availability;
			_clock = clock;
		}

		private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

		#region Availability

		public async Task<bool> IsAvailableAsync(int departmentId) => await CheckAsync(departmentId, false) == null;

		/// <summary>Null when the department may use advice now; else the outcome that blocks it. Remembered for a few minutes per process.</summary>
		private async Task<string> CheckAsync(int departmentId, bool fresh)
		{
			if (departmentId <= 0 || !AiDestinationConfig.Enabled)
				return AiDestinationOutcomes.Disabled;
			if (!OperatorConfigured())
				return AiDestinationOutcomes.Unconfigured;

			var now = UtcNow;
			if (!fresh && _availability.TryGet(departmentId, now, out var remembered))
				return remembered;

			string outcome;
			var lifetime = TimeSpan.FromSeconds(Math.Clamp(AiDestinationConfig.AvailabilityCacheSeconds, 0, 3600));
			try
			{
				var entitled = AiOperatorSettings.IsListedDepartment(AiConfig.SelfHostedDepartmentIds, departmentId)
					? await _enhancedAi.IsEnabledAsync(departmentId) && (await _flags.EvaluateFreshAsync(FeatureFlagKeys.AiDestinationAdvice, departmentId))?.IsEnabled == true
					: await _enhancedAi.CanUseAsync(departmentId, FeatureFlagKeys.AiDestinationAdvice);

				// Protected fields may reach inference only through a future ai-inference broker purpose (plan D9).
				outcome = !entitled ? AiDestinationOutcomes.NotEntitled
					: await _protection.ShouldEncryptNewWritesAsync(departmentId) ? AiDestinationOutcomes.ProtectionUnsupported : null;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"AI destination advice availability could not be read for department {departmentId}.");
				outcome = AiDestinationOutcomes.NotEntitled;
				lifetime = TimeSpan.FromSeconds(Math.Min(60, lifetime.TotalSeconds));
			}

			_availability.Set(departmentId, outcome, now + lifetime);
			return outcome;
		}

		/// <summary>The operator's generative model is a valid, reviewed, pinned endpoint (AI dispatch's rule, AiConfig).</summary>
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

		#endregion Availability

		#region Triggers

		public async Task RequestReviewAsync(int departmentId, int callId, AiDestinationReviewTriggers trigger, CancellationToken cancellationToken = default(CancellationToken))
		{
			try
			{
				if (departmentId <= 0 || callId <= 0 || !AiDestinationConfig.Enabled)
					return;
				if (await CheckAsync(departmentId, false) != null)
					return;

				var call = await _calls.GetCallByIdAsync(callId);
				if (!IsOpen(call, departmentId) || !await IsReviewableAsync(call))
					return;

				await _reviews.QueueAsync(departmentId, callId, trigger, UtcNow, CancellationToken.None);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				// A trigger rides on a note or a call save; it must never fail them.
				Framework.Logging.LogException(ex, $"AI destination review could not be queued for call {callId}.");
			}
		}

		public async Task RequestReviewsForDestinationAsync(int departmentId, int poiId, CancellationToken cancellationToken = default(CancellationToken))
		{
			try
			{
				if (departmentId <= 0 || poiId <= 0 || !AiDestinationConfig.Enabled)
					return;
				if (await CheckAsync(departmentId, false) != null)
					return;

				var active = await _calls.GetActiveCallsByDepartmentAsync(departmentId) ?? new List<Call>();
				var pending = await _calls.GetPendingCallsByDepartmentIdAsync(departmentId) ?? new List<Call>();
				var callIds = active.Concat(pending)
					.Where(c => IsOpen(c, departmentId) && c.DestinationPoiId == poiId)
					.Select(c => c.CallId).Distinct().Take(100).ToList();

				var now = UtcNow;
				foreach (var callId in callIds)
					await _reviews.QueueAsync(departmentId, callId, AiDestinationReviewTriggers.AvailabilityChanged, now, CancellationToken.None);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				Framework.Logging.LogException(ex, $"AI destination reviews could not be queued for destination {poiId}.");
			}
		}

		/// <summary>Background reviews only cover calls going somewhere: a destination POI, or a destination rule matching the call.</summary>
		private async Task<bool> IsReviewableAsync(Call call)
		{
			if (call.DestinationPoiId is > 0)
				return true;

			var config = await _settings.GetDestinationSuggestionConfigAsync(call.DepartmentId);
			if (config == null || config.GetMode() == DestinationSuggestionModes.Off)
				return false;

			var rules = await _rules.GetRulesForDepartmentAsync(call.DepartmentId) ?? new List<DestinationRule>();
			if (rules.Count == 0)
				return false;

			var context = await _rules.BuildContextAsync(call.DepartmentId, call.CallTypeId, call.Type, call.Priority, null);
			return rules.Any(r => DestinationRuleResolver.Matches(r, context));
		}

		#endregion Triggers

		#region Advising

		public async Task<AiDestinationAdviceOutcome> AdviseNowAsync(int departmentId, int callId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var blocked = await CheckAsync(departmentId, true);
			if (blocked != null)
				return AiDestinationAdviceOutcome.Of(blocked);

			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(AiDestinationConfig.OnDemandTimeoutSeconds, 5, 120)));
			try
			{
				var call = await _calls.GetCallByIdAsync(callId);
				if (!IsOpen(call, departmentId))
					return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.CallNotOpen);

				return await AdviseAsync(call, AiDestinationReviewTriggers.OnDemand, true, deadline.Token);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.Unavailable, await _advices.GetOpenForCallAsync(departmentId, callId));
			}
		}

		public async Task<AiDestinationReviewSweepResult> ProcessDueReviewsAsync(DateTime nowUtc, CancellationToken cancellationToken = default(CancellationToken))
		{
			var result = new AiDestinationReviewSweepResult();
			if (!AiDestinationConfig.Enabled)
				return result;

			var lease = TimeSpan.FromSeconds(Math.Clamp(AiDestinationConfig.LeaseSeconds, 60, 3600));
			var debounce = TimeSpan.FromSeconds(Math.Clamp(AiDestinationConfig.DebounceSeconds, 0, 3600));
			var due = await _reviews.GetDueAsync(nowUtc - debounce, nowUtc - lease, Math.Clamp(AiDestinationConfig.MaxReviewsPerPass, 1, 200), cancellationToken);

			foreach (var review in due)
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				var claimedOn = ClaimStamp(UtcNow);
				if (!await _reviews.TryClaimAsync(review.AiDestinationReviewId, claimedOn, claimedOn - lease, cancellationToken))
					continue;

				result.Claimed++;
				string outcome;
				try
				{
					outcome = await ReviewAsync(review, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					await _reviews.ReleaseAsync(review.AiDestinationReviewId, claimedOn, CancellationToken.None);
					throw;
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"AI destination review failed for call {review.CallId}.");
					outcome = AiDestinationOutcomes.Unavailable;
				}

				result.Outcomes[outcome] = result.Outcomes.TryGetValue(outcome, out var count) ? count + 1 : 1;
				if (outcome == AiDestinationOutcomes.Busy)
				{
					// No inference slot came free: the review stays waiting for a later pass.
					await _reviews.ReleaseAsync(review.AiDestinationReviewId, claimedOn, CancellationToken.None);
					result.Retried++;
					continue;
				}

				if (!await _reviews.CompleteAsync(review.AiDestinationReviewId, claimedOn, outcome, UtcNow, CancellationToken.None))
					result.Retried++;
				if (outcome == AiDestinationOutcomes.Advised)
					result.Advised++;
			}

			try
			{
				await _reviews.DeleteProcessedBeforeAsync(nowUtc.AddDays(-Math.Clamp(AiDestinationConfig.ReviewRetentionDays, 1, 365)), CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}

			return result;
		}

		private async Task<string> ReviewAsync(AiDestinationReview review, CancellationToken cancellationToken)
		{
			var blocked = await CheckAsync(review.DepartmentId, false);
			if (blocked != null)
				return blocked;

			var call = await _calls.GetCallByIdAsync(review.CallId);
			if (!IsOpen(call, review.DepartmentId))
				return AiDestinationOutcomes.CallNotOpen;

			var trigger = Enum.IsDefined(typeof(AiDestinationReviewTriggers), review.ReviewTrigger)
				? (AiDestinationReviewTriggers)review.ReviewTrigger : AiDestinationReviewTriggers.CallUpdated;
			return (await AdviseAsync(call, trigger, false, cancellationToken)).Outcome;
		}

		private async Task<AiDestinationAdviceOutcome> AdviseAsync(Call call, AiDestinationReviewTriggers trigger, bool onDemand, CancellationToken cancellationToken)
		{
			var departmentId = call.DepartmentId;
			var inputs = await BuildInputsAsync(call, cancellationToken);
			if (inputs.Outcome != null)
				return AiDestinationAdviceOutcome.Of(inputs.Outcome, onDemand ? await _advices.GetOpenForCallAsync(departmentId, call.CallId) : null);
			if (!onDemand && !inputs.Applicable)
				return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.NotApplicable);

			var digest = AiDestinationPrompt.Digest(inputs.Context, AiConfig.Model);
			var latest = await _advices.GetLatestForCallAsync(departmentId, call.CallId, cancellationToken);
			if (latest != null && latest.InputDigest == digest && latest.PromptVersion == AiDestinationPrompt.Version)
			{
				// Nothing the model reads changed. Open advice is still current; advice a person applied or dismissed is not offered again
				// in the background (a person asking again still gets a fresh answer).
				if (latest.State == (int)AiDestinationAdviceStates.Open)
					return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.Unchanged, latest);
				if (!onDemand)
					return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.Unchanged);
			}

			// Applying the advice saves the call, and that save is a trigger too: when the only difference is the destination the advice
			// itself set, the model is not asked to confirm its own recommendation.
			if (!onDemand && latest != null && latest.State == (int)AiDestinationAdviceStates.Applied && latest.PromptVersion == AiDestinationPrompt.Version &&
				latest.RecommendedPoiId.HasValue && latest.RecommendedPoiId == inputs.Context.CurrentPoiId &&
				AiDestinationPrompt.Digest(inputs.Context with { CurrentPoiId = latest.CurrentPoiId }, AiConfig.Model) == latest.InputDigest)
				return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.Unchanged);

			var answer = await AskAsync(departmentId, inputs.Context, onDemand, cancellationToken);
			if (answer.Outcome != null)
				return AiDestinationAdviceOutcome.Of(answer.Outcome, onDemand ? await _advices.GetOpenForCallAsync(departmentId, call.CallId) : null);

			// Re-read so a destination a dispatcher set while the model was thinking is what the advice is measured against.
			var current = await _calls.GetCallByIdAsync(call.CallId);
			if (!IsOpen(current, departmentId))
				return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.CallNotOpen);

			var decision = answer.Decision;
			var currentPoiId = current.DestinationPoiId is > 0 ? current.DestinationPoiId : null;
			var recommended = decision.RecommendedPoiId == currentPoiId ? null : decision.RecommendedPoiId;
			var now = UtcNow;
			var advice = new AiDestinationAdvice
			{
				AiDestinationAdviceId = Guid.NewGuid().ToString("D"),
				DepartmentId = departmentId,
				CallId = call.CallId,
				CurrentPoiId = currentPoiId,
				RecommendedPoiId = recommended,
				RequiredCapabilityIds = decision.RequiredCapabilityIds.Count == 0 ? null : string.Join(",", decision.RequiredCapabilityIds),
				Rationale = decision.Rationale,
				Confidence = Math.Round(decision.Confidence, 4),
				IsChange = recommended.HasValue,
				ReviewTrigger = (int)trigger,
				State = (int)AiDestinationAdviceStates.Open,
				InputDigest = digest,
				PromptVersion = AiDestinationPrompt.Version,
				ModelRevision = Truncate(AiConfig.ModelRevision, 100),
				CreatedOn = now
			};

			await _advices.InsertAsync(advice, CancellationToken.None);
			await _advices.SupersedeOpenAsync(departmentId, call.CallId, advice.AiDestinationAdviceId, now, CancellationToken.None);

			if (advice.IsChange && advice.Confidence >= Math.Clamp(AiDestinationConfig.MinimumConfidence, 0, 1))
				await AnnounceAsync(current, advice, inputs.PoiNames);

			await RaiseAdviceEventAsync(advice);
			return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.Advised, advice);
		}

		private sealed record Answer(AiDestinationDecision Decision, string Outcome);

		private async Task<Answer> AskAsync(int departmentId, AiDestinationContext context, bool onDemand, CancellationToken cancellationToken)
		{
			var selfHosted = AiOperatorSettings.IsListedDepartment(AiConfig.SelfHostedDepartmentIds, departmentId);
			var admission = await ReserveAsync(departmentId, selfHosted, onDemand ? Math.Min(8, Math.Clamp(AiDestinationConfig.OnDemandTimeoutSeconds, 5, 120) / 2) : AiDestinationConfig.AdmissionWaitSeconds,
				cancellationToken);
			if (admission.Outcome != null)
				return new Answer(null, admission.Outcome);

			var usedTokens = 0;
			var ledgerOutcome = "Unavailable";
			try
			{
				LlmResult result;
				using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(AiDestinationConfig.RequestTimeoutSeconds, 5, 300)));
					try
					{
						result = await _client.Value.CompleteAsync(new LlmRequest(AiConfig.Model, AiDestinationPrompt.Messages(context), new[] { AiDestinationPrompt.Tool(context) }, 512,
							AiCacheIsolation.SaltFor(departmentId, AiConfig.SendCacheSalt, AiConfig.CacheSaltKey, AiConfig.AuditHmacKey)), deadline.Token);
					}
					catch (Exception ex) when (ex is LlmUnavailableException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
					{
						return new Answer(null, AiDestinationOutcomes.Unavailable);
					}
				}

				usedTokens = Math.Min(admission.Reservation.Tokens, Math.Max(0, result.InputTokens + result.OutputTokens));
				try
				{
					var decision = AiDestinationPrompt.Validate(AiDestinationPrompt.Parse(result), context);
					ledgerOutcome = "Answered";
					return new Answer(decision, null);
				}
				catch (Exception ex) when (ex is ArgumentException || ex is System.Text.Json.JsonException)
				{
					ledgerOutcome = "InvalidOutput";
					return new Answer(null, AiDestinationOutcomes.InvalidOutput);
				}
			}
			finally
			{
				try { await _usage.CompleteAsync(admission.Reservation, usedTokens, ledgerOutcome, CancellationToken.None); }
				catch (Exception ex) { Framework.Logging.LogException(ex); }
			}
		}

		private sealed record Admission(AiUsageReservation Reservation, string Outcome);

		/// <summary>Background priority on the shared inference slots: waits for a moment when no turn is live, up to <paramref name="waitSeconds"/>.</summary>
		private async Task<Admission> ReserveAsync(int departmentId, bool selfHosted, int waitSeconds, CancellationToken cancellationToken)
		{
			var tokens = Math.Clamp(AiDestinationConfig.ReservedTokens, 1024, 32768);
			var tier = selfHosted ? AdminAssistAskTiers.SelfHosted : AdminAssistAskTiers.EnhancedAi;
			var giveUp = UtcNow.AddSeconds(Math.Clamp(waitSeconds, 0, 900));
			while (true)
			{
				var now = UtcNow;
				if (await _usage.RemainingAsync(departmentId, now, AiConfig.MonthlyTokenLimit, cancellationToken) < tokens)
					return new Admission(null, AiDestinationOutcomes.BudgetExhausted);
				var reservation = await _admission.ReserveBackgroundAsync(departmentId, Feature, tier, now, tokens, AiConfig.MonthlyTokenLimit, cancellationToken);
				if (reservation != null)
					return new Admission(reservation, null);
				if (now >= giveUp)
					return new Admission(null, AiDestinationOutcomes.Busy);
				await Task.Delay(TimeSpan.FromSeconds(2), _clock, cancellationToken);
			}
		}

		#endregion Advising

		#region Inputs

		private sealed class Inputs
		{
			public string Outcome { get; set; }
			public AiDestinationContext Context { get; set; }
			public bool Applicable { get; set; }
			public Dictionary<int, string> PoiNames { get; } = new Dictionary<int, string>();
		}

		/// <summary>What the model is shown for the call: its text, the rule-ranked destinations around it and the capability catalog.</summary>
		private async Task<Inputs> BuildInputsAsync(Call call, CancellationToken cancellationToken)
		{
			var inputs = new Inputs();
			var departmentId = call.DepartmentId;
			if (!CallLocations.TryParsePoint(call.GeoLocationData, out var latitude, out var longitude))
			{
				inputs.Outcome = AiDestinationOutcomes.NoLocation;
				return inputs;
			}

			var capabilities = (await _mapping.GetPoiCapabilitiesForDepartmentAsync(departmentId) ?? new List<PoiCapability>())
				.Where(c => c != null && !string.IsNullOrWhiteSpace(c.PoiCapabilityId) && !string.IsNullOrWhiteSpace(c.Name))
				.OrderBy(c => c.Sort).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Take(60).ToList();
			var catalog = new HashSet<string>(capabilities.Select(c => c.PoiCapabilityId), StringComparer.OrdinalIgnoreCase);

			var config = await _settings.GetDestinationSuggestionConfigAsync(departmentId) ?? new DestinationSuggestionConfig();
			var requirements = DestinationRequirements.None;
			List<NearestDestination> ranked;
			if (config.GetMode() != DestinationSuggestionModes.Off)
			{
				var suggestions = await _nearest.SuggestDestinationsAsync(new DestinationSuggestionRequest
				{
					DepartmentId = departmentId, Latitude = latitude, Longitude = longitude, CallTypeId = call.CallTypeId, CallType = call.Type,
					CallPriority = call.Priority, MaxResults = INearestDestinationService.MaximumResults
				}, cancellationToken);
				ranked = suggestions?.Destinations ?? new List<NearestDestination>();
				requirements = suggestions?.Requirements ?? DestinationRequirements.None;
			}
			else
			{
				// The department does not use destination rules: every destination, nearest first.
				ranked = await _nearest.GetNearestDestinationsAsync(departmentId, latitude, longitude, null, null, INearestDestinationService.MaximumResults, cancellationToken)
					?? new List<NearestDestination>();
			}

			var now = UtcNow;
			var candidates = SelectCandidates(ranked.Where(d => d?.Poi != null).ToList(), Math.Clamp(AiDestinationConfig.MaxCandidates, 1, 25))
				.Select(d => ToCandidate(d.Poi, d.Status, d.Tier, d.MeetsRequirements, d.TravelSeconds, d.DistanceMeters, catalog, now)).ToList();

			var currentPoiId = call.DestinationPoiId is > 0 ? call.DestinationPoiId : null;
			if (currentPoiId.HasValue && candidates.All(c => c.PoiId != currentPoiId.Value))
			{
				// The current destination is always shown, so the model can say whether it still fits.
				var poi = await _mapping.GetDestinationPOIByIdAsync(departmentId, currentPoiId.Value);
				if (poi != null)
				{
					await _mapping.PopulatePoiDetailsAsync(departmentId, new[] { poi });
					var status = PoiAvailabilityRules.StatusOf(poi.Availability, now);
					var meets = requirements.AllowsType(poi.PoiTypeId) && requirements.RequiredCapabilityIds.All(id => (poi.CapabilityIds ?? new List<string>()).Contains(id, StringComparer.OrdinalIgnoreCase));
					var tier = status == PoiAvailabilityStatuses.Closed ? DestinationTiers.Closed : !meets ? DestinationTiers.Lacking
						: status == PoiAvailabilityStatuses.Diversion ? DestinationTiers.Diversion : DestinationTiers.Ready;
					candidates.Add(ToCandidate(poi, status, tier, meets, -1, DistanceMeters(latitude, longitude, poi.Latitude, poi.Longitude), catalog, now));
				}
			}

			if (candidates.Count == 0)
			{
				inputs.Outcome = AiDestinationOutcomes.NoCandidates;
				return inputs;
			}

			foreach (var candidate in candidates)
				inputs.PoiNames[candidate.PoiId] = candidate.Name;

			await _calls.PopulateCallData(call, false, false, true, false, false, false, false, false, false);
			var maxNote = Math.Clamp(AiDestinationConfig.MaxNoteChars, 50, 4000);
			var notes = (call.CallNotes ?? new List<CallNote>())
				.Where(n => n != null && !n.IsDeleted && n.Source != (int)CallNoteSources.System)
				.OrderByDescending(n => n.Timestamp)
				.Select(n => new { n.Timestamp, Text = Plain(n.Note, maxNote) })
				.Where(n => n.Text != null)
				.Take(Math.Clamp(AiDestinationConfig.MaxCallNotes, 0, 100))
				.Select(n => new AiDestinationCallNote((int)Math.Max(0, (now - DateTime.SpecifyKind(n.Timestamp, DateTimeKind.Utc)).TotalMinutes), n.Text))
				.ToList();

			var maxText = Math.Clamp(AiDestinationConfig.MaxCallTextChars, 100, 12000);
			var freeDestination = currentPoiId.HasValue ? null
				: !string.IsNullOrWhiteSpace(call.DestinationAddress) ? Plain(call.DestinationAddress, 200)
				: !string.IsNullOrWhiteSpace(call.DestinationGeoLocationData) ? "a map point" : null;

			inputs.Context = new AiDestinationContext(
				Truncate(call.Type?.Trim(), 100),
				await PriorityNameAsync(departmentId, call.Priority),
				Plain(call.NatureOfCall, maxText),
				Plain(call.Notes, maxText),
				notes,
				currentPoiId,
				freeDestination,
				candidates,
				(requirements.AppliedRules ?? new List<DestinationRule>()).Where(r => r != null && !string.IsNullOrWhiteSpace(r.Name)).Select(r => r.Name.Trim()).Take(10).ToList(),
				(requirements.RequiredCapabilityIds ?? new List<string>()).Where(catalog.Contains).ToList(),
				(requirements.PreferredCapabilityIds ?? new List<string>()).Where(catalog.Contains).ToList(),
				capabilities.Select(c => new AiDestinationCapability(c.PoiCapabilityId, Truncate(c.Name.Trim(), 100))).ToList());
			inputs.Applicable = currentPoiId.HasValue || (requirements.AppliedRules?.Count ?? 0) > 0;
			return inputs;
		}

		/// <summary>
		/// The top of the ranking, plus up to five farther destinations that offer a capability none of those do, so a specialty centre
		/// (a Level I trauma or burn centre past the nearest hospitals) is always on the table.
		/// </summary>
		public static List<NearestDestination> SelectCandidates(List<NearestDestination> ranked, int take)
		{
			var selected = ranked.Take(take).ToList();
			var offered = new HashSet<string>(selected.SelectMany(d => d.Poi.CapabilityIds ?? new List<string>()), StringComparer.OrdinalIgnoreCase);
			foreach (var extra in ranked.Skip(take))
			{
				if (selected.Count >= take + 5)
					break;

				var adds = (extra.Poi.CapabilityIds ?? new List<string>()).Where(id => !offered.Contains(id)).ToList();
				if (adds.Count == 0 || extra.Tier == DestinationTiers.Closed)
					continue;

				selected.Add(extra);
				foreach (var id in adds)
					offered.Add(id);
			}

			return selected;
		}

		private static AiDestinationCandidate ToCandidate(Poi poi, PoiAvailabilityStatuses status, int tier, bool meets, double travelSeconds, double distanceMeters,
			HashSet<string> catalog, DateTime nowUtc)
		{
			var availability = PoiAvailabilityRules.Current(poi.Availability, nowUtc);
			return new AiDestinationCandidate(
				poi.PoiId,
				Truncate(string.IsNullOrWhiteSpace(poi.Name) ? "Destination " + poi.PoiId.ToString(CultureInfo.InvariantCulture) : poi.Name.Trim(), 100),
				Truncate(poi.Type?.Name?.Trim(), 60),
				(poi.CapabilityIds ?? new List<string>()).Where(catalog.Contains).ToList(),
				(availability?.GetUnavailableCapabilityIds() ?? new List<string>()).Where(catalog.Contains).ToList(),
				status.ToString().ToLowerInvariant(),
				tier,
				meets,
				travelSeconds >= 0 ? (int)Math.Round(travelSeconds / 60.0) : (int?)null,
				Math.Max(0, distanceMeters) / 1000.0,
				Plain(availability?.Note, 200));
		}

		private async Task<string> PriorityNameAsync(int departmentId, int priority)
		{
			var priorities = await _calls.GetActiveCallPrioritiesForDepartmentAsync(departmentId) ?? new List<DepartmentCallPriority>();
			var match = priorities.FirstOrDefault(p => p != null && p.DepartmentCallPriorityId == priority && !string.IsNullOrWhiteSpace(p.Name));
			if (match != null)
				return Truncate(match.Name.Trim(), 60);

			return priority switch { 0 => "Low", 1 => "Medium", 2 => "High", 3 => "Emergency", _ => null };
		}

		/// <summary>Markup removed, entities decoded, whitespace collapsed, cut to <paramref name="max"/>; null when empty.</summary>
		public static string Plain(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var text = Spaces.Replace(WebUtility.HtmlDecode(Tags.Replace(value, " ")), " ").Trim();
			if (text.Length == 0)
				return null;

			return text.Length <= max ? text : text.Substring(0, max).TrimEnd();
		}

		private static double DistanceMeters(double latitude1, double longitude1, double latitude2, double longitude2)
		{
			const double radius = 6371000;
			double Rad(double degrees) => degrees * Math.PI / 180;
			var dLat = Rad(latitude2 - latitude1);
			var dLon = Rad(longitude2 - longitude1);
			var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(latitude1)) * Math.Cos(Rad(latitude2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
			return radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
		}

		#endregion Inputs

		#region Reading, applying, dismissing

		public Task<AiDestinationAdvice> GetOpenAdviceAsync(int departmentId, int callId) => _advices.GetOpenForCallAsync(departmentId, callId);

		public Task<AiDestinationAdvice> GetAdviceByIdAsync(int departmentId, string adviceId) => _advices.GetForDepartmentAsync(departmentId, adviceId);

		public async Task<AiDestinationAdviceDetails> DescribeAsync(int departmentId, AiDestinationAdvice advice)
		{
			var details = new AiDestinationAdviceDetails();
			if (advice == null || advice.DepartmentId != departmentId)
				return details;

			var ids = PoiAvailabilityRules.ParseIds(advice.RequiredCapabilityIds);
			if (ids.Count > 0)
			{
				var names = (await _mapping.GetPoiCapabilitiesForDepartmentAsync(departmentId) ?? new List<PoiCapability>())
					.Where(c => c != null && !string.IsNullOrWhiteSpace(c.PoiCapabilityId))
					.GroupBy(c => c.PoiCapabilityId, StringComparer.OrdinalIgnoreCase)
					.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
				foreach (var id in ids)
				{
					if (names.TryGetValue(id, out var name))
						details.RequiredCapabilities.Add(new KeyValuePair<string, string>(id, name));
				}
			}

			if (advice.CurrentPoiId.HasValue)
				details.CurrentPoiName = (await _mapping.GetDestinationPOIByIdAsync(departmentId, advice.CurrentPoiId.Value))?.Name;

			if (advice.RecommendedPoiId.HasValue)
			{
				var recommended = await _mapping.GetDestinationPOIByIdAsync(departmentId, advice.RecommendedPoiId.Value);
				if (recommended != null)
				{
					details.RecommendedPoiName = recommended.Name;
					var availability = (await _mapping.GetCurrentPoiAvailabilitiesAsync(departmentId))?.GetValueOrDefault(recommended.PoiId);
					details.RecommendedPoiStatus = availability == null ? null : (int)PoiAvailabilityRules.StatusOf(availability, UtcNow);
				}
			}

			return details;
		}

		public async Task<AiDestinationAdviceOutcome> ApplyAsync(int departmentId, string adviceId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var advice = await _advices.GetForDepartmentAsync(departmentId, adviceId, cancellationToken);
			if (advice == null)
				return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.NotFound);
			if (advice.State != (int)AiDestinationAdviceStates.Open)
				return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.NotOpen, advice);
			if (!advice.RecommendedPoiId.HasValue)
				return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.NothingToApply, advice);
			if (await _protection.ShouldEncryptNewWritesAsync(departmentId))
				return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.ProtectionUnsupported, advice);

			var poi = await _mapping.GetDestinationPOIByIdAsync(departmentId, advice.RecommendedPoiId.Value);
			if (poi == null)
				return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.DestinationGone, advice);

			var call = await _calls.GetCallByIdAsync(advice.CallId);
			if (!IsOpen(call, departmentId))
				return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.CallNotOpen, advice);

			// The transition is the claim: two people pressing Apply save the destination once.
			var now = UtcNow;
			if (!await _advices.TryTransitionAsync(departmentId, advice.AiDestinationAdviceId, AiDestinationAdviceStates.Open, AiDestinationAdviceStates.Applied, userId, now, CancellationToken.None))
				return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.NotOpen, await _advices.GetForDepartmentAsync(departmentId, advice.AiDestinationAdviceId, CancellationToken.None));

			try
			{
				// One destination: the POI clears any free destination (the save normalizes it the same way).
				call.DestinationPoiId = poi.PoiId;
				CallLocations.NormalizeDestination(call);
				call = await _calls.SaveCallAsync(call, CancellationToken.None);
			}
			catch
			{
				await _advices.TryTransitionAsync(departmentId, advice.AiDestinationAdviceId, AiDestinationAdviceStates.Applied, AiDestinationAdviceStates.Open, null, null, CancellationToken.None);
				throw;
			}

			advice.State = (int)AiDestinationAdviceStates.Applied;
			advice.ResolvedByUserId = userId;
			advice.ResolvedOn = now;

			await NoteAsync(call, userId, $"{NotePrefix} applied by {await PersonNameAsync(userId)}: destination set to {Truncate(poi.Name?.Trim(), 100) ?? "the recommended destination"}.");
			// Boards and apps refresh like any edit; nothing is re-broadcast, so no one is paged again.
			_events.SendMessage(new CallUpdatedEvent { DepartmentId = departmentId, Call = call });
			await RaiseAdviceEventAsync(advice);

			return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.Applied, advice);
		}

		public async Task<AiDestinationAdviceOutcome> DismissAsync(int departmentId, string adviceId, string userId, string reason, CancellationToken cancellationToken = default(CancellationToken))
		{
			var advice = await _advices.GetForDepartmentAsync(departmentId, adviceId, cancellationToken);
			if (advice == null)
				return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.NotFound);
			if (advice.State != (int)AiDestinationAdviceStates.Open)
				return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.NotOpen, advice);

			var now = UtcNow;
			if (!await _advices.TryTransitionAsync(departmentId, advice.AiDestinationAdviceId, AiDestinationAdviceStates.Open, AiDestinationAdviceStates.Dismissed, userId, now, CancellationToken.None))
				return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.NotOpen, await _advices.GetForDepartmentAsync(departmentId, advice.AiDestinationAdviceId, CancellationToken.None));

			advice.State = (int)AiDestinationAdviceStates.Dismissed;
			advice.ResolvedByUserId = userId;
			advice.ResolvedOn = now;

			// A change was announced in the call's notes, so its dismissal is too.
			if (advice.IsChange)
			{
				var call = await _calls.GetCallByIdAsync(advice.CallId);
				if (call != null && call.DepartmentId == departmentId && !call.IsDeleted)
				{
					var why = Plain(reason, 200);
					await NoteAsync(call, userId, $"{NotePrefix} dismissed by {await PersonNameAsync(userId)}" + (why == null ? "." : ": " + why));
				}
			}

			await RaiseAdviceEventAsync(advice);
			return AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.Dismissed, advice);
		}

		#endregion Reading, applying, dismissing

		#region Announcing

		/// <summary>
		/// A confident change: a System call note everyone on the call sees, and a push to the dispatcher who logged the call (a person,
		/// not an import) and to the devices of the units still on the call (the transporting crew is one of them). Push only, never SMS
		/// or email, and never the reasoning (it is read from patient details); the push opens the call. Best effort.
		/// </summary>
		private async Task AnnounceAsync(Call call, AiDestinationAdvice advice, IReadOnlyDictionary<int, string> poiNames)
		{
			var poiName = advice.RecommendedPoiId.HasValue && poiNames.TryGetValue(advice.RecommendedPoiId.Value, out var name) ? name : "another destination";
			try
			{
				var managingUserId = (await _departments.GetDepartmentByIdAsync(call.DepartmentId, false))?.ManagingUserId;
				if (!string.IsNullOrWhiteSpace(managingUserId))
					await NoteAsync(call, managingUserId, BuildNote(poiName, advice.Rationale));
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"AI destination advice note failed for call {call.CallId}; the advice was saved.");
			}

			try
			{
				await NotifyAsync(call, poiName);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"AI destination advice push failed for call {call.CallId}; the advice was saved.");
			}
		}

		/// <summary>The System call note for a confident change.</summary>
		public static string BuildNote(string poiName, string rationale) =>
			$"{NotePrefix}: consider {poiName} — {rationale} (verify before relying on it; apply or dismiss it on the call).";

		private async Task NotifyAsync(Call call, string poiName)
		{
			if (SystemBehaviorConfig.DoNotBroadcast && !SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Contains(call.DepartmentId))
				return;

			var department = await _departments.GetDepartmentByIdAsync(call.DepartmentId, false);
			var number = string.IsNullOrWhiteSpace(call.Number) ? call.CallId.ToString(CultureInfo.InvariantCulture) : call.Number;
			StandardPushMessage Message() => new StandardPushMessage
			{
				Title = "Enhanced AI destination advice",
				SubTitle = Truncate($"Call {number}: consider {poiName}. Open the call to apply or dismiss.", 200),
				DepartmentId = call.DepartmentId,
				DepartmentCode = department?.Code,
				// "NC:" opens the call in the Responder, Unit and IC apps with a notification sound; a code starting with "C" would ring as a dispatch.
				Id = "NC:" + call.CallId.ToString(CultureInfo.InvariantCulture)
			};

			if (call.CallSource == (int)CallSources.User && !string.IsNullOrWhiteSpace(call.ReportingUserId))
				await _push.PushNotification(Message(), call.ReportingUserId);

			await _calls.PopulateCallData(call, false, false, false, false, true, false, false, false, false);
			var unitIds = (call.UnitDispatches ?? Enumerable.Empty<CallDispatchUnit>())
				.Where(d => d != null && !d.ReleasedOn.HasValue).Select(d => d.UnitId).Distinct()
				.Take(Math.Clamp(AiDestinationConfig.MaxUnitsNotified, 0, 50)).ToList();
			foreach (var unitId in unitIds)
			{
				try { await _push.PushNotificationUnit(Message(), unitId); }
				catch (Exception ex) { Framework.Logging.LogException(ex); }
			}
		}

		private async Task NoteAsync(Call call, string userId, string text)
		{
			try
			{
				await _calls.SaveCallNoteAsync(new CallNote
				{
					CallId = call.CallId, UserId = userId, Note = text, Source = (int)CallNoteSources.System, Timestamp = UtcNow
				}, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"AI destination advice note failed for call {call.CallId}.");
			}
		}

		private async Task<string> PersonNameAsync(string userId)
		{
			try
			{
				var profile = string.IsNullOrWhiteSpace(userId) ? null : await _profiles.GetProfileByUserIdAsync(userId);
				var name = profile?.FullName?.AsFirstNameLastName?.Trim();
				return string.IsNullOrWhiteSpace(name) ? "a dispatcher" : name;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return "a dispatcher";
			}
		}

		/// <summary>EventingTypes 18: ids and state only; clients reload through GetAdvice, which checks the viewer may see the call.</summary>
		private async Task RaiseAdviceEventAsync(AiDestinationAdvice advice)
		{
			try
			{
				await _events.SendMessageAsync(new AiDestinationAdviceUpdatedEvent
				{
					DepartmentId = advice.DepartmentId,
					CallId = advice.CallId,
					Payload = JsonConvert.SerializeObject(new { adviceId = advice.AiDestinationAdviceId, state = advice.State, isChange = advice.IsChange })
				});
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"AI destination advice realtime update failed for call {advice.CallId}.");
			}
		}

		#endregion Announcing

		private static bool IsOpen(Call call, int departmentId) =>
			call != null && call.DepartmentId == departmentId && !call.IsDeleted &&
			(call.State == (int)CallStates.Active || call.State == (int)CallStates.Pending);

		/// <summary>Claims are compared exactly, so they are written at a precision both databases store unchanged.</summary>
		public static DateTime ClaimStamp(DateTime utc) => new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

		private static string Truncate(string value, int max) =>
			string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value.Substring(0, max);
	}
}
