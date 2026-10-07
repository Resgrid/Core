using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class PendingCallsService : IPendingCallsService
	{
		private readonly ICallsService _callsService;
		private readonly IQueueService _queueService;
		private readonly IUserProfileService _userProfileService;
		private readonly ICallDispatchStatusService _callDispatchStatusService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IDispatchRecommendationService _dispatchRecommendationService;
		private readonly IEventAggregator _eventAggregator;

		public PendingCallsService(ICallsService callsService, IQueueService queueService, IUserProfileService userProfileService,
			ICallDispatchStatusService callDispatchStatusService, IFeatureToggleService featureToggleService,
			IDispatchRecommendationService dispatchRecommendationService, IEventAggregator eventAggregator)
		{
			_callsService = callsService;
			_queueService = queueService;
			_userProfileService = userProfileService;
			_callDispatchStatusService = callDispatchStatusService;
			_featureToggleService = featureToggleService;
			_dispatchRecommendationService = dispatchRecommendationService;
			_eventAggregator = eventAggregator;
		}

		public bool IsWaitingForDispatch(Call call)
		{
			if (call == null || call.IsDeleted)
				return false;

			if (call.State == (int)CallStates.Pending)
				return true;

			return call.State == (int)CallStates.Active && call.DispatchOn.HasValue && call.HasBeenDispatched == false;
		}

		public async Task<DispatchNowOutcome> DispatchNowAsync(Call call, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!IsWaitingForDispatch(call))
				return DispatchNowOutcome.NotWaiting;

			var wasPending = call.State == (int)CallStates.Pending;
			var originalDispatchOn = call.DispatchOn;

			call.State = (int)CallStates.Active;
			call.DispatchOn = DateTime.UtcNow;
			call.HasBeenDispatched = true;

			// Run card auto-dispatch, as a newly entered call gets it: the call's location and resource picture are
			// final now. Only mutates the in-memory call; the single save below persists it.
			DispatchRecommendationResult recommendation = null;
			try
			{
				if (await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, call.DepartmentId))
				{
					var enriched = await _dispatchRecommendationService.EnrichCallForDispatchAsync(call, 1, true, cancellationToken);

					if (enriched != null && enriched.MatchedRunCardId.HasValue && enriched.AutoDispatch && enriched.HasRecommendations)
						recommendation = enriched;
				}
			}
			catch (Exception ex)
			{
				// A recommendation failure must never block the dispatch.
				Logging.LogException(ex);
			}

			if (!HasRecipients(call))
			{
				RestoreWaiting(call, wasPending, originalDispatchOn);
				return DispatchNowOutcome.NoRecipients;
			}

			var cqi = new CallQueueItem { Call = call };

			// Groups, roles and units fan out to members the worker resolves itself, so it gets every profile; a
			// personnel-only dispatch needs just those people's.
			if (call.GroupDispatches?.Any() == true || call.RoleDispatches?.Any() == true || call.UnitDispatches?.Any() == true)
				cqi.Profiles = (await _userProfileService.GetAllProfilesForDepartmentAsync(call.DepartmentId)).Select(x => x.Value).ToList();
			else
				cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(call.Dispatches.Select(x => x.UserId).ToList());

			// Saved before the broadcast so the worker reads an active call; put back if the queue refuses it, so
			// the call is still waiting and the dispatcher can try again.
			var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);

			if (!await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken))
			{
				RestoreWaiting(savedCall, wasPending, originalDispatchOn);
				await _callsService.SaveCallAsync(savedCall, cancellationToken);
				return DispatchNowOutcome.QueueFailed;
			}

			if (recommendation != null)
			{
				try
				{
					await _dispatchRecommendationService.RecordActivationAsync(savedCall, recommendation, userId, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			if (savedCall.GroupDispatches?.Any() == true || savedCall.UnitDispatches?.Any() == true)
			{
				await _callDispatchStatusService.ApplyDispatchStatusesAsync(savedCall,
					savedCall.GroupDispatches?.Select(x => x.DepartmentGroupId),
					savedCall.UnitDispatches?.Select(x => x.UnitId),
					cancellationToken);
			}

			// A pending call was never announced, so it is "added" now (incident chat channel, Call Added
			// workflows, the apps' call lists). A scheduled call was announced when it was entered.
			if (wasPending)
				_eventAggregator.SendMessage<CallAddedEvent>(new CallAddedEvent() { DepartmentId = savedCall.DepartmentId, Call = savedCall });
			else
				_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent() { DepartmentId = savedCall.DepartmentId, Call = savedCall });

			return DispatchNowOutcome.Dispatched;
		}

		private static bool HasRecipients(Call call)
		{
			return call.Dispatches?.Any() == true || call.GroupDispatches?.Any() == true ||
				call.RoleDispatches?.Any() == true || call.UnitDispatches?.Any() == true;
		}

		private static void RestoreWaiting(Call call, bool wasPending, DateTime? originalDispatchOn)
		{
			call.State = wasPending ? (int)CallStates.Pending : (int)CallStates.Active;
			call.DispatchOn = originalDispatchOn;
			call.HasBeenDispatched = wasPending ? null : false;
		}
	}
}
