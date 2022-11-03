using System;
using System.Threading.Tasks;
using CommonServiceLocator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Resgrid.Model.Services;
using Resgrid.Web.Eventing.Hubs.Models;

namespace Resgrid.Web.Eventing.Hubs
{
	public interface IGeolocationHub
	{
		Task GeolocationConnect(int departmentId);

		Task PersonnelLocationUpdated(int departmentId, int id);

		Task UnitLocationUpdated(int departmentId, int id);
	}

	[AllowAnonymous]
	public class GeolocationHub : Hub
	{
		public GeolocationHub()
		{

		}

		public async Task GeolocationConnect(int departmentId)
		{
			await Groups.AddToGroupAsync(Context.ConnectionId, departmentId.ToString());

			await Clients.Caller.SendAsync("onGeolocationConnect", Context.ConnectionId);
		}
		
		public async Task PersonnelLocationUpdated(PersonnelLocationUpdate update)
		{
			if (update != null && !String.IsNullOrWhiteSpace(update.UserId) && update.DepartmentId > 0)
			{
				var group = Clients.Group(update.DepartmentId.ToString());

				if (group != null)
					await group.SendAsync("onPersonnelLocationUpdated", update);
			}
		}
		
		public async Task UnitLocationUpdated(UnitLocationUpdate update)
		{
			if (update != null && !String.IsNullOrWhiteSpace(update.UnitId) && update.DepartmentId > 0)
			{
				var group = Clients.Group(update.DepartmentId.ToString());

				if (group != null)
					await group.SendAsync("onUnitLocationUpdated", update);
			}
		}
	}
}
