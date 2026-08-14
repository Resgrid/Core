using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo.Handlers;
using Quidjibo.Misc;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Workers.Console.Tasks
{
	public class DispatchScheduledCallsTask : IQuidjiboHandler<DispatchScheduledCallsCommand>
	{
		public string Name => "Dispatch Scheduled Calls";
		public int Priority => 1;
		public ILogger _logger;

		public DispatchScheduledCallsTask(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessAsync(DispatchScheduledCallsCommand command, IQuidjiboProgress progress, CancellationToken cancellationToken)
		{
			try
			{
				progress.Report(1, $"Starting the {Name} Task");

				var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
				var callsService = Bootstrapper.GetKernel().Resolve<ICallsService>();
				var queueService = Bootstrapper.GetKernel().Resolve<IQueueService>();
				var callDispatchStatusService = Bootstrapper.GetKernel().Resolve<ICallDispatchStatusService>();
				var featureToggleService = Bootstrapper.GetKernel().Resolve<IFeatureToggleService>();
				var dispatchRecommendationService = Bootstrapper.GetKernel().Resolve<IDispatchRecommendationService>();

				var pendingCalls = await callsService.GetAllNonDispatchedScheduledCallsWithinDateRange(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));

				if (pendingCalls != null && pendingCalls.Any())
				{
					foreach (var call in pendingCalls)
					{
						// PopulateCallData hydrates and returns the same instance, so this is the
						// one object carried through enrichment, broadcast and the single save.
						var populatedCall = await callsService.PopulateCallData(call, true, false, false, true, true, true, true, false, false);

						// Run card auto-dispatch: scheduled calls are enriched at dispatch time,
						// when the call's location and resource picture are final. Enrichment only
						// mutates the in-memory graph here — persisting it is left to the single
						// save below, because every save of a call fans out events and workflow
						// triggers and the call must not be written twice per dispatch.
						Resgrid.Model.DispatchRecommendationResult recommendation = null;
						try
						{
							if (await featureToggleService.IsEnabledAsync(Resgrid.Model.FeatureFlagKeys.DispatchRunCards, populatedCall.DepartmentId))
							{
								var enriched = await dispatchRecommendationService.EnrichCallForDispatchAsync(populatedCall, 1, true, cancellationToken);

								if (enriched.MatchedRunCardId.HasValue && enriched.AutoDispatch && enriched.HasRecommendations)
									recommendation = enriched;
							}
						}
						catch (Exception recEx)
						{
							// A recommendation failure must never block the scheduled dispatch.
							Resgrid.Framework.Logging.LogException(recEx);
						}

						var cqi = new CallQueueItem();
						cqi.Call = populatedCall;

						if (cqi.Call.Dispatches != null && cqi.Call.Dispatches.Any())
							cqi.Profiles = await userProfileService.GetSelectedUserProfilesAsync(cqi.Call.Dispatches.Select(x => x.UserId).ToList());

						var result = await queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);

						if (result)
						{
							// One write, covering both the dispatched flag and anything the run
							// card added. If the broadcast failed we leave the call untouched so
							// the next poll retries it cleanly.
							populatedCall.HasBeenDispatched = true;
							await callsService.SaveCallAsync(populatedCall, cancellationToken);

							if (recommendation != null)
							{
								try
								{
									// Recorded only once the dispatch actually went out, so the audit
									// trail and its workflow event cannot describe a call that never
									// broadcast.
									await dispatchRecommendationService.RecordActivationAsync(populatedCall, recommendation, null, cancellationToken);
								}
								catch (Exception recEx)
								{
									Resgrid.Framework.Logging.LogException(recEx);
								}
							}

							await callDispatchStatusService.ApplyDispatchStatusesAsync(cqi.Call, cancellationToken: cancellationToken);
						}
					}
				}

				progress.Report(100, $"Finishing the {Name} Task");
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
		}
	}
}
