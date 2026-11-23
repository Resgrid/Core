using System;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Keeps the dispatcher desk (M0324, dispatchers-on-duty-plan.md) in step with calls changed anywhere else, in every host that
	/// raises call events (web, API, workers): a call saved as pending (v4 SaveCall, the web's Save as Pending; both raise
	/// <see cref="CallUpdatedEvent"/> for a pending call) is put on the desk at once instead of on the desk worker's next sweep, and a
	/// pending or scheduled call that was dispatched (<see cref="PendingCallsService.DispatchNowAsync"/> raises Call Added / Updated),
	/// closed (<see cref="CallClosedEvent"/>) or deleted (Call Updated with IsDeleted) has its desk items completed or cancelled
	/// through <see cref="IDispatcherRoutingService.OnCallChangedAsync"/>.
	/// </summary>
	/// <remarks>
	/// An auto-activated singleton like <see cref="ChatProvisioningEventService"/>, with the same scope rule: the desk services are
	/// per-scope, so each event runs in its own child lifetime scope. Call events are raised with the synchronous
	/// <see cref="IEventPublisher.SendMessage{TMessage}"/>, so the listeners are synchronous and hand off (fire and forget, like
	/// <see cref="CoreEventService"/>); the publisher is never held up and a desk failure never reaches the call flow. Departments
	/// whose desk is off cost one cached config read.
	/// </remarks>
	public class DispatcherDeskCallEventService
	{
		private readonly ILifetimeScope _lifetimeScope;

		public DispatcherDeskCallEventService(IEventAggregator eventAggregator, ILifetimeScope lifetimeScope)
		{
			_lifetimeScope = lifetimeScope;

			eventAggregator.AddListener<CallAddedEvent>(message => _ = OnCallChangedAsync(message?.DepartmentId ?? 0, message?.Call));
			eventAggregator.AddListener<CallUpdatedEvent>(message => _ = OnCallChangedAsync(message?.DepartmentId ?? 0, message?.Call));
			eventAggregator.AddListener<CallClosedEvent>(message => _ = OnCallChangedAsync(message?.DepartmentId ?? 0, message?.Call));
		}

		/// <summary>One call event: a waiting pending call goes on the desk; anything else tells the desk the call changed.</summary>
		public Task OnCallChangedAsync(int departmentId, Call call)
		{
			if (call == null || call.CallId <= 0)
				return Task.CompletedTask;

			// Read now: the publisher keeps using (and may change) the call after the hand-off.
			var deptId = call.DepartmentId > 0 ? call.DepartmentId : departmentId;
			var callId = call.CallId;
			var waitingPending = !call.IsDeleted && call.State == (int)CallStates.Pending;
			var name = call.Name;
			var nature = call.NatureOfCall;
			var reportingUserId = call.ReportingUserId;
			var callSource = call.CallSource;
			var sourceIdentifier = call.SourceIdentifier;
			var contactName = call.ContactName;

			if (deptId <= 0)
				return Task.CompletedTask;

			return RunAsync(async scope =>
			{
				var config = await scope.Resolve<IDispatcherDeskConfigService>().GetConfigAsync(deptId);
				if (config == null || !config.Enabled)
					return;

				var routing = scope.Resolve<IDispatcherRoutingService>();

				if (waitingPending)
				{
					// Duplicate when it is on the desk already (an edit of a pending call, a chat app's call put there by intake, or an emailed
					// call queued as an email). Under its own kind, so a chat or an email whose first hand-off failed still reaches a desk that
					// takes chats or emails but not pending calls. A failure here (the intake channel read, the insert) is left to the desk
					// worker's sweep and never stops the reconcile below from withdrawing an offer the change made stale.
					try
					{
						var kind = await WaitingCallKindAsync(scope, config, deptId, callId, callSource, sourceIdentifier);
						if (kind.HasValue)
						{
							await routing.EnqueueAsync(new DispatcherEnqueueRequest
							{
								DepartmentId = deptId,
								Kind = kind.Value,
								CallId = callId,
								SourceName = kind.Value == DispatcherQueueItemKinds.Chat ? contactName : null,
								Subject = name != null && name.Length > 250 ? name.Substring(0, 250) : name,
								Body = nature,
								CreatedByUserId = reportingUserId
							});
						}
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"The dispatcher desk could not take waiting call {callId}; the desk sweep tries again.");
					}

					// Already on the desk (by any of its options) and now screened (Enhanced AI's reading lands after the call is saved): its
					// offer comes back (M0326). Whether or not the desk still takes new pending calls.
					await routing.OnCallChangedAsync(deptId, callId);
					return;
				}

				await routing.OnCallChangedAsync(deptId, callId);
			});
		}

		/// <summary>
		/// The desk kind a waiting pending call belongs under: a chat app's intake call is a Chat when the desk takes chats, an emailed
		/// call (not one of the desk's own "DQ:" pending calls) an Email when it takes emails, anything else (and those, when their own
		/// option is unticked) a PendingCall when it takes pending calls; null when the desk takes none of them.
		/// </summary>
		private static async Task<DispatcherQueueItemKinds?> WaitingCallKindAsync(ILifetimeScope scope, DispatcherDeskConfig config, int departmentId, int callId,
			int callSource, string sourceIdentifier)
		{
			if (callSource == (int)CallSources.Intake && config.Routes(DispatcherQueueItemKinds.Chat) && await IsChatCallAsync(scope, departmentId, callId))
				return DispatcherQueueItemKinds.Chat;

			if (callSource == (int)CallSources.EmailImport && config.Routes(DispatcherQueueItemKinds.Email) &&
				(sourceIdentifier == null || !sourceIdentifier.StartsWith(AiDispatch.AiDispatchEnrichmentService.DeskCallSourcePrefix, StringComparison.Ordinal)))
				return DispatcherQueueItemKinds.Email;

			return config.Routes(DispatcherQueueItemKinds.PendingCall) ? DispatcherQueueItemKinds.PendingCall : null;
		}

		private static async Task<bool> IsChatCallAsync(ILifetimeScope scope, int departmentId, int callId)
		{
			var intake = scope.ResolveOptional<IIntakeService>();
			if (intake == null)
				return false;

			var sources = await intake.GetCallSourcesAsync(departmentId, new[] { callId });
			return sources != null && sources.TryGetValue(callId, out var source) && Intake.IntakeService.IsChatChannel((IntakeChannelTypes)source.ChannelType);
		}

		private async Task RunAsync(Func<ILifetimeScope, Task> action)
		{
			try
			{
				using var scope = _lifetimeScope.BeginLifetimeScope();
				await action(scope);
			}
			catch (Exception ex)
			{
				// Nothing awaits this hand-off, so an escaping exception would go unobserved; the desk worker's sweep catches up.
				Logging.LogException(ex, "The dispatcher desk could not be told about a call change.");
			}
		}
	}
}
