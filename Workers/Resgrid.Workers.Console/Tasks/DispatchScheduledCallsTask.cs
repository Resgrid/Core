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

				using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
				var userProfileService = scope.Resolve<IUserProfileService>();
				var callsService = scope.Resolve<ICallsService>();
				var queueService = scope.Resolve<IQueueService>();
				var callDispatchStatusService = scope.Resolve<ICallDispatchStatusService>();
				var featureToggleService = scope.Resolve<IFeatureToggleService>();
				var dispatchRecommendationService = scope.Resolve<IDispatchRecommendationService>();

				// Resolved when first needed, inside the boundary that uses it, so it can never stop calls already due from going out.
				IRecurringCallsService recurringCallsService = null;
				IRecurringCallsService Recurring() => recurringCallsService ??= scope.Resolve<IRecurringCallsService>();

				// Calls already due go out first: recurring schedules (every department's, with their alerts) must never hold them up,
				// and a failure here must not hold up the recurring schedules or the owed pages below either.
				await TryDispatchDueCallsAsync();

				// Then recurring calls, in their own failure boundary: each occurrence due within its schedule's lead time becomes an
				// ordinary scheduled call (dispatcher-mode schedules alert a dispatcher instead; missed occurrences and settled alerts
				// are dealt with too). One created already due is sent by a second dispatch run in this same pass.
				var created = 0;
				try
				{
					created = await Recurring().GenerateDueCallsAsync(DateTime.UtcNow, cancellationToken);
					if (created > 0)
						progress.Report(60, $"{Name}: created {created} call(s) from recurring schedules");
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception recurringEx)
				{
					Resgrid.Framework.Logging.LogException(recurringEx, "Recurring call schedules could not create their calls.");
				}

				if (created > 0)
					await TryDispatchDueCallsAsync();

				// Pages owed to resources an edit added to a live call that the queue refused (or that edit never sent): their own
				// failure boundary, after the scheduled calls.
				try
				{
					var paged = await scope.Resolve<ICallBroadcastOutboxService>().SendDueAsync(100, cancellationToken);
					if (paged > 0)
						progress.Report(90, $"{Name}: sent {paged} page(s) for resources added to calls");
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception outboxEx)
				{
					Resgrid.Framework.Logging.LogException(outboxEx, "Pages for resources added to calls could not be retried.");
				}

				progress.Report(100, $"Finishing the {Name} Task");

				async Task TryDispatchDueCallsAsync()
				{
					try
					{
						await DispatchDueCallsAsync();
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						throw;
					}
					catch (Exception dueEx)
					{
						Resgrid.Framework.Logging.LogException(dueEx, "Scheduled calls due now could not be read or dispatched.");
					}
				}

				async Task DispatchDueCallsAsync()
				{
					cancellationToken.ThrowIfCancellationRequested();

					// Calls whose dispatch time has arrived, never ones still ahead (the old +5 minute edge sent
					// calls up to one poll early). Looking back three poll intervals lets a broadcast that failed,
					// or a poll that was missed, retry; HasBeenDispatched keeps each call to a single dispatch. The
					// list also holds calls whose dispatch claim was abandoned (its process died mid-dispatch).
					var now = DateTime.UtcNow;
					var pendingCalls = await callsService.GetAllNonDispatchedScheduledCallsWithinDateRange(now.AddMinutes(-15), now);

					if (pendingCalls != null && pendingCalls.Any())
					{
						foreach (var call in pendingCalls)
						{
							// Stopping: no further call is started (one already claimed finishes or hands its claim back below).
							cancellationToken.ThrowIfCancellationRequested();

							// A scheduled call closed or cancelled before its time must not page anyone.
							if (call.State != (int)Resgrid.Model.CallStates.Active)
								continue;

							// A recurring call goes out through its schedule's rules: the run card it asks for, only the chosen resources
							// that are available, and held for a dispatcher (with an alert) when nobody is left.
							if (call.RecurringCallId.HasValue)
							{
								try
								{
									var outcome = await Recurring().DispatchCallAsync(call, null, cancellationToken);
									if (outcome.Outcome == RecurringCallDispatchOutcomes.Held)
										progress.Report(50, $"{Name}: recurring call {call.CallId} held, nobody available to send");
								}
								catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
								{
									throw;
								}
								catch (Exception recurringDispatchEx)
								{
									Resgrid.Framework.Logging.LogException(recurringDispatchEx, $"Recurring call {call.CallId} could not be dispatched.");
								}

								continue;
							}

							// One call that keeps failing must not hold up the calls after it (or the rest of this task): it is
							// logged, and retried on the next poll unless its page already went out.
							var broadcastAccepted = false;
							try
							{
								// A dispatcher may press Dispatch Now on this call while the poll runs: only whoever marks the
								// stored call dispatched first sends it.
								var claimedOn = await callsService.TryClaimCallForDispatchAsync(call.CallId, call.DepartmentId, cancellationToken);
								if (!claimedOn.HasValue)
									continue;

								var originalDispatchOn = call.DispatchOn;
								var sent = false;
								try
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

									// Once queued the call has gone out, so nothing after this may hand the claim back.
									sent = await queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);
									broadcastAccepted = sent;

									if (sent)
									{
										// Ended before anything else can fail, so the claim's lease never sends the call twice.
										try
										{
											await callsService.CompleteCallDispatchClaimAsync(call.CallId, call.DepartmentId, claimedOn.Value);
										}
										catch (Exception claimEx)
										{
											Resgrid.Framework.Logging.LogException(claimEx, $"Could not complete the dispatch claim on call {call.CallId}.");
										}

										// One write, covering both the dispatched flag and anything the run
										// card added. If the broadcast failed the claim is given back below so
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
								finally
								{
									if (!sent)
										await callsService.ReleaseCallDispatchClaimAsync(call.CallId, call.DepartmentId, claimedOn.Value, (int)Resgrid.Model.CallStates.Active, originalDispatchOn, false);
								}
							}
							catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
							{
								throw;
							}
							catch (Exception callEx)
							{
								// After the queue accepted the page the claim is completed, so the next poll does not send it again: only
								// the follow-up (dispatched flag, run card record, statuses) is missing. Before that, the claim is given
								// back and the next poll retries the call while it is still within the look-back.
								Resgrid.Framework.Logging.LogException(callEx, broadcastAccepted
									? $"Scheduled call {call.CallId} was dispatched, but the work after its page (dispatched flag, run card record or statuses) failed; it is not sent again."
									: $"Scheduled call {call.CallId} could not be dispatched; the next poll retries it while it is still due.");
							}
						}
					}
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// The worker is stopping: not a failure of this task.
				throw;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				_logger.LogError(ex.ToString());
			}
		}
	}
}
