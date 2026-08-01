using System;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Bridges domain events to chat channel provisioning. Registered as an auto-activated singleton so
	/// every host that raises call/incident events provisions the matching chat channels.
	///
	/// The scoped chat/incident services are NOT constructor-injected: this is a singleton, and capturing
	/// InstancePerLifetimeScope services in it would be a captive dependency (one shared instance — and one
	/// shared DB connection — for the whole app lifetime). Instead an <see cref="ILifetimeScope"/> is
	/// injected and each event runs in its own child scope, giving fresh scoped services (and their own
	/// unit-of-work/connection) that are disposed afterward. Provisioning is idempotent and best-effort:
	/// a chat failure must never affect call or command flow, so every handler swallows and logs.
	/// </summary>
	public class ChatProvisioningEventService : IChatProvisioningEventService
	{
		private readonly IEventAggregator _eventAggregator;
		private readonly ILifetimeScope _lifetimeScope;

		public ChatProvisioningEventService(IEventAggregator eventAggregator, ILifetimeScope lifetimeScope)
		{
			_eventAggregator = eventAggregator;
			_lifetimeScope = lifetimeScope;

			_eventAggregator.AddAsyncListener<CallAddedEvent>(OnCallAddedAsync);
			_eventAggregator.AddAsyncListener<CallClosedEvent>(OnCallClosedAsync);
			_eventAggregator.AddAsyncListener<CommandEstablishedEvent>(OnCommandEstablishedAsync);
			_eventAggregator.AddAsyncListener<IncidentReopenedEvent>(OnIncidentReopenedAsync);
		}

		private Task OnCallAddedAsync(CallAddedEvent message)
		{
			if (message?.Call == null)
				return Task.CompletedTask;

			return RunAsync(scope => scope.Resolve<IChatChannelService>()
				.EnsureIncidentChannelAsync(message.Call.DepartmentId, message.Call.CallId, message.Call.Name));
		}

		private Task OnCallClosedAsync(CallClosedEvent message)
		{
			if (message?.Call == null)
				return Task.CompletedTask;

			return RunAsync(scope => scope.Resolve<IChatChannelService>()
				.SetIncidentChannelsArchivedAsync(message.Call.CallId, true));
		}

		private Task OnCommandEstablishedAsync(CommandEstablishedEvent message)
		{
			if (message == null)
				return Task.CompletedTask;

			return RunAsync(async scope =>
			{
				var chatChannelService = scope.Resolve<IChatChannelService>();
				var incidentCommandService = scope.Resolve<IIncidentCommandService>();

				var command = await incidentCommandService.GetCommandByIdAsync(message.IncidentCommandId);
				if (command == null)
					return;

				await chatChannelService.EnsureIncidentChannelAsync(message.DepartmentId, message.CallId, null);
				await chatChannelService.EnsureCommandChannelAsync(command);

				// Lane channels for template-seeded nodes; later ad-hoc lanes are handled by SaveNodeAsync.
				// Batched: one existing-channel read for the call, then insert only the missing lanes.
				var nodes = await incidentCommandService.GetNodesForCallAsync(message.DepartmentId, message.CallId);
				await chatChannelService.EnsureLaneChannelsAsync(nodes);
			});
		}

		private Task OnIncidentReopenedAsync(IncidentReopenedEvent message)
		{
			if (message == null)
				return Task.CompletedTask;

			return RunAsync(scope => scope.Resolve<IChatChannelService>()
				.SetIncidentChannelsArchivedAsync(message.CallId, false));
		}

		/// <summary>
		/// Runs a provisioning action in its own DI lifetime scope so each event gets fresh scoped
		/// services (and their own unit-of-work/DB connection), disposed when the action completes.
		/// Best-effort: failures are swallowed and logged so chat never affects call/command flow.
		/// </summary>
		private async Task RunAsync(Func<ILifetimeScope, Task> action)
		{
			try
			{
				using var scope = _lifetimeScope.BeginLifetimeScope();
				await action(scope);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}
	}
}
