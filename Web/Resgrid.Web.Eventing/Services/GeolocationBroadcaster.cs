using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Resgrid.Model.Services;
using Resgrid.Web.Eventing.Hubs;
using Resgrid.Web.Eventing.Hubs.Models;

namespace Resgrid.Web.Eventing.Services
{
	/// <summary>
	/// Addresses realtime location updates to the connections allowed to see them, mirroring the location
	/// visibility matrix checks the REST map applies. Eventing instances share the Rabbit queue, so the
	/// instance handling an event reaches the others' connections through groups on the Redis backplane:
	/// one group per event audience, never one send per viewer.
	/// </summary>
	public sealed class GeolocationBroadcaster
	{
		public const string UnitLocationUpdatedMethod = "onUnitLocationUpdated";
		public const string PersonnelLocationUpdatedMethod = "onPersonnelLocationUpdated";

		private readonly ILocationVisibilityService _locationVisibilityService;

		public GeolocationBroadcaster(ILocationVisibilityService locationVisibilityService)
		{
			_locationVisibilityService = locationVisibilityService;
		}

		public async Task SendUnitLocationAsync(IHubClients<IClientProxy> clients, int departmentId, UnitLocationUpdate location)
		{
			if (location == null || departmentId <= 0 || string.IsNullOrWhiteSpace(location.UnitId))
				return;

			var groups = await GetUnitLocationGroupsAsync(departmentId, location.UnitId);

			await clients.Groups(groups).SendAsync(UnitLocationUpdatedMethod, location);
		}

		public async Task SendPersonnelLocationAsync(IHubClients<IClientProxy> clients, int departmentId, PersonnelLocationUpdate location)
		{
			if (location == null || departmentId <= 0 || string.IsNullOrWhiteSpace(location.UserId))
				return;

			var groups = await GetPersonnelLocationGroupsAsync(departmentId, location.UserId);

			await clients.Groups(groups).SendAsync(PersonnelLocationUpdatedMethod, location);
		}

		// A connection in more than one of these groups gets the update more than once, which is harmless:
		// applying a fix is idempotent.
		public async Task<IReadOnlyList<string>> GetUnitLocationGroupsAsync(int departmentId, string unitId)
		{
			// Single-unit trackers are only in this group while the matrix lets them see the unit.
			var groups = new List<string> { GeolocationGroups.Unit(unitId) };

			// Unit ids are integers; one that is not cannot be checked, so it goes nowhere else.
			if (int.TryParse(unitId, out var parsedUnitId))
			{
				var audience = await _locationVisibilityService.GetUnitLocationAudienceAsync(departmentId, parsedUnitId);

				groups.Add(audience.IsEntireDepartment
					? GeolocationGroups.Department(departmentId)
					: GeolocationGroups.VisibilitySet(departmentId, audience.VisibilitySetKey));
			}

			return groups;
		}

		public async Task<IReadOnlyList<string>> GetPersonnelLocationGroupsAsync(int departmentId, string userId)
		{
			var groups = new List<string> { GeolocationGroups.Person(departmentId, userId) };
			var audience = await _locationVisibilityService.GetPersonnelLocationAudienceAsync(departmentId, userId);

			if (audience.IsEntireDepartment)
			{
				groups.Add(GeolocationGroups.Department(departmentId));
			}
			else
			{
				groups.Add(GeolocationGroups.VisibilitySet(departmentId, audience.VisibilitySetKey));
				// Everyone may see their own location even when the matrix does not list them.
				groups.Add(GeolocationGroups.Self(departmentId, userId));
			}

			return groups;
		}
	}
}
