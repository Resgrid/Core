//using System.Threading.Tasks;
//using Microsoft.AspNetCore.SignalR;
//using Resgrid.Config;
//using Resgrid.Model;
//using Resgrid.Model.Events;
//using Resgrid.Model.Providers;
//using Resgrid.Model.Services;
//using Resgrid.Web.Eventing.Hubs;
//using Resgrid.Web.Eventing.Hubs.Models;

//namespace Resgrid.Web.Eventing.Services
//{
//	public class EventingHubService
//	{
//		private readonly IHubContext<EventingHub> _eventingHub;
//		private readonly IHubContext<GeolocationHub> _geolocationHub;
//		private readonly IRabbitInboundEventProvider _rabbitInboundEventProvider;

//		public EventingHubService(IHubContext<EventingHub> eventingHub, IHubContext<GeolocationHub> geolocationHub,
//			IRabbitInboundEventProvider rabbitInboundEventProvider)
//		{
//			_eventingHub = eventingHub;
//			_geolocationHub = geolocationHub;
//			_rabbitInboundEventProvider = rabbitInboundEventProvider;
//			_rabbitInboundEventProvider.RegisterForEvents(PersonnelStatusUpdated, UnitStatusUpdated, CallsUpdated,
//				PersonnelStaffingUpdated, CallAdded, CallClosed, PersonnelLocationUpdated, UnitLocationUpdated);
//		}
		
//		public async Task PersonnelStatusUpdated(int departmentId, string id)
//		{
//			var group = _eventingHub.Clients.Group(departmentId.ToString());

//			if (group != null)
//				await group.SendAsync("personnelStatusUpdated", id);
//		}

//		public async Task PersonnelStaffingUpdated(int departmentId, string id)
//		{
//			var group = _eventingHub.Clients.Group(departmentId.ToString());

//			if (group != null)
//				await group.SendAsync("personnelStaffingUpdated", id);
//		}

//		public async Task UnitStatusUpdated(int departmentId, string id)
//		{
//			var group = _eventingHub.Clients.Group(departmentId.ToString());

//			if (group != null)
//				await group.SendAsync("unitStatusUpdated", id);
//		}

//		public async Task CallsUpdated(int departmentId, string id)
//		{
//			var group = _eventingHub.Clients.Group(departmentId.ToString());

//			if (group != null)
//				await group.SendAsync("callsUpdated", id);
//		}

//		public async Task DepartmentUpdated(int departmentId)
//		{
//			var group = _eventingHub.Clients.Group(departmentId.ToString());

//			if (group != null)
//				await group.SendAsync("departmentUpdated");
//		}

//		public async Task CallAdded(int departmentId, string id)
//		{
//			var group = _eventingHub.Clients.Group(departmentId.ToString());

//			if (group != null)
//				await group.SendAsync("callAdded", id);
//		}

//		public async Task CallClosed(int departmentId, string id)
//		{
//			var group = _eventingHub.Clients.Group(departmentId.ToString());

//			if (group != null)
//				await group.SendAsync("callClosed", id);
//		}

//		public async Task PersonnelLocationUpdated(int departmentId, PersonnelLocationUpdatedEvent update)
//		{
//			var group = _geolocationHub.Clients.Group(departmentId.ToString());

//			var location = new PersonnelLocationUpdate();
//			location.DepartmentId = update.DepartmentId;
//			location.UserId = update.UserId;
//			location.Latitude = update.Latitude;
//			location.Longitude = update.Longitude;
//			location.RecordId = update.RecordId;

//			if (group != null)
//				await group.SendAsync("onPersonnelLocationUpdated", location);
//		}

//		public async Task UnitLocationUpdated(int departmentId, UnitLocationUpdatedEvent update)
//		{
//			var group = _geolocationHub.Clients.Group(departmentId.ToString());

//			var location = new UnitLocationUpdate();
//			location.DepartmentId = update.DepartmentId;
//			location.UnitId = update.UnitId;
//			location.Latitude = update.Latitude;
//			location.Longitude = update.Longitude;
//			location.RecordId = update.RecordId;

//			if (group != null)
//				await group.SendAsync("onUnitLocationUpdated", location);
//		}
//	}
//}
