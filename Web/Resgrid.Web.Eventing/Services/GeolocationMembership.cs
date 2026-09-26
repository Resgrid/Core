using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Resgrid.Model.Services;
using Resgrid.Web.Eventing.Hubs;

namespace Resgrid.Web.Eventing.Services
{
	/// <summary>
	/// Puts a geolocation connection in exactly the groups its subscriptions allow under the current
	/// location visibility matrix. Run when a client subscribes and again periodically, because the matrix
	/// is rebuilt when groups, roles or permissions change.
	/// </summary>
	public sealed class GeolocationMembership
	{
		private readonly ILocationVisibilityService _locationVisibilityService;

		public GeolocationMembership(ILocationVisibilityService locationVisibilityService)
		{
			_locationVisibilityService = locationVisibilityService;
		}

		public async Task SyncAsync(IGroupManager groups, GeolocationConnection connection, CancellationToken cancellationToken = default)
		{
			await connection.MembershipGate.WaitAsync(cancellationToken);

			try
			{
				var desired = await GetAllowedGroupsAsync(connection);

				// Join before leaving so a viewer whose set key changed is never briefly in neither group.
				foreach (var group in desired.Where(x => !connection.JoinedGroups.Contains(x)).ToList())
				{
					await groups.AddToGroupAsync(connection.ConnectionId, group, cancellationToken);
					connection.JoinedGroups.Add(group);
				}

				foreach (var group in connection.JoinedGroups.Where(x => !desired.Contains(x)).ToList())
				{
					await groups.RemoveFromGroupAsync(connection.ConnectionId, group, cancellationToken);
					connection.JoinedGroups.Remove(group);
				}
			}
			finally
			{
				connection.MembershipGate.Release();
			}
		}

		public async Task<HashSet<string>> GetAllowedGroupsAsync(GeolocationConnection connection)
		{
			var subscriptions = connection.GetSubscriptions();
			var departmentId = connection.DepartmentId;
			var viewer = connection.UserId;
			var groups = new HashSet<string>();

			if (subscriptions.DepartmentMap)
			{
				groups.Add(GeolocationGroups.Department(departmentId));

				if (viewer != null)
				{
					groups.Add(GeolocationGroups.Self(departmentId, viewer));

					foreach (var setKey in await _locationVisibilityService.GetVisibilitySetKeysForViewerAsync(departmentId, viewer))
						groups.Add(GeolocationGroups.VisibilitySet(departmentId, setKey));
				}
			}

			// A tracker that loses access keeps its subscription and regains the group if access returns.
			foreach (var unitId in subscriptions.Units)
			{
				if (await _locationVisibilityService.CanViewUnitLocationAsync(departmentId, unitId, viewer))
					groups.Add(GeolocationGroups.Unit(unitId.ToString()));
			}

			foreach (var userId in subscriptions.People)
			{
				if (await _locationVisibilityService.CanViewPersonnelLocationAsync(departmentId, userId, viewer))
					groups.Add(GeolocationGroups.Person(departmentId, userId));
			}

			return groups;
		}
	}
}
