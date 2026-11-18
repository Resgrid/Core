using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Recurring calls (M0319, M0320). Each occurrence of a schedule becomes an ordinary scheduled call (<see cref="Call.DispatchOn"/>)
	/// <see cref="RecurringCall.LeadTimeMinutes"/> before it is due; when it is due the scheduled-calls worker sends it through
	/// <see cref="DispatchCallAsync"/>, which adds run card resources, leaves off chosen resources that are not available and holds
	/// the call (pending) when nobody is left. In dispatcher mode nothing is created: the dispatcher is alerted to send each
	/// occurrence. A <see cref="RecurringCallOccurrence"/> row, unique per schedule and time, is claimed before anything is done
	/// with an occurrence, so it is dealt with once however many worker passes or dispatchers race for it; a
	/// <see cref="RecurringCallAlert"/>, unique per occurrence and kind, is raised (and its recipients told) once.
	/// </summary>
	public class RecurringCallsService : IRecurringCallsService
	{
		/// <summary>How long a claim may stay "creating" before the next pass treats its process as dead.</summary>
		public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(10);

		/// <summary>Closed alerts are kept this long for the history list, then purged by the worker.</summary>
		public static readonly TimeSpan ClosedAlertRetention = TimeSpan.FromDays(90);

		/// <summary>A "some resources were unavailable" alert nobody dismisses closes itself after this long.</summary>
		public static readonly TimeSpan UnavailableAlertLifetime = TimeSpan.FromHours(24);

		/// <summary>The reason recorded on calls deleted because their schedule changed, was paused or was deleted.</summary>
		public const string ReplacedReason = "Replaced by a change to its recurring call schedule";
		public const string ScheduleDeletedReason = "Its recurring call schedule was deleted";
		public const string SchedulePausedReason = "Its recurring call schedule was paused";
		public const string DuplicateReason = "Created twice for one occurrence of its recurring call schedule";

		/// <summary>The push event code prefix of an alert notification ("RCA:{alertId}").</summary>
		public const string AlertEventCodePrefix = "RCA:";

		private const string SystemUserId = "system";

		private static readonly JsonSerializerSettings CamelCase = new JsonSerializerSettings
		{
			ContractResolver = new CamelCasePropertyNamesContractResolver(),
			NullValueHandling = NullValueHandling.Ignore
		};

		private readonly IRecurringCallsRepository _recurringCallsRepository;
		private readonly IRecurringCallOccurrencesRepository _occurrencesRepository;
		private readonly IRecurringCallAlertsRepository _alertsRepository;
		private readonly ICallsService _callsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUnitsService _unitsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IMappingService _mappingService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IDispatchScopeService _dispatchScopeService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IQueueService _queueService;
		private readonly IUserProfileService _userProfileService;
		private readonly ICallDispatchStatusService _callDispatchStatusService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IDispatchRecommendationService _dispatchRecommendationService;
		private readonly ICustomStateService _customStateService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUserStateService _userStateService;
		private readonly ICommunicationService _communicationService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public RecurringCallsService(IRecurringCallsRepository recurringCallsRepository, IRecurringCallOccurrencesRepository occurrencesRepository,
			IRecurringCallAlertsRepository alertsRepository, ICallsService callsService, IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService, IUnitsService unitsService, IPersonnelRolesService personnelRolesService,
			IMappingService mappingService, IGeoLocationProvider geoLocationProvider, IDispatchScopeService dispatchScopeService,
			IEventAggregator eventAggregator, IQueueService queueService, IUserProfileService userProfileService,
			ICallDispatchStatusService callDispatchStatusService, IFeatureToggleService featureToggleService,
			IDispatchRecommendationService dispatchRecommendationService, ICustomStateService customStateService, IActionLogsService actionLogsService,
			IUserStateService userStateService, ICommunicationService communicationService, IDepartmentSettingsService departmentSettingsService)
		{
			_recurringCallsRepository = recurringCallsRepository;
			_occurrencesRepository = occurrencesRepository;
			_alertsRepository = alertsRepository;
			_callsService = callsService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_unitsService = unitsService;
			_personnelRolesService = personnelRolesService;
			_mappingService = mappingService;
			_geoLocationProvider = geoLocationProvider;
			_dispatchScopeService = dispatchScopeService;
			_eventAggregator = eventAggregator;
			_queueService = queueService;
			_userProfileService = userProfileService;
			_callDispatchStatusService = callDispatchStatusService;
			_featureToggleService = featureToggleService;
			_dispatchRecommendationService = dispatchRecommendationService;
			_customStateService = customStateService;
			_actionLogsService = actionLogsService;
			_userStateService = userStateService;
			_communicationService = communicationService;
			_departmentSettingsService = departmentSettingsService;
		}

		#region Schedules

		public async Task<List<RecurringCall>> GetAllForDepartmentAsync(int departmentId)
		{
			var items = await _recurringCallsRepository.GetForDepartmentAsync(departmentId);
			return items?.Where(x => x.DepartmentId == departmentId && !x.IsDeleted).OrderBy(x => x.Name).ToList() ?? new List<RecurringCall>();
		}

		public async Task<List<RecurringCall>> GetAllVisibleToUserAsync(int departmentId, string userId)
		{
			return await FilterSchedulesForUserAsync(departmentId, userId, await GetAllForDepartmentAsync(departmentId));
		}

		public async Task<bool> IsVisibleToUserAsync(RecurringCall series, string userId)
		{
			if (series == null)
				return false;

			return (await FilterSchedulesForUserAsync(series.DepartmentId, userId, new List<RecurringCall> { series })).Any();
		}

		public async Task<RecurringCall> GetByIdAsync(int departmentId, int recurringCallId)
		{
			var item = await _recurringCallsRepository.GetByIdAsync(recurringCallId);

			if (item == null || item.DepartmentId != departmentId || item.IsDeleted)
				return null;

			return item;
		}

		public async Task<RecurringCallSaveResult> SaveAsync(RecurringCall series, string userId, bool replaceUpcomingCalls, AuditOrigin origin = null,
			CancellationToken cancellationToken = default)
		{
			if (series == null)
				throw new ArgumentNullException(nameof(series));

			var now = DateTime.UtcNow;
			var result = new RecurringCallSaveResult();
			var isNew = series.RecurringCallId == 0;
			var before = isNew ? null : await _recurringCallsRepository.GetByIdAsync(series.RecurringCallId);

			series.Times = RecurringCallRecurrence.FormatTimes(RecurringCallRecurrence.ParseTimes(series.Times));
			series.StartsOn = DateTime.SpecifyKind(series.StartsOn.Date, DateTimeKind.Unspecified);
			if (series.EndsOn.HasValue)
				series.EndsOn = DateTime.SpecifyKind(series.EndsOn.Value.Date, DateTimeKind.Unspecified);
			if (series.ResumeOn.HasValue)
				series.ResumeOn = DateTime.SpecifyKind(series.ResumeOn.Value.Date, DateTimeKind.Unspecified);
			series.ManualAlertMinutes = Math.Clamp(series.ManualAlertMinutes, 0, RecurringCallRecurrence.MaxManualAlertMinutes);
			series.ScheduleContactEmail = string.IsNullOrWhiteSpace(series.ScheduleContactEmail) ? null : series.ScheduleContactEmail.Trim();

			await ApplyFacilityDefaultsAsync(series);

			// An address with no pin is located once here, rather than on every call the schedule creates.
			if (string.IsNullOrWhiteSpace(series.GeoLocationData) && !string.IsNullOrWhiteSpace(series.Address))
			{
				try
				{
					series.GeoLocationData = await _geoLocationProvider.GetLatLonFromAddress(series.Address);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Could not locate the address of recurring call schedule '{series.Name}'.");
				}
			}

			if (isNew)
			{
				series.CreatedByUserId = userId;
				series.CreatedOn = now;
				series.UpdatedByUserId = null;
				series.UpdatedOn = null;
			}
			else
			{
				series.UpdatedByUserId = userId;
				series.UpdatedOn = now;
			}

			var saved = await _recurringCallsRepository.SaveOrUpdateAsync(series, cancellationToken);
			result.Series = saved;

			if (replaceUpcomingCalls && !isNew)
				result.ReplacedCalls = await DeleteUpcomingCallsAsync(saved, userId, ReplacedReason, now, cancellationToken);

			Audit(isNew ? AuditLogTypes.RecurringCallCreated : AuditLogTypes.RecurringCallUpdated, saved.DepartmentId, userId, before, saved, origin);
			await RaiseScheduleEventAsync(saved, isNew ? RecurringCallScheduleChanges.Created : RecurringCallScheduleChanges.Updated, userId, now);

			if (saved.IsActive)
				result.CreatedCalls = await GenerateForSeriesAsync(saved, now, cancellationToken);

			return result;
		}

		public async Task<RecurringCall> PauseAsync(int departmentId, int recurringCallId, string userId, string reason, DateTime? resumeOn, bool deleteUpcomingCalls,
			AuditOrigin origin = null, CancellationToken cancellationToken = default)
		{
			var series = await GetByIdAsync(departmentId, recurringCallId);
			if (series == null)
				return null;

			var now = DateTime.UtcNow;
			var before = Snapshot(series);

			series.IsActive = false;
			series.PausedOn = now;
			series.PausedByUserId = userId;
			series.PauseReason = string.IsNullOrWhiteSpace(reason) ? null : Truncate(reason.Trim(), 1000);
			series.ResumeOn = resumeOn.HasValue ? DateTime.SpecifyKind(resumeOn.Value.Date, DateTimeKind.Unspecified) : (DateTime?)null;
			series.UpdatedByUserId = userId;
			series.UpdatedOn = now;

			var saved = await _recurringCallsRepository.SaveOrUpdateAsync(series, cancellationToken);

			// Paused now: the rest must not stop because the request went away, or the calls it was asked to remove would still go
			// out with no schedule state left to retry from.
			if (deleteUpcomingCalls)
				await DeleteUpcomingCallsAsync(saved, userId, SchedulePausedReason, now, CancellationToken.None);

			Audit(AuditLogTypes.RecurringCallPaused, saved.DepartmentId, userId, before, saved, origin);
			await RaiseScheduleEventAsync(saved, RecurringCallScheduleChanges.Paused, userId, now);

			return saved;
		}

		public async Task<RecurringCall> ResumeAsync(int departmentId, int recurringCallId, string userId, AuditOrigin origin = null, CancellationToken cancellationToken = default)
		{
			var series = await GetByIdAsync(departmentId, recurringCallId);
			if (series == null)
				return null;

			return await ResumeInternalAsync(series, userId, origin, DateTime.UtcNow, cancellationToken);
		}

		public async Task<bool> DeleteAsync(int departmentId, int recurringCallId, string userId, bool deleteUpcomingCalls, AuditOrigin origin = null,
			CancellationToken cancellationToken = default)
		{
			var series = await GetByIdAsync(departmentId, recurringCallId);
			if (series == null)
				return false;

			var now = DateTime.UtcNow;
			var before = Snapshot(series);
			series.IsDeleted = true;
			series.IsActive = false;
			series.UpdatedByUserId = userId;
			series.UpdatedOn = now;

			await _recurringCallsRepository.SaveOrUpdateAsync(series, cancellationToken);

			// Deleted now, and a deleted schedule cannot be loaded to retry: the clean-up runs to the end even if the request went away.
			if (deleteUpcomingCalls)
				await DeleteUpcomingCallsAsync(series, userId, ScheduleDeletedReason, now, CancellationToken.None);

			// Alerts about occurrences that have no call are moot now; one about a held or sent call stays until that call is settled.
			foreach (var alert in (await _alertsRepository.GetOpenForScheduleAsync(series.RecurringCallId) ?? Enumerable.Empty<RecurringCallAlert>())
					 .Where(x => !x.CallId.HasValue))
				await ResolveAlertAsync(alert, series, RecurringCallAlertResolutions.ScheduleDeleted, userId, null, now, CancellationToken.None);

			Audit(AuditLogTypes.RecurringCallDeleted, series.DepartmentId, userId, before, null, origin);
			await RaiseScheduleEventAsync(series, RecurringCallScheduleChanges.Deleted, userId, now);

			return true;
		}

		public async Task<List<Call>> GetUpcomingCallsAsync(int departmentId, int recurringCallId, string userId = null)
		{
			var calls = (await _recurringCallsRepository.GetCallsForScheduleAsync(departmentId, recurringCallId, DateTime.UtcNow.AddHours(-RecurringCallRecurrence.MissedLookbackHours))
						 ?? Enumerable.Empty<Call>()).Where(x => IsWaitingToGoOut(x) || IsHeld(x)).ToList();

			// A call can be changed on its own after the schedule created it: the member sees the ones in their dispatch scope.
			if (userId != null && calls.Any())
				calls = await _dispatchScopeService.FilterCallsForUserAsync(departmentId, userId, calls) ?? new List<Call>();

			return calls.OrderBy(x => x.DispatchOn).ToList();
		}

		#endregion Schedules

		#region Occurrences

		public async Task<bool> SkipOccurrenceAsync(int departmentId, int recurringCallId, DateTime occurrenceUtc, string userId, AuditOrigin origin = null,
			CancellationToken cancellationToken = default)
		{
			var series = await GetByIdAsync(departmentId, recurringCallId);
			if (series == null || !await IsOccurrenceAsync(series, occurrenceUtc))
				return false;

			var now = DateTime.UtcNow;
			var skipped = false;
			var existing = await _occurrencesRepository.GetByOccurrenceAsync(series.RecurringCallId, occurrenceUtc);

			if (existing == null)
			{
				skipped = await _occurrencesRepository.TryClaimAsync(new RecurringCallOccurrence
				{
					RecurringCallId = series.RecurringCallId,
					DepartmentId = series.DepartmentId,
					OccurrenceOn = occurrenceUtc,
					State = (int)RecurringCallOccurrenceStates.Skipped,
					UserId = userId,
					UpdatedOn = now
				}, cancellationToken);
			}
			else if (existing.State == (int)RecurringCallOccurrenceStates.AwaitingDispatcher || existing.State == (int)RecurringCallOccurrenceStates.Missed)
			{
				skipped = await _occurrencesRepository.TryTransitionStateAsync(existing.RecurringCallOccurrenceId, existing.State,
					(int)RecurringCallOccurrenceStates.Skipped, userId, now, cancellationToken);
			}

			if (!skipped)
				return false;

			await ResolveOccurrenceAlertsAsync(series, occurrenceUtc, RecurringCallAlertResolutions.Skipped, userId, now, cancellationToken);

			Audit(AuditLogTypes.RecurringCallOccurrenceSkipped, series.DepartmentId, userId, null, OccurrenceSnapshot(series, occurrenceUtc, null), origin);
			RaiseOccurrenceEvent(series, occurrenceUtc, RecurringCallOccurrenceChanges.Skipped, null, userId, true, null);

			return true;
		}

		public async Task<bool> RestoreOccurrenceAsync(int departmentId, int recurringCallId, DateTime occurrenceUtc, string userId, AuditOrigin origin = null,
			CancellationToken cancellationToken = default)
		{
			var series = await GetByIdAsync(departmentId, recurringCallId);
			if (series == null)
				return false;

			var occurrence = await _occurrencesRepository.GetByOccurrenceAsync(series.RecurringCallId, occurrenceUtc);
			if (occurrence == null || occurrence.State != (int)RecurringCallOccurrenceStates.Skipped)
				return false;

			if (!await _occurrencesRepository.DeleteIfStateAsync(occurrence.RecurringCallOccurrenceId, (int)RecurringCallOccurrenceStates.Skipped, cancellationToken))
				return false;

			Audit(AuditLogTypes.RecurringCallOccurrenceRestored, series.DepartmentId, userId, null, OccurrenceSnapshot(series, occurrenceUtc, null), origin);

			// Due within the lead time already: create it now rather than waiting for the next worker pass.
			if (series.IsActive)
				await GenerateForSeriesAsync(series, DateTime.UtcNow, cancellationToken);

			return true;
		}

		public async Task<Call> CreateOccurrenceCallNowAsync(int departmentId, int recurringCallId, DateTime occurrenceUtc, string userId, AuditOrigin origin = null,
			CancellationToken cancellationToken = default)
		{
			var series = await GetByIdAsync(departmentId, recurringCallId);
			var now = DateTime.UtcNow;

			if (series == null || !await IsOccurrenceAsync(series, occurrenceUtc))
				return null;

			var claim = await TakeOverOccurrenceAsync(series, occurrenceUtc, userId, now, cancellationToken);
			if (claim.ExistingCall != null)
				return claim.ExistingCall;
			if (claim.Claim == null)
				return null;

			// A future occurrence becomes a scheduled call for its time; one already due waits as pending, so the dispatcher sends it.
			var future = occurrenceUtc > now;
			var targets = await LoadDispatchTargetsAsync(series.DepartmentId);
			var call = await CreateClaimedCallAsync(series, claim.Claim, occurrenceUtc, now, userId, targets, future ? (int)CallStates.Active : (int)CallStates.Pending,
				occurrenceUtc, cancellationToken);

			if (call != null)
				Audit(AuditLogTypes.RecurringCallOccurrenceCreated, series.DepartmentId, userId, null, OccurrenceSnapshot(series, occurrenceUtc, call), origin);

			return call;
		}

		public async Task<RecurringCallDispatchResult> DispatchOccurrenceNowAsync(int departmentId, int recurringCallId, DateTime occurrenceUtc, string userId,
			AuditOrigin origin = null, CancellationToken cancellationToken = default)
		{
			var series = await GetByIdAsync(departmentId, recurringCallId);
			var now = DateTime.UtcNow;

			if (series == null || !await IsOccurrenceAsync(series, occurrenceUtc))
				return RecurringCallDispatchResult.Of(RecurringCallDispatchOutcomes.NotCreated);

			var claim = await TakeOverOccurrenceAsync(series, occurrenceUtc, userId, now, cancellationToken);
			Call call;

			if (claim.ExistingCall != null)
			{
				call = claim.ExistingCall;
			}
			else if (claim.Claim != null)
			{
				// Saved for the occurrence's own time, the identity a dead claim is recovered by (a call saved at "now" would not be
				// found, and the occurrence could be created twice); it goes out now below.
				var targets = await LoadDispatchTargetsAsync(series.DepartmentId);
				call = await CreateClaimedCallAsync(series, claim.Claim, occurrenceUtc, now, userId, targets, (int)CallStates.Active, occurrenceUtc, cancellationToken);
				if (call == null)
					return RecurringCallDispatchResult.Of(RecurringCallDispatchOutcomes.NotCreated);

				Audit(AuditLogTypes.RecurringCallOccurrenceCreated, series.DepartmentId, userId, null, OccurrenceSnapshot(series, occurrenceUtc, call), origin);

				// If the broadcast cannot be queued, a call for a time already past is left scheduled for now, so the worker retries it.
				if (occurrenceUtc <= now)
					call.DispatchOn = now;
			}
			else
			{
				return RecurringCallDispatchResult.Of(RecurringCallDispatchOutcomes.NotCreated);
			}

			// A held (pending) call is claimed as it is (the claim takes a pending call), never saved back to scheduled first: a
			// second dispatcher's stale save would otherwise clear the first one's claim and both would send it.
			return await DispatchCallInternalAsync(call, series, occurrenceUtc, userId, true, now, cancellationToken);
		}

		#endregion Occurrences

		#region Dispatch

		public async Task<RecurringCallDispatchResult> DispatchCallAsync(Call call, string userId, CancellationToken cancellationToken = default)
		{
			if (call == null || !call.RecurringCallId.HasValue)
				return RecurringCallDispatchResult.Of(RecurringCallDispatchOutcomes.NotWaiting, call);

			// The schedule as it is, paused or deleted included: a call it already created still goes out unless someone deleted it.
			var series = await _recurringCallsRepository.GetByIdAsync(call.RecurringCallId.Value);
			if (series != null && series.DepartmentId != call.DepartmentId)
				series = null;

			var occurrence = series == null ? null : await _occurrencesRepository.GetByCallAsync(series.RecurringCallId, call.CallId);
			var occurrenceUtc = occurrence != null
				? DateTime.SpecifyKind(occurrence.OccurrenceOn, DateTimeKind.Utc)
				: DateTime.SpecifyKind(call.DispatchOn ?? DateTime.UtcNow, DateTimeKind.Utc);

			return await DispatchCallInternalAsync(call, series, occurrenceUtc, userId, !string.IsNullOrWhiteSpace(userId), null, cancellationToken);
		}

		public async Task OnCallDispatchedAsync(Call call, string userId, CancellationToken cancellationToken = default)
		{
			if (call == null || !call.RecurringCallId.HasValue)
				return;

			try
			{
				var series = await _recurringCallsRepository.GetByIdAsync(call.RecurringCallId.Value);
				if (series == null || series.DepartmentId != call.DepartmentId)
					return;

				var occurrence = await _occurrencesRepository.GetByCallAsync(series.RecurringCallId, call.CallId);
				var occurrenceUtc = occurrence != null
					? DateTime.SpecifyKind(occurrence.OccurrenceOn, DateTimeKind.Utc)
					: DateTime.SpecifyKind(call.DispatchOn ?? DateTime.UtcNow, DateTimeKind.Utc);

				await ResolveOccurrenceAlertsAsync(series, occurrenceUtc, RecurringCallAlertResolutions.Dispatched, userId, DateTime.UtcNow, CancellationToken.None,
					RecurringCallAlertKinds.NoResources, RecurringCallAlertKinds.DispatchDue, RecurringCallAlertKinds.Missed, RecurringCallAlertKinds.CreateFailed);

				// The one publisher of this occurrence's Dispatched trigger for a call sent this way (the alert reconciliation only closes
				// alerts), so a workflow runs once whether or not the call had an alert.
				RaiseOccurrenceEvent(series, occurrenceUtc, RecurringCallOccurrenceChanges.Dispatched, call, userId, true, null);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Could not record the dispatch of recurring call {call.CallId}.");
			}
		}

		/// <param name="sendNowUtc">A dispatcher sending it now: it goes out with this as its dispatch time.</param>
		private async Task<RecurringCallDispatchResult> DispatchCallInternalAsync(Call call, RecurringCall series, DateTime occurrenceUtc, string userId,
			bool byDispatcher, DateTime? sendNowUtc, CancellationToken cancellationToken)
		{
			// Only whoever marks the stored call dispatched first sends it (a dispatcher's Dispatch Now, another worker pass).
			var claimedOn = await _callsService.TryClaimCallForDispatchAsync(call.CallId, call.DepartmentId, cancellationToken);
			if (!claimedOn.HasValue)
				return RecurringCallDispatchResult.Of(RecurringCallDispatchOutcomes.NotWaiting, call);

			// What the call is put back to when nothing goes out: a scheduled call scheduled again, a held one held again.
			var originalDispatchOn = call.DispatchOn;
			var originalState = call.State == (int)CallStates.Pending ? (int)CallStates.Pending : (int)CallStates.Active;
			var originalHasBeenDispatched = originalState == (int)CallStates.Pending ? (bool?)null : false;
			var result = new RecurringCallDispatchResult { Call = call };
			var sent = false;
			var handedBack = false;

			try
			{
				// PopulateCallData hydrates and returns the same instance: the one object carried through enrichment, broadcast and save.
				var populated = await _callsService.PopulateCallData(call, true, false, false, true, true, true, true, false, false);
				result.Call = populated;

				DispatchRecommendationResult recommendation = null;
				try
				{
					if (await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, populated.DepartmentId))
					{
						// The schedule's "use the run card" applies the match whatever the card's auto-dispatch setting; otherwise only an
						// auto-dispatching card adds resources, as for any scheduled call.
						var forced = series?.UseRunCard == true;
						var enriched = await _dispatchRecommendationService.EnrichCallForDispatchAsync(populated, 1, !forced, cancellationToken);

						if (enriched != null && enriched.MatchedRunCardId.HasValue && enriched.HasRecommendations && (forced || enriched.AutoDispatch))
							recommendation = enriched;
					}
				}
				catch (Exception ex)
				{
					// A recommendation failure must never block the dispatch.
					Logging.LogException(ex);
				}

				result.RunCardApplied = recommendation != null;

				var removed = await RemoveUnavailableAsync(populated, series != null, cancellationToken);
				result.UnavailableUnitIds = removed.Units.Select(x => x.UnitId).Distinct().ToList();
				result.UnavailableUserIds = removed.People.Select(x => x.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

				if (!HasRecipients(populated))
				{
					// Nobody to send: hold it as pending for a dispatcher, without the unavailable resources so it shows what is missing.
					// Handed back once, as pending. False means the call moved on meanwhile (closed, deleted): it is left as it is, with
					// its resources, and nothing is announced. If the release throws, the claim is still this call's to give back below.
					var released = await _callsService.ReleaseCallDispatchClaimAsync(call.CallId, call.DepartmentId, claimedOn.Value, (int)CallStates.Pending,
						originalDispatchOn, null, CancellationToken.None);
					handedBack = true;

					if (!released)
					{
						result.Outcome = RecurringCallDispatchOutcomes.NotWaiting;
						return result;
					}

					await DeleteRemovedDispatchesAsync(removed, CancellationToken.None);

					populated.State = (int)CallStates.Pending;
					populated.HasBeenDispatched = null;
					_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent { DepartmentId = populated.DepartmentId, Call = populated });

					if (series != null)
					{
						var reason = removed.Units.Any() || removed.People.Any() ? RecurringCallAlertReasons.NoneAvailable : RecurringCallAlertReasons.NoneChosen;
						await RaiseAlertAsync(series, occurrenceUtc, RecurringCallAlertKinds.NoResources, populated.CallId,
							new RecurringCallAlertDetail { Reason = reason, UnavailableUnitIds = NullIfEmpty(result.UnavailableUnitIds), UnavailableUserIds = NullIfEmpty(result.UnavailableUserIds) },
							CancellationToken.None);
					}

					result.Outcome = RecurringCallDispatchOutcomes.Held;
					return result;
				}

				var cqi = new CallQueueItem { Call = populated };

				// Groups, roles and units fan out to members the broadcast resolves itself, so it gets every profile; a personnel-only
				// dispatch needs just those people's.
				if (populated.GroupDispatches?.Any() == true || populated.RoleDispatches?.Any() == true || populated.UnitDispatches?.Any() == true)
					cqi.Profiles = (await _userProfileService.GetAllProfilesForDepartmentAsync(populated.DepartmentId))?.Select(x => x.Value).ToList();
				else
					cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(populated.Dispatches.Select(x => x.UserId).ToList());

				try
				{
					sent = await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Could not queue the dispatch of recurring call {populated.CallId}.");
					sent = false;
				}

				if (!sent)
				{
					result.Outcome = RecurringCallDispatchOutcomes.QueueFailed;
					return result;
				}

				// Ended before anything else can fail, so the claim's lease never sends the call twice.
				try
				{
					await _callsService.CompleteCallDispatchClaimAsync(call.CallId, call.DepartmentId, claimedOn.Value, CancellationToken.None);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Could not complete the dispatch claim on call {call.CallId}.");
				}

				await DeleteRemovedDispatchesAsync(removed, CancellationToken.None);

				// A held call that went out is active again; one a dispatcher sent now carries the time it went.
				populated.State = (int)CallStates.Active;
				if (sendNowUtc.HasValue)
					populated.DispatchOn = sendNowUtc.Value;
				populated.HasBeenDispatched = true;
				await _callsService.SaveCallAsync(populated, CancellationToken.None);

				if (recommendation != null)
				{
					try
					{
						await _dispatchRecommendationService.RecordActivationAsync(populated, recommendation, userId, CancellationToken.None);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}

				await _callDispatchStatusService.ApplyDispatchStatusesAsync(populated, cancellationToken: CancellationToken.None);

				result.Outcome = RecurringCallDispatchOutcomes.Dispatched;

				if (series != null)
				{
					var now = DateTime.UtcNow;
					await ResolveOccurrenceAlertsAsync(series, occurrenceUtc, RecurringCallAlertResolutions.Dispatched, userId, now, CancellationToken.None,
						RecurringCallAlertKinds.NoResources, RecurringCallAlertKinds.DispatchDue, RecurringCallAlertKinds.Missed, RecurringCallAlertKinds.CreateFailed);

					if (result.UnavailableUnitIds.Any() || result.UnavailableUserIds.Any())
						await RaiseAlertAsync(series, occurrenceUtc, RecurringCallAlertKinds.ResourcesUnavailable, populated.CallId,
							new RecurringCallAlertDetail { UnavailableUnitIds = NullIfEmpty(result.UnavailableUnitIds), UnavailableUserIds = NullIfEmpty(result.UnavailableUserIds) },
							CancellationToken.None);

					RaiseOccurrenceEvent(series, occurrenceUtc, RecurringCallOccurrenceChanges.Dispatched, populated, userId, byDispatcher, result);
				}

				return result;
			}
			finally
			{
				if (!sent && !handedBack)
				{
					try
					{
						await _callsService.ReleaseCallDispatchClaimAsync(call.CallId, call.DepartmentId, claimedOn.Value, originalState, originalDispatchOn,
							originalHasBeenDispatched, CancellationToken.None);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"Could not hand back the dispatch claim on call {call.CallId}.");
					}
				}
			}
		}

		private sealed class RemovedDispatches
		{
			public List<CallDispatchUnit> Units { get; } = new List<CallDispatchUnit>();
			public List<CallDispatch> People { get; } = new List<CallDispatch>();
		}

		/// <summary>Takes chosen units and members that are not available off the in-memory call (the stored rows go once it is decided).</summary>
		private async Task<RemovedDispatches> RemoveUnavailableAsync(Call call, bool check, CancellationToken cancellationToken)
		{
			var removed = new RemovedDispatches();

			if (!check || (call.UnitDispatches?.Any() != true && call.Dispatches?.Any() != true))
				return removed;

			try
			{
				var states = await _customStateService.GetAllCustomStatesForDepartmentAsync(call.DepartmentId) ?? new List<CustomState>();
				var unitBaseTypes = RecurringCallResourceAvailability.CustomBaseTypes(states, CustomStateTypes.Unit);
				var latestUnitStates = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(call.DepartmentId) ?? new List<UnitState>();
				var unitState = latestUnitStates.Where(x => x != null).GroupBy(x => x.UnitId).ToDictionary(x => x.Key, x => x.OrderByDescending(s => s.Timestamp).First());

				foreach (var dispatch in (call.UnitDispatches ?? new List<CallDispatchUnit>()).ToList())
				{
					unitState.TryGetValue(dispatch.UnitId, out var latest);
					if (!RecurringCallResourceAvailability.IsUnitAvailable(latest, unitBaseTypes))
					{
						removed.Units.Add(dispatch);
						call.UnitDispatches.Remove(dispatch);
					}
				}

				if (call.Dispatches?.Any() == true)
				{
					var personnelBaseTypes = RecurringCallResourceAvailability.CustomBaseTypes(states, CustomStateTypes.Personnel);
					var staffingBaseTypes = RecurringCallResourceAvailability.CustomBaseTypes(states, CustomStateTypes.Staffing);
					var unitAvailability = PersonnelOnUnit.LatestUnitAvailability(latestUnitStates, unitBaseTypes);
					var statuses = (await _actionLogsService.GetLastActionLogsForDepartmentAsync(call.DepartmentId) ?? new List<ActionLog>())
						.Where(x => x != null).GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.OrderByDescending(a => a.Timestamp).First(), StringComparer.OrdinalIgnoreCase);
					var staffing = (await _userStateService.GetLatestStatesForDepartmentAsync(call.DepartmentId) ?? new List<UserState>())
						.Where(x => x != null).GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.OrderByDescending(s => s.Timestamp).First(), StringComparer.OrdinalIgnoreCase);

					foreach (var dispatch in call.Dispatches.ToList())
					{
						statuses.TryGetValue(dispatch.UserId ?? string.Empty, out var status);
						staffing.TryGetValue(dispatch.UserId ?? string.Empty, out var staffingState);

						if (!RecurringCallResourceAvailability.IsPersonAvailable(status, staffingState, personnelBaseTypes, staffingBaseTypes, unitAvailability))
						{
							removed.People.Add(dispatch);
							call.Dispatches.Remove(dispatch);
						}
					}
				}
			}
			catch (Exception ex)
			{
				// Availability could not be read: send to everyone chosen rather than hold the call.
				Logging.LogException(ex, $"Could not check resource availability for recurring call {call.CallId}.");

				foreach (var unit in removed.Units)
					call.UnitDispatches.Add(unit);
				foreach (var person in removed.People)
					call.Dispatches.Add(person);

				return new RemovedDispatches();
			}

			return removed;
		}

		private async Task DeleteRemovedDispatchesAsync(RemovedDispatches removed, CancellationToken cancellationToken)
		{
			try
			{
				var units = removed.Units.Where(x => x.CallDispatchUnitId > 0).ToList();
				if (units.Any())
					await _callsService.DeleteUnitDispatchesAsync(units, cancellationToken);

				var people = removed.People.Where(x => x.CallDispatchId > 0).ToList();
				if (people.Any())
					await _callsService.DeleteDispatchesAsync(people, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Could not remove unavailable resources from a recurring call.");
			}
		}

		private static bool HasRecipients(Call call)
		{
			return call.Dispatches?.Any() == true || call.GroupDispatches?.Any() == true ||
				   call.RoleDispatches?.Any() == true || call.UnitDispatches?.Any() == true;
		}

		#endregion Dispatch

		#region Worker

		public async Task<int> GenerateDueCallsAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			await ResolveStaleClaimsAsync(nowUtc, cancellationToken);
			await ResumeDueSchedulesAsync(nowUtc, cancellationToken);

			var created = 0;
			var schedules = await _recurringCallsRepository.GetActiveAsync() ?? Enumerable.Empty<RecurringCall>();

			foreach (var series in schedules)
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				try
				{
					created += await GenerateForSeriesAsync(series, nowUtc, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					// One schedule that cannot be created must not hold up every other department's.
					Logging.LogException(ex, $"Recurring call schedule {series.RecurringCallId} could not create its calls.");
				}
			}

			try
			{
				var open = (await _alertsRepository.GetAllOpenAsync() ?? Enumerable.Empty<RecurringCallAlert>()).ToList();
				await ReconcileAlertsAsync(open, nowUtc, cancellationToken);
				await RetryUnnotifiedAlertsAsync(open, nowUtc, cancellationToken);
				await _alertsRepository.PurgeResolvedBeforeAsync(nowUtc - ClosedAlertRetention, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Recurring call alerts could not be reconciled.");
			}

			return created;
		}

		/// <summary>How long an alert whose recipients could not be reached is offered to them again, once a pass.</summary>
		public static readonly TimeSpan UnnotifiedAlertRetryWindow = TimeSpan.FromHours(1);

		/// <summary>
		/// Open alerts nobody was told about (every send failed, or the process stopped after storing it) are sent again on the
		/// worker's passes for <see cref="UnnotifiedAlertRetryWindow"/>; one that reached anybody is marked and not sent again.
		/// </summary>
		private async Task RetryUnnotifiedAlertsAsync(List<RecurringCallAlert> open, DateTime nowUtc, CancellationToken cancellationToken)
		{
			var schedules = new Dictionary<int, RecurringCall>();

			// Older than a minute, so one being raised (and notified) by another process right now is not sent twice.
			foreach (var alert in open.Where(x => x != null && x.IsOpen && !x.NotifiedOn.HasValue &&
													  DateTime.SpecifyKind(x.CreatedOn, DateTimeKind.Utc) < nowUtc.AddMinutes(-1) &&
													  DateTime.SpecifyKind(x.CreatedOn, DateTimeKind.Utc) >= nowUtc - UnnotifiedAlertRetryWindow))
			{
				if (cancellationToken.IsCancellationRequested)
					return;

				try
				{
					if (!schedules.TryGetValue(alert.RecurringCallId, out var series))
						schedules[alert.RecurringCallId] = series = await _recurringCallsRepository.GetByIdAsync(alert.RecurringCallId);

					if (series == null || series.IsDeleted || series.DepartmentId != alert.DepartmentId)
						continue;

					if (await NotifyAsync(series, alert, (RecurringCallAlertKinds)alert.Kind, alert.GetDetail()))
						await _alertsRepository.MarkNotifiedAsync(alert.RecurringCallAlertId, DateTime.UtcNow, CancellationToken.None);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Could not notify the recipients of recurring call alert {alert.RecurringCallAlertId}.");
				}
			}
		}

		private async Task ResumeDueSchedulesAsync(DateTime nowUtc, CancellationToken cancellationToken)
		{
			IEnumerable<RecurringCall> paused;
			try
			{
				paused = await _recurringCallsRepository.GetPausedWithResumeDateAsync() ?? Enumerable.Empty<RecurringCall>();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return;
			}

			foreach (var series in paused)
			{
				try
				{
					var department = await _departmentsService.GetDepartmentByIdAsync(series.DepartmentId, false);
					if (department == null || !series.ResumeOn.HasValue)
						continue;

					var localToday = RecurringCallRecurrence.ToLocal(nowUtc, department).Date;
					if (localToday >= series.ResumeOn.Value.Date)
						await ResumeInternalAsync(series, null, null, nowUtc, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Recurring call schedule {series.RecurringCallId} could not be resumed.");
				}
			}
		}

		private async Task<RecurringCall> ResumeInternalAsync(RecurringCall series, string userId, AuditOrigin origin, DateTime nowUtc, CancellationToken cancellationToken)
		{
			if (series.IsActive)
				return series;

			var before = Snapshot(series);
			series.IsActive = true;
			series.PausedOn = null;
			series.PausedByUserId = null;
			series.PauseReason = null;
			series.ResumeOn = null;
			// Resuming stamps the schedule, so nothing due while it was paused is created now.
			series.UpdatedByUserId = userId ?? series.UpdatedByUserId;
			series.UpdatedOn = nowUtc;

			var saved = await _recurringCallsRepository.SaveOrUpdateAsync(series, cancellationToken);

			Audit(AuditLogTypes.RecurringCallResumed, saved.DepartmentId, userId ?? SystemUserId, before, saved, origin);
			await RaiseScheduleEventAsync(saved, RecurringCallScheduleChanges.Resumed, userId, nowUtc);

			await GenerateForSeriesAsync(saved, nowUtc, cancellationToken);

			return saved;
		}

		/// <summary>
		/// One schedule's work for this pass: the calls due within its lead time (dispatcher mode: alerts for those due within its
		/// alert window), and missed-call alerts for occurrences that passed with nothing done.
		/// </summary>
		private async Task<int> GenerateForSeriesAsync(RecurringCall series, DateTime nowUtc, CancellationToken cancellationToken)
		{
			if (series == null || !series.IsActive || series.IsDeleted)
				return 0;

			var department = await _departmentsService.GetDepartmentByIdAsync(series.DepartmentId, false);
			if (department == null)
				return 0;

			// Never an occurrence due before the schedule took its current shape, and never one so late it would not go out.
			var from = nowUtc.AddMinutes(-RecurringCallRecurrence.MissedGraceMinutes);
			if (series.EffectiveFrom > from)
				from = series.EffectiveFrom;

			var ahead = series.IsDispatcherMode
				? Math.Clamp(series.ManualAlertMinutes, 0, RecurringCallRecurrence.MaxManualAlertMinutes)
				: Math.Clamp(series.LeadTimeMinutes, RecurringCallRecurrence.MinLeadTimeMinutes, RecurringCallRecurrence.MaxLeadTimeMinutes);
			var to = nowUtc.AddMinutes(ahead).AddSeconds(1);

			var created = 0;
			var due = to > from ? RecurringCallRecurrence.UtcOccurrences(series, department, from, to) : new List<DateTime>();

			if (due.Any())
			{
				var dealtWith = await DealtWithAsync(series.RecurringCallId, from, to);
				var pending = due.Where(x => !dealtWith.ContainsKey(x)).ToList();

				// The snapshot this pass read may be stale by now: a schedule paused, deleted or changed since creates nothing from it
				// (a pause that removes the calls already created would not see one created after it).
				if (pending.Any() && await IsStillAsReadAsync(series))
				{
					var targets = series.IsDispatcherMode ? null : await LoadDispatchTargetsAsync(series.DepartmentId);

					foreach (var occurrence in pending)
					{
						if (cancellationToken.IsCancellationRequested)
							break;

						if (series.IsDispatcherMode)
							await AwaitDispatcherAsync(series, occurrence, nowUtc, cancellationToken);
						else if (await CreateOccurrenceAsync(series, occurrence, nowUtc, null, targets, cancellationToken) != null)
							created++;
					}
				}

				// Awaiting a dispatcher with no alert ever raised (storing it failed after the claim): raised now.
				if (series.IsDispatcherMode)
					await RaiseMissingAlertsAsync(series, dealtWith.Values, RecurringCallOccurrenceStates.AwaitingDispatcher, RecurringCallAlertKinds.DispatchDue, cancellationToken);
			}

			await RaiseMissedAsync(series, department, nowUtc, cancellationToken);

			return created;
		}

		private async Task<Dictionary<DateTime, RecurringCallOccurrence>> DealtWithAsync(int recurringCallId, DateTime fromUtc, DateTime toUtc)
		{
			return (await _occurrencesRepository.GetForScheduleInRangeAsync(recurringCallId, fromUtc, toUtc) ?? Enumerable.Empty<RecurringCallOccurrence>())
				.Where(x => x != null)
				.GroupBy(x => DateTime.SpecifyKind(x.OccurrenceOn, DateTimeKind.Utc))
				.ToDictionary(x => x.Key, x => x.First());
		}

		/// <summary>
		/// The stored schedule still runs and is the one this pass read (not paused, deleted or changed since). Its last-change time
		/// is compared to the second: a schedule just saved by this request is compared with its database copy, which can round it.
		/// </summary>
		private async Task<bool> IsStillAsReadAsync(RecurringCall series)
		{
			var stored = await _recurringCallsRepository.GetByIdAsync(series.RecurringCallId);

			return stored != null && stored.DepartmentId == series.DepartmentId && stored.IsActive && !stored.IsDeleted &&
				   Math.Abs((stored.EffectiveFrom - series.EffectiveFrom).TotalSeconds) < 1;
		}

		/// <summary>
		/// Occurrences claimed in <paramref name="state"/> whose alert was never stored (the insert failed after the claim): the next
		/// pass raises it. One raised before (open or closed) is never raised again, so a dismissed alert stays dismissed.
		/// </summary>
		private async Task RaiseMissingAlertsAsync(RecurringCall series, IEnumerable<RecurringCallOccurrence> rows, RecurringCallOccurrenceStates state,
			RecurringCallAlertKinds kind, CancellationToken cancellationToken)
		{
			foreach (var row in rows.Where(x => x.State == (int)state))
			{
				if (cancellationToken.IsCancellationRequested)
					return;

				var occurrenceUtc = DateTime.SpecifyKind(row.OccurrenceOn, DateTimeKind.Utc);

				try
				{
					if (!await _alertsRepository.AnyForOccurrenceAsync(series.RecurringCallId, occurrenceUtc, (int)kind))
						await RaiseAlertAsync(series, occurrenceUtc, kind, null, null, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Could not check the {kind} alert of recurring call schedule {series.RecurringCallId}.");
				}
			}
		}

		/// <summary>Dispatcher mode: claims the occurrence as awaiting a dispatcher and alerts them.</summary>
		private async Task AwaitDispatcherAsync(RecurringCall series, DateTime occurrenceUtc, DateTime nowUtc, CancellationToken cancellationToken)
		{
			var claimed = await _occurrencesRepository.TryClaimAsync(new RecurringCallOccurrence
			{
				RecurringCallOccurrenceId = Guid.NewGuid().ToString(),
				RecurringCallId = series.RecurringCallId,
				DepartmentId = series.DepartmentId,
				OccurrenceOn = occurrenceUtc,
				State = (int)RecurringCallOccurrenceStates.AwaitingDispatcher,
				UpdatedOn = nowUtc
			}, cancellationToken);

			if (claimed)
				await RaiseAlertAsync(series, occurrenceUtc, RecurringCallAlertKinds.DispatchDue, null, null, cancellationToken);
		}

		/// <summary>Occurrences that passed (within the look-back) with nothing created, skipped or awaiting: recorded as missed and alerted.</summary>
		private async Task RaiseMissedAsync(RecurringCall series, Department department, DateTime nowUtc, CancellationToken cancellationToken)
		{
			var from = nowUtc.AddHours(-RecurringCallRecurrence.MissedLookbackHours);
			if (series.EffectiveFrom > from)
				from = series.EffectiveFrom;

			var to = nowUtc.AddMinutes(-RecurringCallRecurrence.MissedGraceMinutes);
			if (to <= from)
				return;

			var passed = RecurringCallRecurrence.UtcOccurrences(series, department, from, to);
			if (!passed.Any())
				return;

			var dealtWith = await DealtWithAsync(series.RecurringCallId, from, to);

			// Missed with no alert ever raised (storing it failed after the claim): raised now.
			await RaiseMissingAlertsAsync(series, dealtWith.Values, RecurringCallOccurrenceStates.Missed, RecurringCallAlertKinds.Missed, cancellationToken);

			foreach (var occurrence in passed.Where(x => !dealtWith.ContainsKey(x)))
			{
				var claimed = await _occurrencesRepository.TryClaimAsync(new RecurringCallOccurrence
				{
					RecurringCallOccurrenceId = Guid.NewGuid().ToString(),
					RecurringCallId = series.RecurringCallId,
					DepartmentId = series.DepartmentId,
					OccurrenceOn = occurrence,
					State = (int)RecurringCallOccurrenceStates.Missed,
					UpdatedOn = nowUtc
				}, cancellationToken);

				if (claimed)
					await RaiseAlertAsync(series, occurrence, RecurringCallAlertKinds.Missed, null, null, cancellationToken);
			}
		}

		/// <summary>Claims one occurrence and creates its call; null when someone else has it or creating it failed (the claim is then given back).</summary>
		private async Task<Call> CreateOccurrenceAsync(RecurringCall series, DateTime occurrenceUtc, DateTime nowUtc, string userId, RecurringCallDispatchTargets targets,
			CancellationToken cancellationToken)
		{
			var claim = new RecurringCallOccurrence
			{
				RecurringCallOccurrenceId = Guid.NewGuid().ToString(),
				RecurringCallId = series.RecurringCallId,
				DepartmentId = series.DepartmentId,
				OccurrenceOn = occurrenceUtc,
				State = (int)RecurringCallOccurrenceStates.Creating,
				UserId = userId,
				UpdatedOn = nowUtc
			};

			if (!await _occurrencesRepository.TryClaimAsync(claim, cancellationToken))
				return null;

			return await CreateClaimedCallAsync(series, claim, occurrenceUtc, nowUtc, userId, targets, (int)CallStates.Active, occurrenceUtc, cancellationToken);
		}

		/// <summary>
		/// Creates the call for an occurrence this caller has claimed (<see cref="RecurringCallOccurrenceStates.Creating"/>), marks the
		/// occurrence created and closes the alerts about it having no call. On failure the claim is given back (or, for an occurrence
		/// a dispatcher took over, put back as it was) and a create-failed alert is raised.
		/// </summary>
		private async Task<Call> CreateClaimedCallAsync(RecurringCall series, RecurringCallOccurrence claim, DateTime occurrenceUtc, DateTime nowUtc, string userId,
			RecurringCallDispatchTargets targets, int state, DateTime dispatchOnUtc, CancellationToken cancellationToken)
		{
			Call saved;
			try
			{
				var destinationValid = series.DestinationPoiId.HasValue && series.DestinationPoiId.Value > 0 &&
									   await _mappingService.GetDestinationPOIByIdAsync(series.DepartmentId, series.DestinationPoiId.Value) != null;

				var call = BuildCall(series, dispatchOnUtc, nowUtc, userId ?? series.OwnerUserId, targets, destinationValid);
				call.State = state;
				if (state == (int)CallStates.Pending)
					call.HasBeenDispatched = null;

				saved = await _callsService.SaveCallAsync(call, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Recurring call schedule {series.RecurringCallId} could not create its call for {occurrenceUtc:O}.");

				// The save inserts the call before its later steps, so a failure can come after the call exists. Looked up by the
				// occurrence's identity (schedule, dispatch time, logged since the claim): found, it is this occurrence's call; not
				// known (the lookup failed too), the claim is left for the stale-claim recovery to settle the same way.
				Call committed;
				try
				{
					committed = await _recurringCallsRepository.GetCallForOccurrenceAsync(series.RecurringCallId, DateTime.SpecifyKind(dispatchOnUtc, DateTimeKind.Utc),
						nowUtc.AddMinutes(-1));
				}
				catch (Exception lookupEx)
				{
					Logging.LogException(lookupEx, $"Could not tell whether recurring call schedule {series.RecurringCallId} saved its call for {occurrenceUtc:O}.");
					return null;
				}

				if (committed == null)
				{
					if (claim.PreviousState.HasValue)
						await _occurrencesRepository.TryTransitionStateAsync(claim.RecurringCallOccurrenceId, (int)RecurringCallOccurrenceStates.Creating,
							claim.PreviousState.Value, claim.UserId, DateTime.UtcNow, CancellationToken.None);
					else
						await _occurrencesRepository.DeleteIfStateAsync(claim.RecurringCallOccurrenceId, (int)RecurringCallOccurrenceStates.Creating, CancellationToken.None);

					await RaiseAlertAsync(series, occurrenceUtc, RecurringCallAlertKinds.CreateFailed, null,
						new RecurringCallAlertDetail { Reason = RecurringCallAlertReasons.SaveFailed }, CancellationToken.None);
					return null;
				}

				saved = committed;
			}

			// Marked created only while this caller still holds the claim. A claim that outlived its lease may have been dropped by the
			// stale-claim recovery and taken by someone else meanwhile: then the call just saved is a second one for the occurrence
			// and is deleted again (unless the recovery found it and recorded it as the occurrence's call).
			if (!await _occurrencesRepository.TryCompleteClaimAsync(claim.RecurringCallOccurrenceId, saved.CallId, DateTime.UtcNow, CancellationToken.None))
			{
				var row = await _occurrencesRepository.GetByOccurrenceAsync(series.RecurringCallId, DateTime.SpecifyKind(occurrenceUtc, DateTimeKind.Utc));
				var kept = row != null
					? row.CallId == saved.CallId
					: await _occurrencesRepository.TryClaimAsync(new RecurringCallOccurrence
					{
						RecurringCallOccurrenceId = Guid.NewGuid().ToString(),
						RecurringCallId = series.RecurringCallId,
						DepartmentId = series.DepartmentId,
						OccurrenceOn = occurrenceUtc,
						State = (int)RecurringCallOccurrenceStates.Created,
						CallId = saved.CallId,
						UserId = userId,
						UpdatedOn = DateTime.UtcNow
					}, CancellationToken.None);

				if (!kept)
				{
					await DeleteDuplicateCallAsync(saved, userId);
					return null;
				}
			}

			claim.State = (int)RecurringCallOccurrenceStates.Created;
			claim.CallId = saved.CallId;

			// Like a call entered on the New Call page: added now, sent at its dispatch time (or by the dispatcher).
			_eventAggregator.SendMessage<CallAddedEvent>(new CallAddedEvent { DepartmentId = saved.DepartmentId, Call = saved });

			await ResolveOccurrenceAlertsAsync(series, occurrenceUtc, RecurringCallAlertResolutions.CallCreated, userId, DateTime.UtcNow, CancellationToken.None,
				RecurringCallAlertKinds.CreateFailed, RecurringCallAlertKinds.Missed, RecurringCallAlertKinds.DispatchDue);

			RaiseOccurrenceEvent(series, occurrenceUtc, RecurringCallOccurrenceChanges.Created, saved, userId, !string.IsNullOrWhiteSpace(userId), null);

			return saved;
		}

		/// <summary>A call created for an occurrence that already has one (its creator lost the claim): deleted, never announced or sent.</summary>
		private async Task DeleteDuplicateCallAsync(Call call, string userId)
		{
			try
			{
				call.IsDeleted = true;
				call.DeletedOn = DateTime.UtcNow;
				call.DeletedByUserId = string.IsNullOrWhiteSpace(userId) ? SystemUserId : userId;
				call.DeletedReason = DuplicateReason;
				await _callsService.SaveCallAsync(call, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Could not delete call {call.CallId}, created twice for one recurring call occurrence.");
			}
		}

		private sealed class OccurrenceTakeOver
		{
			public RecurringCallOccurrence Claim { get; set; }
			public Call ExistingCall { get; set; }
		}

		/// <summary>
		/// A dispatcher acting on one occurrence: claims it when it has no row (or was skipped, which un-skips it), takes it over when
		/// it is awaiting a dispatcher or was missed, or returns the call it already has.
		/// </summary>
		private async Task<OccurrenceTakeOver> TakeOverOccurrenceAsync(RecurringCall series, DateTime occurrenceUtc, string userId, DateTime nowUtc,
			CancellationToken cancellationToken)
		{
			var existing = await _occurrencesRepository.GetByOccurrenceAsync(series.RecurringCallId, occurrenceUtc);

			if (existing != null)
			{
				switch ((RecurringCallOccurrenceStates)existing.State)
				{
					case RecurringCallOccurrenceStates.Created:
						if (existing.CallId.HasValue)
						{
							var call = await _callsService.GetCallByIdAsync(existing.CallId.Value);
							if (call != null && !call.IsDeleted && call.DepartmentId == series.DepartmentId)
								return new OccurrenceTakeOver { ExistingCall = call };
						}
						return new OccurrenceTakeOver();

					case RecurringCallOccurrenceStates.Skipped:
						if (!await _occurrencesRepository.DeleteIfStateAsync(existing.RecurringCallOccurrenceId, (int)RecurringCallOccurrenceStates.Skipped, cancellationToken))
							return new OccurrenceTakeOver();
						break;

					case RecurringCallOccurrenceStates.AwaitingDispatcher:
					case RecurringCallOccurrenceStates.Missed:
						if (!await _occurrencesRepository.TryTransitionStateAsync(existing.RecurringCallOccurrenceId, existing.State,
								(int)RecurringCallOccurrenceStates.Creating, userId, nowUtc, cancellationToken))
							return new OccurrenceTakeOver();

						existing.PreviousState = existing.State;
						existing.State = (int)RecurringCallOccurrenceStates.Creating;
						existing.UserId = userId;
						existing.UpdatedOn = nowUtc;
						return new OccurrenceTakeOver { Claim = existing };

					default:
						// Being created by someone else right now.
						return new OccurrenceTakeOver();
				}
			}

			var claim = new RecurringCallOccurrence
			{
				RecurringCallOccurrenceId = Guid.NewGuid().ToString(),
				RecurringCallId = series.RecurringCallId,
				DepartmentId = series.DepartmentId,
				OccurrenceOn = occurrenceUtc,
				State = (int)RecurringCallOccurrenceStates.Creating,
				UserId = userId,
				UpdatedOn = nowUtc
			};

			return await _occurrencesRepository.TryClaimAsync(claim, cancellationToken) ? new OccurrenceTakeOver { Claim = claim } : new OccurrenceTakeOver();
		}

		/// <summary>
		/// Claims left "creating" past their lease belong to a process that died. If its call was saved the occurrence is marked
		/// created; otherwise the claim is dropped so the occurrence is created again (if still due) or reported missed.
		/// </summary>
		private async Task ResolveStaleClaimsAsync(DateTime nowUtc, CancellationToken cancellationToken)
		{
			IEnumerable<RecurringCallOccurrence> stale;
			try
			{
				stale = await _occurrencesRepository.GetStaleClaimsAsync(nowUtc - ClaimLease) ?? Enumerable.Empty<RecurringCallOccurrence>();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return;
			}

			foreach (var claim in stale)
			{
				try
				{
					var claimedOn = DateTime.SpecifyKind(claim.UpdatedOn, DateTimeKind.Utc);
					var occurrenceUtc = DateTime.SpecifyKind(claim.OccurrenceOn, DateTimeKind.Utc);
					var call = await _recurringCallsRepository.GetCallForOccurrenceAsync(claim.RecurringCallId, occurrenceUtc, claimedOn.AddMinutes(-1));

					if (call != null)
					{
						// Only while it is still the stale claim: a creator that finished meanwhile has already recorded its call.
						if (await _occurrencesRepository.TryCompleteClaimAsync(claim.RecurringCallOccurrenceId, call.CallId, nowUtc, cancellationToken))
						{
							claim.State = (int)RecurringCallOccurrenceStates.Created;
							claim.CallId = call.CallId;
							claim.UpdatedOn = nowUtc;
						}
					}
					else
					{
						await _occurrencesRepository.DeleteIfStateAsync(claim.RecurringCallOccurrenceId, (int)RecurringCallOccurrenceStates.Creating, cancellationToken);
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}
		}

		#endregion Worker

		#region Alerts

		public async Task<List<RecurringCallAlertInfo>> GetAlertsAsync(int departmentId, string userId, DateTime? includeClosedSinceUtc = null,
			CancellationToken cancellationToken = default)
		{
			var alerts = (includeClosedSinceUtc.HasValue
					? await _alertsRepository.GetForDepartmentSinceAsync(departmentId, includeClosedSinceUtc.Value)
					: await _alertsRepository.GetOpenForDepartmentAsync(departmentId))
				?.Where(x => x.DepartmentId == departmentId).ToList() ?? new List<RecurringCallAlert>();

			if (!alerts.Any())
				return new List<RecurringCallAlertInfo>();

			// Settled ones (the held call went out, the occurrence was skipped) are closed before they are shown.
			await ReconcileAlertsAsync(alerts.Where(x => x.IsOpen).ToList(), DateTime.UtcNow, cancellationToken);
			if (!includeClosedSinceUtc.HasValue)
				alerts = alerts.Where(x => x.IsOpen).ToList();

			var schedules = new Dictionary<int, RecurringCall>();
			foreach (var id in alerts.Select(x => x.RecurringCallId).Distinct())
			{
				var series = await _recurringCallsRepository.GetByIdAsync(id);
				if (series != null && series.DepartmentId == departmentId)
					schedules[id] = series;
			}

			// Deleted schedules included: the soft-deleted row still names who its calls dispatch, so its remaining alerts (about held
			// or sent calls) follow the same dispatch scope as a live schedule's.
			var visibleIds = new HashSet<int>((await FilterSchedulesForUserAsync(departmentId, userId, schedules.Values.ToList()))
				.Select(x => x.RecurringCallId));
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			var units = (await _unitsService.GetUnitsForDepartmentAsync(departmentId) ?? new List<Unit>()).Where(x => x != null)
				.GroupBy(x => x.UnitId).ToDictionary(x => x.Key, x => x.First().Name);
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(departmentId) ?? new Dictionary<string, UserProfile>();

			var infos = new List<RecurringCallAlertInfo>();
			foreach (var alert in alerts.OrderByDescending(x => x.IsOpen).ThenBy(x => x.OccurrenceOn))
			{
				if (!schedules.TryGetValue(alert.RecurringCallId, out var series))
					continue;

				if (!visibleIds.Contains(series.RecurringCallId))
					continue;

				var detail = alert.GetDetail();
				var info = new RecurringCallAlertInfo
				{
					Alert = alert,
					ScheduleName = series.Name,
					DispatcherMode = series.IsDispatcherMode,
					ScheduleDeleted = series.IsDeleted,
					OccurrenceScheduled = !series.IsDeleted && IsOccurrence(series, department, DateTime.SpecifyKind(alert.OccurrenceOn, DateTimeKind.Utc)),
					Reason = detail.Reason,
					AcknowledgedByName = NameOf(profiles, alert.AcknowledgedByUserId),
					ResolvedByName = NameOf(profiles, alert.ResolvedByUserId)
				};

				foreach (var unitId in detail.UnavailableUnitIds ?? new List<int>())
					info.UnavailableUnits.Add(new KeyValuePair<int, string>(unitId, units.TryGetValue(unitId, out var name) ? name : unitId.ToString(CultureInfo.InvariantCulture)));

				foreach (var personId in detail.UnavailableUserIds ?? new List<string>())
					info.UnavailablePeople.Add(new KeyValuePair<string, string>(personId, NameOf(profiles, personId) ?? personId));

				if (alert.CallId.HasValue)
				{
					var call = await _callsService.GetCallByIdAsync(alert.CallId.Value);
					if (call != null && call.DepartmentId == departmentId)
					{
						info.CallNumber = call.Number;
						info.CallState = call.State;
						info.CallDispatched = call.State == (int)CallStates.Active && call.HasBeenDispatched == true && !call.DispatchClaimedOn.HasValue;
					}
				}

				infos.Add(info);
			}

			return infos;
		}

		public async Task<bool> AcknowledgeAlertAsync(int departmentId, string recurringCallAlertId, string userId, AuditOrigin origin = null,
			CancellationToken cancellationToken = default)
		{
			var alert = await _alertsRepository.GetByIdAsync(recurringCallAlertId);
			if (alert == null || alert.DepartmentId != departmentId || !alert.IsOpen)
				return false;

			if (!await _alertsRepository.TryAcknowledgeAsync(alert.RecurringCallAlertId, userId, DateTime.UtcNow, cancellationToken))
				return false;

			Audit(AuditLogTypes.RecurringCallAlertAcknowledged, departmentId, userId, null, AlertSnapshot(alert), origin);
			await PublishAlertsUpdatedAsync(alert, false);

			return true;
		}

		public async Task<bool> DismissAlertAsync(int departmentId, string recurringCallAlertId, string userId, string note, AuditOrigin origin = null,
			CancellationToken cancellationToken = default)
		{
			var alert = await _alertsRepository.GetByIdAsync(recurringCallAlertId);
			if (alert == null || alert.DepartmentId != departmentId || !alert.IsOpen)
				return false;

			var series = await _recurringCallsRepository.GetByIdAsync(alert.RecurringCallId);
			var closed = await ResolveAlertAsync(alert, series, RecurringCallAlertResolutions.Dismissed, userId,
				string.IsNullOrWhiteSpace(note) ? null : Truncate(note.Trim(), 1000), DateTime.UtcNow, cancellationToken);

			if (closed)
				Audit(AuditLogTypes.RecurringCallAlertDismissed, departmentId, userId, null, AlertSnapshot(alert), origin);

			return closed;
		}

		/// <summary>
		/// Raises an alert (once per occurrence and kind): records it, tells the schedule's alert recipients, pushes it to dispatch
		/// clients and raises the workflow trigger.
		/// </summary>
		private async Task<RecurringCallAlert> RaiseAlertAsync(RecurringCall series, DateTime occurrenceUtc, RecurringCallAlertKinds kind, int? callId,
			RecurringCallAlertDetail detail, CancellationToken cancellationToken)
		{
			try
			{
				var alert = new RecurringCallAlert
				{
					RecurringCallAlertId = Guid.NewGuid().ToString(),
					DepartmentId = series.DepartmentId,
					RecurringCallId = series.RecurringCallId,
					OccurrenceOn = DateTime.SpecifyKind(occurrenceUtc, DateTimeKind.Utc),
					Kind = (int)kind,
					CallId = callId,
					CreatedOn = DateTime.UtcNow
				};
				alert.SetDetail(detail);

				if (!await _alertsRepository.TryRaiseAsync(alert, cancellationToken))
					return null;

				_eventAggregator.SendMessage<RecurringCallAlertEvent>(new RecurringCallAlertEvent
				{
					DepartmentId = series.DepartmentId,
					Raised = true,
					AlertId = alert.RecurringCallAlertId,
					RecurringCallId = series.RecurringCallId,
					Name = series.Name,
					Kind = alert.Kind,
					KindName = kind.ToString(),
					OccurrenceOn = alert.OccurrenceOn,
					CallId = callId,
					Reason = detail?.Reason,
					UnavailableUnitCount = detail?.UnavailableUnitIds?.Count ?? 0,
					UnavailablePersonnelCount = detail?.UnavailableUserIds?.Count ?? 0
				});

				await PublishAlertsUpdatedAsync(alert, true);

				try
				{
					if (await NotifyAsync(series, alert, kind, detail))
						await _alertsRepository.MarkNotifiedAsync(alert.RecurringCallAlertId, DateTime.UtcNow, CancellationToken.None);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Could not notify the alert recipients of recurring call schedule {series.RecurringCallId}.");
				}

				return alert;
			}
			catch (Exception ex)
			{
				// An alert that cannot be stored must not undo what the worker did; it is logged and the next pass raises it again.
				Logging.LogException(ex, $"Could not raise a {kind} alert for recurring call schedule {series.RecurringCallId}.");
				return null;
			}
		}

		private async Task ResolveOccurrenceAlertsAsync(RecurringCall series, DateTime occurrenceUtc, RecurringCallAlertResolutions resolution, string userId,
			DateTime nowUtc, CancellationToken cancellationToken, params RecurringCallAlertKinds[] kinds)
		{
			try
			{
				var open = await _alertsRepository.GetOpenForOccurrenceAsync(series.RecurringCallId, occurrenceUtc) ?? Enumerable.Empty<RecurringCallAlert>();

				foreach (var alert in open.Where(x => kinds == null || kinds.Length == 0 || kinds.Contains((RecurringCallAlertKinds)x.Kind)))
					await ResolveAlertAsync(alert, series, resolution, userId, null, nowUtc, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		private async Task<bool> ResolveAlertAsync(RecurringCallAlert alert, RecurringCall series, RecurringCallAlertResolutions resolution, string userId, string note,
			DateTime nowUtc, CancellationToken cancellationToken)
		{
			if (!await _alertsRepository.TryResolveAsync(alert.RecurringCallAlertId, (int)resolution, userId, note, nowUtc, cancellationToken))
				return false;

			alert.ResolvedOn = nowUtc;
			alert.ResolvedByUserId = userId;
			alert.Resolution = (int)resolution;
			alert.ResolutionNote = note;

			_eventAggregator.SendMessage<RecurringCallAlertEvent>(new RecurringCallAlertEvent
			{
				DepartmentId = alert.DepartmentId,
				Raised = false,
				AlertId = alert.RecurringCallAlertId,
				RecurringCallId = alert.RecurringCallId,
				Name = series?.Name,
				Kind = alert.Kind,
				KindName = ((RecurringCallAlertKinds)alert.Kind).ToString(),
				OccurrenceOn = DateTime.SpecifyKind(alert.OccurrenceOn, DateTimeKind.Utc),
				CallId = alert.CallId,
				Reason = alert.GetDetail().Reason,
				Resolution = resolution.ToString(),
				UserId = userId
			});

			await PublishAlertsUpdatedAsync(alert, false);
			return true;
		}

		/// <summary>Closes open alerts whose cause is settled: the call went out or was closed, the occurrence was created or skipped, or it went stale.</summary>
		private async Task ReconcileAlertsAsync(IEnumerable<RecurringCallAlert> open, DateTime nowUtc, CancellationToken cancellationToken)
		{
			var schedules = new Dictionary<int, RecurringCall>();

			foreach (var alert in open.Where(x => x != null && x.IsOpen).ToList())
			{
				try
				{
					if (!schedules.TryGetValue(alert.RecurringCallId, out var series))
						schedules[alert.RecurringCallId] = series = await _recurringCallsRepository.GetByIdAsync(alert.RecurringCallId);

					var createdOn = DateTime.SpecifyKind(alert.CreatedOn, DateTimeKind.Utc);
					var kind = (RecurringCallAlertKinds)alert.Kind;

					if (alert.CallId.HasValue)
					{
						var call = await _callsService.GetCallByIdAsync(alert.CallId.Value);

						if (call == null || call.IsDeleted || (call.State != (int)CallStates.Active && call.State != (int)CallStates.Pending))
							await ResolveAlertAsync(alert, series, RecurringCallAlertResolutions.CallClosed, null, null, nowUtc, cancellationToken);
						else if (kind == RecurringCallAlertKinds.ResourcesUnavailable)
						{
							if (createdOn < nowUtc - UnavailableAlertLifetime)
								await ResolveAlertAsync(alert, series, RecurringCallAlertResolutions.Expired, null, null, nowUtc, cancellationToken);
						}
						else if (call.State == (int)CallStates.Active && call.HasBeenDispatched == true && !call.DispatchClaimedOn.HasValue)
						{
							// The held call went out (a dispatch claim still held means it is in flight and may yet be handed back). Only the
							// alert is closed here: whoever sent it raises the occurrence's Dispatched trigger (OnCallDispatchedAsync or the
							// recurring dispatch), so a workflow never runs twice for one dispatch.
							await ResolveAlertAsync(alert, series, RecurringCallAlertResolutions.Dispatched, null, null, nowUtc, cancellationToken);
						}

						continue;
					}

					if (series == null || series.IsDeleted)
					{
						await ResolveAlertAsync(alert, series, RecurringCallAlertResolutions.ScheduleDeleted, null, null, nowUtc, cancellationToken);
						continue;
					}

					var row = await _occurrencesRepository.GetByOccurrenceAsync(alert.RecurringCallId, DateTime.SpecifyKind(alert.OccurrenceOn, DateTimeKind.Utc));
					if (row?.State == (int)RecurringCallOccurrenceStates.Created)
						await ResolveAlertAsync(alert, series, RecurringCallAlertResolutions.CallCreated, null, null, nowUtc, cancellationToken);
					else if (row?.State == (int)RecurringCallOccurrenceStates.Skipped)
						await ResolveAlertAsync(alert, series, RecurringCallAlertResolutions.Skipped, null, null, nowUtc, cancellationToken);
					else if (createdOn < nowUtc.AddHours(-RecurringCallRecurrence.DispatcherAlertExpiryHours))
						await ResolveAlertAsync(alert, series, RecurringCallAlertResolutions.Expired, null, null, nowUtc, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Could not reconcile recurring call alert {alert.RecurringCallAlertId}.");
				}
			}
		}

		/// <summary>Sends the alert to the schedule's alert recipients (members, groups, roles; else its owner). False when nobody was reached.</summary>
		private async Task<bool> NotifyAsync(RecurringCall series, RecurringCallAlert alert, RecurringCallAlertKinds kind, RecurringCallAlertDetail detail)
		{
			var recipients = await ResolveAlertRecipientsAsync(series);
			if (!recipients.Any())
				return false;

			var department = await _departmentsService.GetDepartmentByIdAsync(series.DepartmentId, false);
			var departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(series.DepartmentId);
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(series.DepartmentId) ?? new Dictionary<string, UserProfile>();
			var units = detail?.UnavailableUnitIds?.Any() == true
				? (await _unitsService.GetUnitsForDepartmentAsync(series.DepartmentId) ?? new List<Unit>()).Where(x => x != null).GroupBy(x => x.UnitId).ToDictionary(x => x.Key, x => x.First().Name)
				: new Dictionary<int, string>();

			var when = RecurringCallRecurrence.ToLocal(DateTime.SpecifyKind(alert.OccurrenceOn, DateTimeKind.Utc), department)
				.ToString("ddd MMM d HH:mm", CultureInfo.InvariantCulture);
			var title = Truncate($"Recurring call: {series.Name}", 100);
			var body = AlertMessage(kind, when, detail, units, profiles);
			var sentAny = false;

			foreach (var userId in recipients)
			{
				try
				{
					profiles.TryGetValue(userId, out var profile);
					if (await _communicationService.SendNotificationAsync(userId, series.DepartmentId, body, departmentNumber, department, title, profile, false,
							AlertEventCodePrefix + alert.RecurringCallAlertId))
						sentAny = true;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			return sentAny;
		}

		/// <summary>The alert text sent to recipients (English, like other outbound dispatch text).</summary>
		public static string AlertMessage(RecurringCallAlertKinds kind, string when, RecurringCallAlertDetail detail, IReadOnlyDictionary<int, string> unitNames,
			IReadOnlyDictionary<string, UserProfile> profiles)
		{
			var names = new List<string>();
			foreach (var unitId in detail?.UnavailableUnitIds ?? new List<int>())
				names.Add(unitNames != null && unitNames.TryGetValue(unitId, out var unitName) ? unitName : $"unit {unitId}");
			foreach (var userId in detail?.UnavailableUserIds ?? new List<string>())
				names.Add(NameOf(profiles, userId) ?? "a member");

			var who = names.Any() ? string.Join(", ", names) : null;

			switch (kind)
			{
				case RecurringCallAlertKinds.CreateFailed:
					return $"The call for {when} could not be created. It will be tried again; check the schedule or create it by hand.";
				case RecurringCallAlertKinds.Missed:
					return $"The call for {when} was not created in time. Send it now or skip it.";
				case RecurringCallAlertKinds.NoResources:
					return detail?.Reason == RecurringCallAlertReasons.NoneAvailable && who != null
						? $"Nobody could be sent for {when}: {who} not available. The call is held as pending; choose resources and dispatch it."
						: $"Nobody could be sent for {when}: no resources are chosen and no run card matched. The call is held as pending; choose resources and dispatch it.";
				case RecurringCallAlertKinds.ResourcesUnavailable:
					return $"The call for {when} went out without {who ?? "some chosen resources"} (not available). Add or replace resources if needed.";
				case RecurringCallAlertKinds.DispatchDue:
					return $"It is time to send the call for {when}.";
				default:
					return $"The call for {when} needs a dispatcher.";
			}
		}

		private async Task<List<string>> ResolveAlertRecipientsAsync(RecurringCall series)
		{
			var members = await _departmentsService.GetAllMembersForDepartmentAsync(series.DepartmentId) ?? new List<DepartmentMember>();
			var active = new HashSet<string>(members.Where(x => x != null && !x.IsDeleted && !x.IsDisabled.GetValueOrDefault()).Select(x => x.UserId),
				StringComparer.OrdinalIgnoreCase);
			var ids = new HashSet<string>(series.GetAlertUserIds(), StringComparer.OrdinalIgnoreCase);

			if (series.GetAlertGroupIds().Any())
			{
				var groups = new HashSet<int>((await _departmentGroupsService.GetAllGroupsForDepartmentAsync(series.DepartmentId) ?? new List<DepartmentGroup>())
					.Where(x => x != null).Select(x => x.DepartmentGroupId));

				foreach (var groupId in series.GetAlertGroupIds().Where(groups.Contains))
					foreach (var member in await _departmentGroupsService.GetAllMembersForGroupAsync(groupId) ?? new List<DepartmentGroupMember>())
						if (member != null) ids.Add(member.UserId);
			}

			if (series.GetAlertRoleIds().Any())
			{
				var roles = new HashSet<int>((await _personnelRolesService.GetRolesForDepartmentAsync(series.DepartmentId) ?? new List<PersonnelRole>())
					.Where(x => x != null).Select(x => x.PersonnelRoleId));

				foreach (var roleId in series.GetAlertRoleIds().Where(roles.Contains))
					foreach (var member in await _personnelRolesService.GetAllMembersOfRoleAsync(roleId) ?? new List<PersonnelRoleUser>())
						if (member != null) ids.Add(member.UserId);
			}

			// Nobody named at all: the schedule's owner. A group or role that is named but empty right now reaches nobody, as chosen.
			if (!series.HasAlertRecipients && !string.IsNullOrWhiteSpace(series.OwnerUserId))
				ids.Add(series.OwnerUserId);

			return ids.Where(x => !string.IsNullOrWhiteSpace(x) && active.Contains(x)).ToList();
		}

		/// <summary>
		/// Tells connected dispatch clients to reload their alerts. Its relay is an async listener, so it goes through
		/// <see cref="IEventAggregator.SendMessageAsync{TMessage}"/>; the alert is already saved, so a failed push is only logged
		/// (clients still pick it up on their next refresh).
		/// </summary>
		private async Task PublishAlertsUpdatedAsync(RecurringCallAlert alert, bool raised)
		{
			try
			{
				await _eventAggregator.SendMessageAsync(new RecurringCallAlertsUpdatedEvent
				{
					DepartmentId = alert.DepartmentId,
					RecurringCallId = alert.RecurringCallId,
					Payload = JsonConvert.SerializeObject(new { AlertId = alert.RecurringCallAlertId, alert.Kind, Raised = raised }, CamelCase)
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Recurring call alert update could not be published for department {alert.DepartmentId}.");
			}
		}

		#endregion Alerts

		#region Calendar

		public async Task<List<ScheduledCallCalendarEntry>> GetCalendarAsync(int departmentId, string userId, DateTime fromUtc, DateTime toUtc)
		{
			var entries = new List<ScheduledCallCalendarEntry>();

			if (toUtc <= fromUtc || !RecurringCallRecurrence.IsSupportedInstant(fromUtc) || !RecurringCallRecurrence.IsSupportedInstant(toUtc))
				return entries;

			if ((toUtc - fromUtc).TotalDays > RecurringCallRecurrence.MaxRangeDays)
				toUtc = fromUtc.AddDays(RecurringCallRecurrence.MaxRangeDays);

			var now = DateTime.UtcNow;
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			// Calls: scheduled ones still waiting, and every call a schedule created. Trimmed to the member's dispatch scope as the
			// Scheduled Calls list is.
			var calls = (await _recurringCallsRepository.GetScheduledCallsInRangeAsync(departmentId, fromUtc, toUtc) ?? Enumerable.Empty<Call>())
				.Where(x => x.DepartmentId == departmentId && !x.IsDeleted && x.DispatchOn.HasValue).ToList();
			calls = await _dispatchScopeService.FilterCallsForUserAsync(departmentId, userId, calls) ?? new List<Call>();

			foreach (var call in calls)
			{
				ScheduledCallCalendarEntryKinds kind;
				if (call.HasBeenDispatched.GetValueOrDefault() && call.State != (int)CallStates.Pending)
					kind = ScheduledCallCalendarEntryKinds.Dispatched;
				else if (call.State == (int)CallStates.Active)
					kind = ScheduledCallCalendarEntryKinds.Scheduled;
				else if (call.State == (int)CallStates.Pending)
					kind = ScheduledCallCalendarEntryKinds.Held;
				else
					kind = ScheduledCallCalendarEntryKinds.Cancelled;

				entries.Add(new ScheduledCallCalendarEntry
				{
					Kind = kind,
					DispatchOnUtc = DateTime.SpecifyKind(call.DispatchOn.Value, DateTimeKind.Utc),
					Name = call.Name,
					Priority = call.Priority,
					Address = call.Address,
					CallId = call.CallId,
					CallNumber = call.Number,
					RecurringCallId = call.RecurringCallId
				});
			}

			// Occurrences of running schedules still to come, skipped, awaiting a dispatcher or missed.
			var schedules = (await GetAllForDepartmentAsync(departmentId)).ToList();
			if (!schedules.Any())
				return entries.OrderBy(x => x.DispatchOnUtc).ToList();

			var visible = await FilterSchedulesForUserAsync(departmentId, userId, schedules);
			var dealtWith = (await _occurrencesRepository.GetForDepartmentInRangeAsync(departmentId, fromUtc, toUtc) ?? Enumerable.Empty<RecurringCallOccurrence>())
				.GroupBy(x => x.RecurringCallId)
				.ToDictionary(x => x.Key, x => x.GroupBy(o => DateTime.SpecifyKind(o.OccurrenceOn, DateTimeKind.Utc)).ToDictionary(o => o.Key, o => o.First()));

			// A call a dispatcher sent early or late sits at its own time; tag it with the occurrence it stands for.
			// Its occurrence can fall outside this range (an 08:00 Monday occurrence sent on Sunday), so one not among this range's
			// rows is looked up by the call.
			var byCall = dealtWith.Values.SelectMany(x => x.Values).Where(x => x.CallId.HasValue).GroupBy(x => x.CallId.Value).ToDictionary(x => x.Key, x => x.First());
			foreach (var entry in entries.Where(x => x.CallId.HasValue && x.RecurringCallId.HasValue))
			{
				if (!byCall.TryGetValue(entry.CallId.Value, out var row))
					row = await _occurrencesRepository.GetByCallAsync(entry.RecurringCallId.Value, entry.CallId.Value);

				if (row != null)
					entry.OccurrenceOnUtc = DateTime.SpecifyKind(row.OccurrenceOn, DateTimeKind.Utc);
			}

			foreach (var series in visible)
			{
				dealtWith.TryGetValue(series.RecurringCallId, out var rows);
				var projectFrom = fromUtc > now ? fromUtc : now;

				foreach (var occurrence in RecurringCallRecurrence.UtcOccurrences(series, department, fromUtc, toUtc))
				{
					RecurringCallOccurrence row = null;
					rows?.TryGetValue(occurrence, out row);

					if (row != null)
					{
						// A created call is already on the calendar as itself (unless deleted, when it is gone for good).
						if (row.State == (int)RecurringCallOccurrenceStates.Skipped)
							entries.Add(ProjectedEntry(series, occurrence, ScheduledCallCalendarEntryKinds.Skipped));
						else if (row.State == (int)RecurringCallOccurrenceStates.AwaitingDispatcher)
							entries.Add(ProjectedEntry(series, occurrence, ScheduledCallCalendarEntryKinds.Due));
						else if (row.State == (int)RecurringCallOccurrenceStates.Missed)
							entries.Add(ProjectedEntry(series, occurrence, ScheduledCallCalendarEntryKinds.Missed));

						continue;
					}

					// Only what is still to come, while the schedule runs: an occurrence never created in the past (the schedule did
					// not exist, or it was paused) is not shown as if it were.
					if (series.IsActive && occurrence >= projectFrom && occurrence >= series.EffectiveFrom)
						entries.Add(ProjectedEntry(series, occurrence, ScheduledCallCalendarEntryKinds.Projected));
				}
			}

			return entries.OrderBy(x => x.DispatchOnUtc).ToList();
		}

		#endregion Calendar

		#region Helpers

		/// <summary>The call an occurrence of a schedule creates, before it is saved. Recipients not in the given sets are left off.</summary>
		public static Call BuildCall(RecurringCall series, DateTime dispatchOnUtc, DateTime nowUtc, string reportingUserId, RecurringCallDispatchTargets targets, bool destinationValid)
		{
			var call = new Call
			{
				DepartmentId = series.DepartmentId,
				Name = series.Name,
				NatureOfCall = series.NatureOfCall,
				Notes = CallNotes(series),
				Address = series.Address,
				GeoLocationData = series.GeoLocationData,
				Priority = series.Priority,
				Type = string.IsNullOrWhiteSpace(series.Type) ? null : series.Type,
				ContactName = series.ContactName,
				ContactNumber = series.ContactNumber,
				ExternalIdentifier = series.ExternalIdentifier,
				ReferenceNumber = series.ReferenceNumber,
				IncidentNumber = series.IncidentNumber,
				MapPage = series.MapPage,
				DestinationPoiId = destinationValid ? series.DestinationPoiId : null,
				CheckInTimersEnabled = series.CheckInTimersEnabled,
				ReportingUserId = reportingUserId,
				LoggedOn = nowUtc,
				State = (int)CallStates.Active,
				DispatchOn = DateTime.SpecifyKind(dispatchOnUtc, DateTimeKind.Utc),
				HasBeenDispatched = false,
				CallSource = (int)CallSources.User,
				RecurringCallId = series.RecurringCallId,
				Dispatches = new Collection<CallDispatch>(),
				GroupDispatches = new List<CallDispatchGroup>(),
				UnitDispatches = new List<CallDispatchUnit>(),
				RoleDispatches = new List<CallDispatchRole>(),
				Protocols = new List<CallProtocol>(),
				References = new List<CallReference>(),
				Contacts = new List<CallContact>()
			};

			targets ??= new RecurringCallDispatchTargets();

			foreach (var userId in series.GetDispatchUserIds().Where(x => targets.UserIds.Contains(x)))
				call.Dispatches.Add(new CallDispatch { UserId = userId });

			foreach (var id in series.GetDispatchGroupIds().Where(targets.GroupIds.Contains))
				call.GroupDispatches.Add(new CallDispatchGroup { DepartmentGroupId = id });

			foreach (var id in series.GetDispatchUnitIds().Where(targets.UnitIds.Contains))
				call.UnitDispatches.Add(new CallDispatchUnit { UnitId = id });

			foreach (var id in series.GetDispatchRoleIds().Where(targets.RoleIds.Contains))
				call.RoleDispatches.Add(new CallDispatchRole { RoleId = id });

			return call;
		}

		/// <summary>The call's notes: the schedule's notes, then its pickup facility and destination lines, so the crew has them on every call.</summary>
		public static string CallNotes(RecurringCall series)
		{
			var lines = new List<string>();

			if (!string.IsNullOrWhiteSpace(series.Notes))
				lines.Add(series.Notes.Trim());

			var pickup = JoinParts(series.FacilityName, series.FacilityDetails);
			if (pickup != null)
				lines.Add("Pickup: " + pickup);

			var destination = JoinParts(series.DestinationName, series.DestinationDetails);
			if (destination != null)
				lines.Add("Destination: " + destination);

			return lines.Any() ? string.Join(Environment.NewLine, lines) : null;
		}

		private static string JoinParts(string name, string details)
		{
			var parts = new[] { name, details }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
			return parts.Any() ? string.Join(" - ", parts) : null;
		}

		/// <summary>A chosen pickup facility fills the facility name and the address when they are blank.</summary>
		public async Task ApplyFacilityDefaultsAsync(RecurringCall series)
		{
			if (!series.FacilityPoiId.HasValue || series.FacilityPoiId.Value <= 0)
			{
				series.FacilityPoiId = null;
				return;
			}

			var poi = (await _mappingService.GetPOIsForDepartmentAsync(series.DepartmentId) ?? new List<Poi>()).FirstOrDefault(x => x != null && x.PoiId == series.FacilityPoiId.Value);
			if (poi == null)
			{
				series.FacilityPoiId = null;
				return;
			}

			if (string.IsNullOrWhiteSpace(series.FacilityName))
				series.FacilityName = Truncate(poi.Name, RecurringCall.MaxShortFieldLength);

			if (string.IsNullOrWhiteSpace(series.Address))
			{
				series.Address = poi.Address;
				if (string.IsNullOrWhiteSpace(series.GeoLocationData) && !(poi.Latitude == 0 && poi.Longitude == 0))
					series.GeoLocationData = string.Format(CultureInfo.InvariantCulture, "{0},{1}", poi.Latitude, poi.Longitude);
			}
		}

		private static ScheduledCallCalendarEntry ProjectedEntry(RecurringCall series, DateTime occurrenceUtc, ScheduledCallCalendarEntryKinds kind)
		{
			return new ScheduledCallCalendarEntry
			{
				Kind = kind,
				DispatchOnUtc = occurrenceUtc,
				OccurrenceOnUtc = occurrenceUtc,
				Name = series.Name,
				Priority = series.Priority,
				Address = series.Address,
				RecurringCallId = series.RecurringCallId
			};
		}

		/// <summary>A schedule is in a member's scope when the calls it creates would be (see the dispatch scope rules).</summary>
		private async Task<List<RecurringCall>> FilterSchedulesForUserAsync(int departmentId, string userId, List<RecurringCall> schedules)
		{
			if (schedules == null || !schedules.Any())
				return new List<RecurringCall>();

			// Stand-in calls carrying the schedule's recipients. Their dispatch lists are set (never null), so the scope check
			// reads them instead of loading dispatches for a call that does not exist.
			var standIns = schedules.Select(series => BuildCall(series, DateTime.UtcNow, DateTime.UtcNow, series.OwnerUserId,
				RecurringCallDispatchTargets.Everything(series), true)).ToList();
			var visible = await _dispatchScopeService.FilterCallsForUserAsync(departmentId, userId, standIns) ?? new List<Call>();
			var ids = new HashSet<int>(visible.Select(x => x.RecurringCallId ?? 0));

			return schedules.Where(x => ids.Contains(x.RecurringCallId)).ToList();
		}

		private async Task<bool> IsOccurrenceAsync(RecurringCall series, DateTime occurrenceUtc)
		{
			return IsOccurrence(series, await _departmentsService.GetDepartmentByIdAsync(series.DepartmentId, false), occurrenceUtc);
		}

		private static bool IsOccurrence(RecurringCall series, Department department, DateTime occurrenceUtc)
		{
			if (!RecurringCallRecurrence.IsSupportedInstant(occurrenceUtc))
				return false;

			var match = RecurringCallRecurrence.UtcOccurrences(series, department, occurrenceUtc.AddMinutes(-1), occurrenceUtc.AddMinutes(1));

			return match.Contains(DateTime.SpecifyKind(occurrenceUtc, DateTimeKind.Utc));
		}

		private static bool IsWaitingToGoOut(Call call)
		{
			return call != null && !call.IsDeleted && call.State == (int)CallStates.Active && !call.HasBeenDispatched.GetValueOrDefault() &&
				   call.DispatchOn.HasValue && call.DispatchOn.Value > DateTime.UtcNow;
		}

		private static bool IsHeld(Call call)
		{
			return call != null && !call.IsDeleted && call.State == (int)CallStates.Pending;
		}

		/// <summary>Deletes the calls a schedule created that have not gone out, and forgets their occurrences so they are created again.</summary>
		private async Task<int> DeleteUpcomingCallsAsync(RecurringCall series, string userId, string reason, DateTime nowUtc, CancellationToken cancellationToken)
		{
			var deleted = 0;
			var calls = await _recurringCallsRepository.GetCallsForScheduleAsync(series.DepartmentId, series.RecurringCallId, nowUtc) ?? Enumerable.Empty<Call>();

			foreach (var call in calls.Where(IsWaitingToGoOut))
			{
				var fresh = await _callsService.GetCallByIdAsync(call.CallId);
				if (!IsWaitingToGoOut(fresh))
					continue;

				// Taken off the dispatch path first, with the same claim the worker and Dispatch Now take: only a call still unsent and
				// unclaimed in the database is deleted, and one the worker or a dispatcher is sending right now is left alone. The
				// claim then stays on the deleted call, which nothing dispatches.
				var claimedOn = await _callsService.TryClaimCallForDispatchAsync(fresh.CallId, fresh.DepartmentId, cancellationToken);
				if (!claimedOn.HasValue)
					continue;

				fresh.IsDeleted = true;
				fresh.DeletedOn = nowUtc;
				fresh.DeletedByUserId = userId;
				fresh.DeletedReason = reason;
				fresh.HasBeenDispatched = false;

				try
				{
					await _callsService.SaveCallAsync(fresh, CancellationToken.None);
				}
				catch
				{
					// Not deleted: handed back as it was, so it still goes out at its time.
					await _callsService.ReleaseCallDispatchClaimAsync(fresh.CallId, fresh.DepartmentId, claimedOn.Value, (int)CallStates.Active, fresh.DispatchOn, false,
						CancellationToken.None);
					throw;
				}
				_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent { DepartmentId = fresh.DepartmentId, Call = fresh });
				deleted++;

				var occurrence = await _occurrencesRepository.GetByCallAsync(series.RecurringCallId, fresh.CallId)
								 ?? await _occurrencesRepository.GetByOccurrenceAsync(series.RecurringCallId, DateTime.SpecifyKind(fresh.DispatchOn.Value, DateTimeKind.Utc));
				if (occurrence != null && occurrence.CallId == fresh.CallId)
					await _occurrencesRepository.DeleteIfStateAsync(occurrence.RecurringCallOccurrenceId, (int)RecurringCallOccurrenceStates.Created, cancellationToken);
			}

			return deleted;
		}

		/// <summary>Who can still be dispatched in the department: active members, existing groups and roles, current units.</summary>
		private async Task<RecurringCallDispatchTargets> LoadDispatchTargetsAsync(int departmentId)
		{
			var members = await _departmentsService.GetAllMembersForDepartmentAsync(departmentId) ?? new List<DepartmentMember>();
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>();
			var units = await _unitsService.GetUnitsForDepartmentAsync(departmentId) ?? new List<Unit>();
			var roles = await _personnelRolesService.GetRolesForDepartmentAsync(departmentId) ?? new List<PersonnelRole>();

			return new RecurringCallDispatchTargets
			{
				UserIds = new HashSet<string>(members.Where(x => x != null && !x.IsDeleted && !x.IsDisabled.GetValueOrDefault()).Select(x => x.UserId),
					StringComparer.OrdinalIgnoreCase),
				GroupIds = new HashSet<int>(groups.Where(x => x != null).Select(x => x.DepartmentGroupId)),
				UnitIds = new HashSet<int>(units.Where(x => x != null && !x.IsDeleted).Select(x => x.UnitId)),
				RoleIds = new HashSet<int>(roles.Where(x => x != null).Select(x => x.PersonnelRoleId))
			};
		}

		private async Task RaiseScheduleEventAsync(RecurringCall series, RecurringCallScheduleChanges change, string userId, DateTime nowUtc)
		{
			DateTime? next = null;
			try
			{
				if (series.IsActive && !series.IsDeleted)
				{
					var department = await _departmentsService.GetDepartmentByIdAsync(series.DepartmentId, false);
					next = RecurringCallRecurrence.NextOccurrenceUtc(series, department, nowUtc);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			_eventAggregator.SendMessage<RecurringCallScheduleEvent>(new RecurringCallScheduleEvent
			{
				DepartmentId = series.DepartmentId,
				Change = change,
				RecurringCallId = series.RecurringCallId,
				Name = series.Name,
				NatureOfCall = series.NatureOfCall,
				Address = series.Address,
				FacilityName = series.FacilityName,
				DestinationName = series.DestinationName,
				ScheduleContactName = series.ScheduleContactName,
				ScheduleContactNumber = series.ScheduleContactNumber,
				StartsOn = series.StartsOn,
				EndsOn = series.EndsOn,
				Times = series.Times,
				CreationMode = series.CreationMode,
				UseRunCard = series.UseRunCard,
				IsActive = series.IsActive,
				PauseReason = series.PauseReason,
				ResumeOn = series.ResumeOn,
				UserId = userId,
				NextOccurrenceOn = next
			});
		}

		private void RaiseOccurrenceEvent(RecurringCall series, DateTime occurrenceUtc, RecurringCallOccurrenceChanges change, Call call, string userId, bool byDispatcher,
			RecurringCallDispatchResult result)
		{
			_eventAggregator.SendMessage<RecurringCallOccurrenceEvent>(new RecurringCallOccurrenceEvent
			{
				DepartmentId = series.DepartmentId,
				Change = change,
				RecurringCallId = series.RecurringCallId,
				Name = series.Name,
				OccurrenceOn = DateTime.SpecifyKind(occurrenceUtc, DateTimeKind.Utc),
				CallId = call?.CallId,
				CallNumber = call?.Number,
				UserId = userId,
				ByDispatcher = byDispatcher,
				UnitCount = call?.UnitDispatches?.Count ?? 0,
				PersonnelCount = call?.Dispatches?.Count ?? 0,
				GroupCount = call?.GroupDispatches?.Count ?? 0,
				RoleCount = call?.RoleDispatches?.Count ?? 0,
				UnavailableCount = (result?.UnavailableUnitIds?.Count ?? 0) + (result?.UnavailableUserIds?.Count ?? 0),
				RunCardApplied = result?.RunCardApplied ?? false
			});
		}

		private void Audit(AuditLogTypes type, int departmentId, string userId, object before, object after, AuditOrigin origin)
		{
			try
			{
				_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
				{
					DepartmentId = departmentId,
					UserId = string.IsNullOrWhiteSpace(userId) ? SystemUserId : userId,
					Type = type,
					Before = before == null ? null : JsonConvert.SerializeObject(before is RecurringCall b ? Snapshot(b) : before),
					After = after == null ? null : JsonConvert.SerializeObject(after is RecurringCall a ? Snapshot(a) : after),
					Successful = true,
					IpAddress = origin?.IpAddress,
					UserAgent = origin?.UserAgent,
					ServerName = Environment.MachineName
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		/// <summary>What the audit log keeps of a schedule (its settings; no call history).</summary>
		public static object Snapshot(RecurringCall series)
		{
			if (series == null)
				return null;

			return new
			{
				series.RecurringCallId,
				series.Name,
				series.IsActive,
				series.Frequency,
				series.RepeatInterval,
				series.DaysOfWeek,
				series.MonthlyDay,
				series.MonthlyWeek,
				series.MonthlyWeekday,
				series.Times,
				StartsOn = series.StartsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				EndsOn = series.EndsOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				series.LeadTimeMinutes,
				series.CreationMode,
				series.ManualAlertMinutes,
				series.UseRunCard,
				series.NatureOfCall,
				series.Address,
				series.Priority,
				series.Type,
				series.FacilityName,
				series.FacilityPoiId,
				series.DestinationName,
				series.DestinationPoiId,
				series.ScheduleContactName,
				series.ScheduleContactNumber,
				series.ScheduleContactEmail,
				series.DispatchUserIds,
				series.DispatchGroupIds,
				series.DispatchUnitIds,
				series.DispatchRoleIds,
				series.AlertUserIds,
				series.AlertGroupIds,
				series.AlertRoleIds,
				series.PauseReason,
				ResumeOn = series.ResumeOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			};
		}

		private static object OccurrenceSnapshot(RecurringCall series, DateTime occurrenceUtc, Call call)
		{
			return new { series.RecurringCallId, series.Name, OccurrenceOn = DateTime.SpecifyKind(occurrenceUtc, DateTimeKind.Utc), CallId = call?.CallId, CallNumber = call?.Number };
		}

		private static object AlertSnapshot(RecurringCallAlert alert)
		{
			return new
			{
				alert.RecurringCallAlertId,
				alert.RecurringCallId,
				Kind = ((RecurringCallAlertKinds)alert.Kind).ToString(),
				OccurrenceOn = DateTime.SpecifyKind(alert.OccurrenceOn, DateTimeKind.Utc),
				alert.CallId
			};
		}

		private static string NameOf(IReadOnlyDictionary<string, UserProfile> profiles, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId) || profiles == null)
				return null;

			return profiles.TryGetValue(userId, out var profile) ? profile?.FullName?.AsFirstNameLastName : null;
		}

		private static List<T> NullIfEmpty<T>(List<T> list) => list != null && list.Any() ? list : null;

		private static string Truncate(string value, int max) => value == null || value.Length <= max ? value : value.Substring(0, max);

		#endregion Helpers
	}

	/// <summary>The recipients a schedule's calls may still dispatch (anyone else it names has left or been deleted).</summary>
	public class RecurringCallDispatchTargets
	{
		public HashSet<string> UserIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public HashSet<int> GroupIds { get; set; } = new HashSet<int>();
		public HashSet<int> UnitIds { get; set; } = new HashSet<int>();
		public HashSet<int> RoleIds { get; set; } = new HashSet<int>();

		/// <summary>Everyone a schedule names, unchecked.</summary>
		public static RecurringCallDispatchTargets Everything(RecurringCall series)
		{
			return new RecurringCallDispatchTargets
			{
				UserIds = new HashSet<string>(series.GetDispatchUserIds(), StringComparer.OrdinalIgnoreCase),
				GroupIds = new HashSet<int>(series.GetDispatchGroupIds()),
				UnitIds = new HashSet<int>(series.GetDispatchUnitIds()),
				RoleIds = new HashSet<int>(series.GetDispatchRoleIds())
			};
		}
	}
}
