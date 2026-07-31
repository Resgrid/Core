using System;
using CommonServiceLocator;
using Resgrid.Framework;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Bridges domain events to chat channel provisioning (CoreEventService pattern). Registered as an
	/// auto-activated singleton so every host that raises call/incident events provisions the matching
	/// chat channels. Provisioning is idempotent and best-effort: a chat failure must never affect call
	/// or command flow, so every handler swallows and logs.
	/// </summary>
	public class ChatProvisioningEventService : IChatProvisioningEventService
	{
		private readonly IEventAggregator _eventAggregator;

		public ChatProvisioningEventService(IEventAggregator eventAggregator)
		{
			_eventAggregator = eventAggregator;

			_eventAggregator.AddListener(callAddedHandler);
			_eventAggregator.AddListener(callClosedHandler);
			_eventAggregator.AddListener(commandEstablishedHandler);
			_eventAggregator.AddListener(incidentReopenedHandler);
		}

		private Action<CallAddedEvent> callAddedHandler = async delegate (CallAddedEvent message)
		{
			try
			{
				if (message?.Call == null)
					return;

				var chatChannelService = ServiceLocator.Current.GetInstance<IChatChannelService>();
				await chatChannelService.EnsureIncidentChannelAsync(message.Call.DepartmentId, message.Call.CallId, message.Call.Name);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		};

		private Action<CallClosedEvent> callClosedHandler = async delegate (CallClosedEvent message)
		{
			try
			{
				if (message?.Call == null)
					return;

				var chatChannelService = ServiceLocator.Current.GetInstance<IChatChannelService>();
				await chatChannelService.SetIncidentChannelsArchivedAsync(message.Call.CallId, true);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		};

		private Action<CommandEstablishedEvent> commandEstablishedHandler = async delegate (CommandEstablishedEvent message)
		{
			try
			{
				if (message == null)
					return;

				var chatChannelService = ServiceLocator.Current.GetInstance<IChatChannelService>();
				var incidentCommandService = ServiceLocator.Current.GetInstance<IIncidentCommandService>();

				var command = await incidentCommandService.GetCommandByIdAsync(message.IncidentCommandId);
				if (command == null)
					return;

				await chatChannelService.EnsureIncidentChannelAsync(message.DepartmentId, message.CallId, null);
				await chatChannelService.EnsureCommandChannelAsync(command);

				// Lane channels for template-seeded nodes; later ad-hoc lanes are handled by SaveNodeAsync.
				var nodes = await incidentCommandService.GetNodesForCallAsync(message.DepartmentId, message.CallId);
				if (nodes != null)
				{
					foreach (var node in nodes)
						await chatChannelService.EnsureLaneChannelAsync(node);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		};

		private Action<IncidentReopenedEvent> incidentReopenedHandler = async delegate (IncidentReopenedEvent message)
		{
			try
			{
				if (message == null)
					return;

				var chatChannelService = ServiceLocator.Current.GetInstance<IChatChannelService>();
				await chatChannelService.SetIncidentChannelsArchivedAsync(message.CallId, false);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		};
	}
}
