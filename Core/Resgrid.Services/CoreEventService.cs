using System;
using System.Threading.Tasks;
using CommonServiceLocator;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Model.Providers;

namespace Resgrid.Services
{
	public class CoreEventService : ICoreEventService
	{
		private readonly IEventAggregator _eventAggregator;

		public CoreEventService(IEventAggregator eventAggregator)
		{
			_eventAggregator = eventAggregator;

			_eventAggregator.AddListener(departmentSettingsUpdateHandler);
		}

		private Action<DepartmentSettingsUpdateEvent> departmentSettingsUpdateHandler = async delegate(DepartmentSettingsUpdateEvent message)
		{
			var departmentSettingsService = ServiceLocator.Current.GetInstance<IDepartmentSettingsService>();
			var result = await departmentSettingsService.SaveOrUpdateSettingAsync(message.DepartmentId, DateTime.UtcNow.ToString("G"), DepartmentSettingTypes.UpdateTimestamp);
		};

		public Task IncidentCommandUpdatedAsync(int departmentId, int callId)
		{
			// Raise the domain event onto the eventing/topic rail (OutboundEventProvider ->
			// RabbitTopicProvider -> EventingTopic -> Eventing Worker -> SignalR "incidentCommandUpdated"),
			// mirroring how CallUpdatedEvent drives "callsUpdated".
			_eventAggregator.SendMessage<IncidentCommandUpdatedEvent>(new IncidentCommandUpdatedEvent
			{
				DepartmentId = departmentId,
				CallId = callId
			});

			return Task.CompletedTask;
		}
	}
}
