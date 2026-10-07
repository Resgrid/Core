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

			// A call marked sent by a claim whose process died before the broadcast went out is still waiting: nobody was paged.
			return call.State == (int)CallStates.Active && call.DispatchOn.HasValue &&
				(call.HasBeenDispatched == false || CallDispatchClaims.IsAbandoned(call, DateTime.UtcNow));
		}

		public async Task<DispatchNowOutcome> DispatchNowAsync(Call call, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!IsWaitingForDispatch(call))
				return DispatchNowOutcome.NotWaiting;

			var wasPending = call.State == (int)CallStates.Pending;
			var original = new WaitingState(call);

			// The in-memory call can be stale: two dispatchers pressing Dispatch Now, or the scheduled-calls worker running
			// at the same moment, would each page everyone. Only the caller whose conditional write marks the stored call
			// dispatched goes on.
			var claimedOn = await _callsService.TryClaimCallForDispatchAsync(call.CallId, call.DepartmentId, cancellationToken);
			if (!claimedOn.HasValue)
				return DispatchNowOutcome.NotWaiting;

			call.DispatchClaimedOn = claimedOn;

			Call savedCall;
			CallQueueItem cqi;
			DispatchRecommendationResult recommendation = null;
			try
			{
				call.State = (int)CallStates.Active;
				call.DispatchOn = DateTime.UtcNow;
				call.HasBeenDispatched = true;

				// Run card auto-dispatch, as a newly entered call gets it: the call's location and resource picture are
				// final now. Only mutates the in-memory call; the single save below persists it.
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
					await ReleaseAsync(call, claimedOn.Value, original);
					return DispatchNowOutcome.NoRecipients;
				}

				cqi = new CallQueueItem { Call = call };

				// Groups, roles and units fan out to members the worker resolves itself, so it gets every profile; a
				// personnel-only dispatch needs just those people's.
				if (call.GroupDispatches?.Any() == true || call.RoleDispatches?.Any() == true || call.UnitDispatches?.Any() == true)
					cqi.Profiles = (await _userProfileService.GetAllProfilesForDepartmentAsync(call.DepartmentId)).Select(x => x.Value).ToList();
				else
					cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(call.Dispatches.Select(x => x.UserId).ToList());

				// Saved before the broadcast so the worker reads an active call; put back if the queue refuses it, so
				// the call is still waiting and the dispatcher can try again.
				savedCall = await _callsService.SaveCallAsync(call, cancellationToken);
			}
			catch
			{
				// Nothing went out: a claim left behind would make the call look sent until its lease ran out.
				await ReleaseAsync(call, claimedOn.Value, original);
				throw;
			}

			// The queue reports a refusal by throwing, not by returning false.
			bool queued;
			try
			{
				queued = await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				await ReleaseAsync(savedCall, claimedOn.Value, original);
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Could not queue the dispatch of call {savedCall.CallId}.");
				queued = false;
			}

			if (!queued)
			{
				await ReleaseAsync(savedCall, claimedOn.Value, original);
				return DispatchNowOutcome.QueueFailed;
			}

			// Sent: end the claim so its lease can never send the call again.
			await CompleteAsync(savedCall, claimedOn.Value);

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

		/// <summary>The waiting state a call is put back to when its dispatch does not go out.</summary>
		private readonly struct WaitingState
		{
			public WaitingState(Call call)
			{
				State = call.State;
				DispatchOn = call.DispatchOn;
				// A call taken over from an abandoned claim is stored as sent; waiting is null for pending, false for scheduled.
				HasBeenDispatched = call.State == (int)CallStates.Pending ? (bool?)null : false;
			}

			public int State { get; }
			public DateTime? DispatchOn { get; }
			public bool? HasBeenDispatched { get; }
		}

		/// <summary>
		/// Puts the call back to waiting, in memory and in the database. Only this claim is given back, and only while the call
		/// is still active or pending, so a close or edit that landed meanwhile is never overwritten with this snapshot.
		/// </summary>
		private async Task ReleaseAsync(Call call, DateTime claimedOn, WaitingState original)
		{
			call.State = original.State;
			call.DispatchOn = original.DispatchOn;
			call.HasBeenDispatched = original.HasBeenDispatched;
			call.DispatchClaimedOn = null;

			try
			{
				// Not tied to the request's cancellation: the claim has to be given back even when the caller went away. If
				// this fails too, the claim's lease runs out and the call can be dispatched again.
				await _callsService.ReleaseCallDispatchClaimAsync(call.CallId, call.DepartmentId, claimedOn, call.State, call.DispatchOn, call.HasBeenDispatched);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Could not release the dispatch claim on call {call.CallId}.");
			}
		}

		private async Task CompleteAsync(Call call, DateTime claimedOn)
		{
			call.DispatchClaimedOn = null;

			try
			{
				await _callsService.CompleteCallDispatchClaimAsync(call.CallId, call.DepartmentId, claimedOn);
			}
			catch (Exception ex)
			{
				// The broadcast is queued; an unfinished claim could send the call again once its lease ran out.
				Logging.LogException(ex, $"Could not complete the dispatch claim on call {call.CallId}.");
			}
		}
	}
}
