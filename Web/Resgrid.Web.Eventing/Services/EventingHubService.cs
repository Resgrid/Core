using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Eventing.Hubs;

namespace Resgrid.Web.Eventing.Services
{
	public class EventingHubService
	{
		private readonly IHubContext<EventingHub> _eventingHub;
		private readonly IInboundEventProvider _inboundEventProvider;
		private readonly IRabbitInboundEventProvider _rabbitInboundEventProvider;

		public EventingHubService(IHubContext<EventingHub> eventingHub, IInboundEventProvider inboundEventProvider, IRabbitInboundEventProvider rabbitInboundEventProvider)
		{
			_eventingHub = eventingHub;
			_inboundEventProvider = inboundEventProvider;
			_rabbitInboundEventProvider = rabbitInboundEventProvider;

			if (SystemBehaviorConfig.ServiceBusType == ServiceBusTypes.Rabbit)
				_rabbitInboundEventProvider.RegisterForEvents(PersonnelStatusUpdated, UnitStatusUpdated, CallsUpdated, PersonnelStaffingUpdated);
			else
				_inboundEventProvider.RegisterForEvents(PersonnelStatusUpdated, UnitStatusUpdated, CallsUpdated, PersonnelStaffingUpdated);
		}

		public async Task PersonnelStatusUpdated(int departmentId, int id)
		{
			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("personnelStatusUpdated", id);
		}

		public async Task PersonnelStaffingUpdated(int departmentId, int id)
		{
			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("personnelStaffingUpdated", id);
		}

		public async Task UnitStatusUpdated(int departmentId, int id)
		{
			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("unitStatusUpdated", id);
		}

		public async Task CallsUpdated(int departmentId, int id)
		{
			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("callsUpdated", id);
		}

		public async Task DepartmentUpdated(int departmentId)
		{
			var group = _eventingHub.Clients.Group(departmentId.ToString());

			if (group != null)
				await group.SendAsync("departmentUpdated");
		}
	}
}
