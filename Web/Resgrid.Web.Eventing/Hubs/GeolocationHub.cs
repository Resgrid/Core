using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OpenIddict.Server.AspNetCore;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Eventing.Hubs.Models;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Eventing.Hubs
{
	public interface IGeolocationHub
	{
		Task GeolocationConnect();

		Task PersonnelLocationUpdated(int departmentId, int id);

		Task UnitLocationUpdated(int departmentId, int id);

		Task UnitLocationConnect(int unitId);

		Task PersonLocationConnect(string userId);
	}

	[Authorize(AuthenticationSchemes = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
	public class GeolocationHub : Hub
	{
		private readonly IUnitsService _unitsService;
		private readonly IUsersService _usersService;
		private readonly IDepartmentsService _departmentsService;
			
		public GeolocationHub(IUnitsService unitsService, IUsersService usersService, IDepartmentsService departmentsService)
		{
			_unitsService = unitsService;
			_usersService = usersService;
			_departmentsService = departmentsService;
		}

		public async Task GeolocationConnect()
		{
			var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();

			if (departmentId > 0)
			{
				await Groups.AddToGroupAsync(Context.ConnectionId, departmentId.ToString());

				await Clients.Caller.SendAsync("onGeolocationConnect", Context.ConnectionId);
			}
		}
		
		public async Task PersonnelLocationUpdated(PersonnelLocationUpdate update)
		{
			if (update != null && !String.IsNullOrWhiteSpace(update.UserId) && update.DepartmentId > 0)
			{
				var group = Clients.Group(update.DepartmentId.ToString());

				if (group != null)
					await group.SendAsync("onPersonnelLocationUpdated", update);

				var person = Clients.Group($"PersonLocation_{update.UserId}");
				if (person != null)
					await person.SendAsync("onUnitLocationUpdated", update);
			}
		}
		
		public async Task UnitLocationUpdated(UnitLocationUpdate update)
		{
			if (update != null && !String.IsNullOrWhiteSpace(update.UnitId) && update.DepartmentId > 0)
			{
				var group = Clients.Group(update.DepartmentId.ToString());
				if (group != null)
					await group.SendAsync("onUnitLocationUpdated", update);

				var unit = Clients.Group($"UnitLocation_{update.UnitId}");
				if (unit != null)
					await unit.SendAsync("onUnitLocationUpdated", update);
			}
		}

		public async Task UnitLocationConnect(int unitId)
		{
			var unit = await _unitsService.GetUnitByIdAsync(unitId);

			if (unit != null)
			{
				if (unit.DepartmentId != ClaimsAuthorizationHelper.GetDepartmentId())
					return;
				
				await Groups.AddToGroupAsync(Context.ConnectionId, $"UnitLocation_{unitId}");

				await Clients.Caller.SendAsync("onUnitLocationConnect", Context.ConnectionId);
			}
		}

		public async Task PersonLocationConnect(string userId)
		{
			var memberships = await _departmentsService.GetAllDepartmentsForUserAsync(userId);

			if (memberships != null && memberships.Any(x => x.DepartmentId == ClaimsAuthorizationHelper.GetDepartmentId()))
			{
				await Groups.AddToGroupAsync(Context.ConnectionId, $"PersonLocation_{userId}");

				await Clients.Caller.SendAsync("onPersonLocationConnect", Context.ConnectionId);
			}
		}
	}
}
