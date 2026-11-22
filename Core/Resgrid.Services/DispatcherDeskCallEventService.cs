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
					// Duplicate when it is on the desk already (an edit of a pending call, or an emailed call queued as an email).
					if (config.Routes(DispatcherQueueItemKinds.PendingCall))
					{
						await routing.EnqueueAsync(new DispatcherEnqueueRequest
						{
							DepartmentId = deptId,
							Kind = DispatcherQueueItemKinds.PendingCall,
							CallId = callId,
							Subject = name != null && name.Length > 250 ? name.Substring(0, 250) : name,
							Body = nature,
							CreatedByUserId = reportingUserId
						});

						// Already on the desk and now screened (Enhanced AI's reading lands after the call is saved): its offer comes back (M0326).
						await routing.OnCallChangedAsync(deptId, callId);
					}

					return;
				}

				await routing.OnCallChangedAsync(deptId, callId);
			});
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
