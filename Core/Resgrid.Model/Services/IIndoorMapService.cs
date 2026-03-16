using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IIndoorMapService
	{
		// Indoor Maps CRUD
		Task<IndoorMap> SaveIndoorMapAsync(IndoorMap indoorMap, CancellationToken cancellationToken = default(CancellationToken));
		Task<IndoorMap> GetIndoorMapByIdAsync(string indoorMapId);
		Task<List<IndoorMap>> GetIndoorMapsForDepartmentAsync(int departmentId);
		Task<bool> DeleteIndoorMapAsync(string indoorMapId, CancellationToken cancellationToken = default(CancellationToken));

		// Floors CRUD
		Task<IndoorMapFloor> SaveFloorAsync(IndoorMapFloor floor, CancellationToken cancellationToken = default(CancellationToken));
		Task<IndoorMapFloor> GetFloorByIdAsync(string floorId);
		Task<List<IndoorMapFloor>> GetFloorsForMapAsync(string indoorMapId);
		Task<bool> DeleteFloorAsync(string floorId, CancellationToken cancellationToken = default(CancellationToken));

		// Zones CRUD
		Task<IndoorMapZone> SaveZoneAsync(IndoorMapZone zone, CancellationToken cancellationToken = default(CancellationToken));
		Task<IndoorMapZone> GetZoneByIdAsync(string zoneId);
		Task<List<IndoorMapZone>> GetZonesForFloorAsync(string floorId);
		Task<bool> DeleteZoneAsync(string zoneId, CancellationToken cancellationToken = default(CancellationToken));

		// Dispatch integration
		Task<List<IndoorMapZone>> SearchZonesAsync(int departmentId, string searchTerm);
		Task<string> GetZoneDisplayNameAsync(string zoneId);
	}
}
