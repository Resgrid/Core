using System;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Model.Providers;

namespace Resgrid.Services
{
	/// <summary>
	/// Registered as a singleton, so the scoped department settings service is NOT resolved once and kept: from
	/// the root scope it would share one unit of work, and one DB connection, with everything else resolved there,
	/// and the timestamp save runs inside an audited configuration transaction. Each event instead runs in its own
	/// child scope, as <see cref="ChatProvisioningEventService"/> does.
	/// </summary>
	public class CoreEventService : ICoreEventService
	{
		private readonly IEventAggregator _eventAggregator;
		private readonly ILifetimeScope _lifetimeScope;

		public CoreEventService(IEventAggregator eventAggregator, ILifetimeScope lifetimeScope)
		{
			_eventAggregator = eventAggregator;
			_lifetimeScope = lifetimeScope;

			// Fire-and-forget as before: the publisher (a unit, department or custom state save) is not held up.
			_eventAggregator.AddListener<DepartmentSettingsUpdateEvent>(message => _ = UpdateDepartmentTimestampAsync(message));
		}

		private async Task UpdateDepartmentTimestampAsync(DepartmentSettingsUpdateEvent message)
		{
			try
			{
				using var scope = _lifetimeScope.BeginLifetimeScope();
				await scope.Resolve<IDepartmentSettingsService>().SaveOrUpdateSettingAsync(message.DepartmentId, DateTime.UtcNow.ToString("G"), DepartmentSettingTypes.UpdateTimestamp);
			}
			catch (Exception ex)
			{
				// Nothing awaits this, so an escaping exception would go unobserved.
				Logging.LogException(ex, $"Department update timestamp could not be saved for department {message?.DepartmentId}.");
			}
		}

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
