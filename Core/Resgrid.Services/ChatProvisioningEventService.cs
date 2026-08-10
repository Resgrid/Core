using System;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
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
			_eventAggregator.AddAsyncListener<IncidentClosedEvent>(OnIncidentClosedAsync);
			_eventAggregator.AddAsyncListener<LaneLeadChangedEvent>(OnLaneLeadChangedAsync);
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

				// Same entry point the read-path backfill uses, so establish and heal-on-read can never
				// drift apart. One existing-channel read, then only the missing rows are inserted.
				// Template-seeded lanes are covered here; later ad-hoc lanes come via SaveNodeAsync.
				var nodes = await incidentCommandService.GetNodesForCallAsync(message.DepartmentId, message.CallId);
				await chatChannelService.EnsureIncidentChannelsAsync(command, nodes);
			});
		}

		/// <summary>
		/// A lane lead changed hands, so who can see the lane and "All Leads" channels changed with it.
		/// Both audiences are derived live from the board, but the permission service caches its verdicts —
		/// without this the outgoing lead keeps access, and the incoming one is locked out, until the cache
		/// expires on its own.
		/// </summary>
		private Task OnLaneLeadChangedAsync(LaneLeadChangedEvent message)
		{
			if (message == null)
				return Task.CompletedTask;

			return RunAsync(async scope =>
			{
				var channelRepository = scope.Resolve<IChatChannelRepository>();
				var permissionService = scope.Resolve<IChatPermissionService>();

				var leadsChannel = await channelRepository.GetByCallIdAndTypeAsync(message.CallId, (int)ChatChannelType.IncidentLeads);
				if (leadsChannel != null)
					await permissionService.InvalidateChannelCacheAsync(leadsChannel.ChatChannelId);

				if (!string.IsNullOrWhiteSpace(message.CommandStructureNodeId))
				{
					var laneChannel = await channelRepository.GetByCommandStructureNodeIdAsync(message.CommandStructureNodeId);
					if (laneChannel != null)
						await permissionService.InvalidateChannelCacheAsync(laneChannel.ChatChannelId);
				}
			});
		}

		/// <summary>
		/// Command closed: freeze its command and lane channels into a point-in-time record. Scoped to the
		/// command, NOT the call — the call may still be running, and its own incident channel has to stay
		/// live. (The call-level freeze is CallClosedEvent's job.)
		/// </summary>
		private Task OnIncidentClosedAsync(IncidentClosedEvent message)
		{
			if (message == null || string.IsNullOrWhiteSpace(message.IncidentCommandId))
				return Task.CompletedTask;

			return RunAsync(scope => scope.Resolve<IChatChannelService>()
				.SetCommandChannelsArchivedAsync(message.IncidentCommandId, true));
		}

		private Task OnIncidentReopenedAsync(IncidentReopenedEvent message)
		{
			if (message == null)
				return Task.CompletedTask;

			return RunAsync(async scope =>
			{
				var chatChannelService = scope.Resolve<IChatChannelService>();

				// Thaw the reopened command's own channels first — this is the part that must happen even
				// when the underlying call is closed.
				if (!string.IsNullOrWhiteSpace(message.IncidentCommandId))
					await chatChannelService.SetCommandChannelsArchivedAsync(message.IncidentCommandId, false);

				// The call's incident channel only comes back when the call itself is open again; reopening
				// command on a closed call must not resurrect the call-wide conversation.
				var call = await scope.Resolve<ICallsService>().GetCallByIdAsync(message.CallId);
				if (call != null && !call.ClosedOn.HasValue)
					await chatChannelService.SetIncidentChannelsArchivedAsync(message.CallId, false);
			});
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
