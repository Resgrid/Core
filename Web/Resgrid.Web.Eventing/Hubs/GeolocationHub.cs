using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OpenIddict.Server.AspNetCore;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Eventing.Hubs.Models;
using Resgrid.Web.Eventing.Services;

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
		private readonly ILocationVisibilityService _locationVisibilityService;
		private readonly GeolocationConnectionTracker _connectionTracker;
		private readonly GeolocationMembership _membership;
		private readonly GeolocationBroadcaster _broadcaster;

		public GeolocationHub(IUnitsService unitsService, IUsersService usersService, IDepartmentsService departmentsService,
			ILocationVisibilityService locationVisibilityService, GeolocationConnectionTracker connectionTracker,
			GeolocationMembership membership, GeolocationBroadcaster broadcaster)
		{
			_unitsService = unitsService;
			_usersService = usersService;
			_departmentsService = departmentsService;
			_locationVisibilityService = locationVisibilityService;
			_connectionTracker = connectionTracker;
			_membership = membership;
			_broadcaster = broadcaster;
		}

		// ClaimsAuthorizationHelper reads IHttpContextAccessor.HttpContext, which is not flowed into
		// hub invocations on every transport — it comes back null and NREs. HubCallerContext.User is
		// the connection's authenticated principal and is the supported claim source inside a hub.
		private int GetDepartmentId()
		{
			var claim = Context.User?.FindFirst(ClaimTypes.PrimaryGroupSid);

			return claim != null && int.TryParse(claim.Value, out var departmentId) ? departmentId : 0;
		}

		private string GetUserId()
		{
			return Context.User?.FindFirst(ClaimTypes.PrimarySid)?.Value;
		}

		private GeolocationConnection TrackConnection(int departmentId)
		{
			return _connectionTracker.GetOrAdd(Context.ConnectionId, departmentId, GetUserId());
		}

		/// <summary>
		/// Subscribes to the department map: every unit and personnel location this viewer may see under
		/// the location visibility matrix, the same set the REST map returns.
		/// </summary>
		public async Task GeolocationConnect()
		{
			var departmentId = GetDepartmentId();

			if (departmentId > 0)
			{
				var connection = TrackConnection(departmentId);
				connection.SubscribeToDepartmentMap();
				await _membership.SyncAsync(Groups, connection, Context.ConnectionAborted);

				await Clients.Caller.SendAsync("onGeolocationConnect", Context.ConnectionId);
			}
		}

		public override Task OnDisconnectedAsync(Exception exception)
		{
			_connectionTracker.Remove(Context.ConnectionId);

			return base.OnDisconnectedAsync(exception);
		}

		// Location fan-out normally comes from the eventing Worker (Rabbit -> IHubContext). These hub
		// methods broadcast to a caller-chosen department, so only the internal publisher may call them,
		// and they apply the same visibility routing as the Worker.
		public async Task PersonnelLocationUpdated(PersonnelLocationUpdate update)
		{
			DemandInternalPublisher();

			if (update != null)
				await _broadcaster.SendPersonnelLocationAsync(Clients, update.DepartmentId, update);
		}

		public async Task UnitLocationUpdated(UnitLocationUpdate update)
		{
			DemandInternalPublisher();

			if (update != null)
				await _broadcaster.SendUnitLocationAsync(Clients, update.DepartmentId, update);
		}

		public async Task UnitLocationConnect(int unitId)
		{
			var departmentId = GetDepartmentId();
			var unit = await _unitsService.GetUnitByIdAsync(unitId);

			if (unit == null || departmentId <= 0 || unit.DepartmentId != departmentId)
				return;

			// Same rule as the REST map: a unit whose location the viewer may not see is not trackable.
			if (!await _locationVisibilityService.CanViewUnitLocationAsync(departmentId, unitId, GetUserId()))
				return;

			var connection = TrackConnection(departmentId);
			connection.SubscribeToUnit(unitId);
			await _membership.SyncAsync(Groups, connection, Context.ConnectionAborted);

			await Clients.Caller.SendAsync("onUnitLocationConnect", Context.ConnectionId);
		}

		public async Task PersonLocationConnect(string userId)
		{
			if (String.IsNullOrWhiteSpace(userId))
				return;

			var departmentId = GetDepartmentId();
			var memberships = await _departmentsService.GetAllDepartmentsForUserAsync(userId);

			if (departmentId <= 0 || memberships == null || !memberships.Any(x => x.DepartmentId == departmentId))
				return;

			if (!await _locationVisibilityService.CanViewPersonnelLocationAsync(departmentId, userId, GetUserId()))
				return;

			var connection = TrackConnection(departmentId);
			connection.SubscribeToPerson(userId);
			await _membership.SyncAsync(Groups, connection, Context.ConnectionAborted);

			await Clients.Caller.SendAsync("onPersonLocationConnect", Context.ConnectionId);
		}

		private void DemandInternalPublisher()
		{
			var subject = Context.User?.FindFirst("sub")?.Value ??
				Context.User?.FindFirst(ClaimTypes.PrimarySid)?.Value;
			if (!string.Equals(subject, "system_eventing", StringComparison.Ordinal))
				throw new HubException("This operation is reserved for the eventing publisher.");
		}
	}
}
