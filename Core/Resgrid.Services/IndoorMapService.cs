using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class IndoorMapService : IIndoorMapService
	{
		private readonly IIndoorMapsRepository _indoorMapsRepository;
		private readonly IIndoorMapFloorsRepository _indoorMapFloorsRepository;
		private readonly IIndoorMapZonesRepository _indoorMapZonesRepository;

		public IndoorMapService(IIndoorMapsRepository indoorMapsRepository,
			IIndoorMapFloorsRepository indoorMapFloorsRepository,
			IIndoorMapZonesRepository indoorMapZonesRepository)
		{
			_indoorMapsRepository = indoorMapsRepository;
			_indoorMapFloorsRepository = indoorMapFloorsRepository;
			_indoorMapZonesRepository = indoorMapZonesRepository;
		}

		#region Indoor Maps CRUD

		public async Task<IndoorMap> SaveIndoorMapAsync(IndoorMap indoorMap, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _indoorMapsRepository.SaveOrUpdateAsync(indoorMap, cancellationToken);
		}

		public async Task<IndoorMap> GetIndoorMapByIdAsync(string indoorMapId)
		{
			return await _indoorMapsRepository.GetByIdAsync(indoorMapId);
		}

		public async Task<List<IndoorMap>> GetIndoorMapsForDepartmentAsync(int departmentId)
		{
			var maps = await _indoorMapsRepository.GetIndoorMapsByDepartmentIdAsync(departmentId);

			if (maps != null)
				return maps.Where(x => !x.IsDeleted).ToList();

			return new List<IndoorMap>();
		}

		public async Task<bool> DeleteIndoorMapAsync(string indoorMapId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var map = await _indoorMapsRepository.GetByIdAsync(indoorMapId);

			if (map == null)
				return false;

			map.IsDeleted = true;
			await _indoorMapsRepository.SaveOrUpdateAsync(map, cancellationToken);

			return true;
		}

		#endregion Indoor Maps CRUD

		#region Floors CRUD

		public async Task<IndoorMapFloor> SaveFloorAsync(IndoorMapFloor floor, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _indoorMapFloorsRepository.SaveOrUpdateAsync(floor, cancellationToken);
		}

		public async Task<IndoorMapFloor> GetFloorByIdAsync(string floorId)
		{
			return await _indoorMapFloorsRepository.GetByIdAsync(floorId);
		}

		public async Task<List<IndoorMapFloor>> GetFloorsForMapAsync(string indoorMapId)
		{
			var floors = await _indoorMapFloorsRepository.GetFloorsByIndoorMapIdAsync(indoorMapId);

			if (floors != null)
				return floors.Where(x => !x.IsDeleted).OrderBy(x => x.FloorOrder).ToList();

			return new List<IndoorMapFloor>();
		}

		public async Task<bool> DeleteFloorAsync(string floorId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var floor = await _indoorMapFloorsRepository.GetByIdAsync(floorId);

			if (floor == null)
				return false;

			floor.IsDeleted = true;
			await _indoorMapFloorsRepository.SaveOrUpdateAsync(floor, cancellationToken);

			return true;
		}

		#endregion Floors CRUD

		#region Zones CRUD

		public async Task<IndoorMapZone> SaveZoneAsync(IndoorMapZone zone, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _indoorMapZonesRepository.SaveOrUpdateAsync(zone, cancellationToken);
		}

		public async Task<IndoorMapZone> GetZoneByIdAsync(string zoneId)
		{
			return await _indoorMapZonesRepository.GetByIdAsync(zoneId);
		}

		public async Task<List<IndoorMapZone>> GetZonesForFloorAsync(string floorId)
		{
			var zones = await _indoorMapZonesRepository.GetZonesByFloorIdAsync(floorId);

			if (zones != null)
				return zones.Where(x => !x.IsDeleted).ToList();

			return new List<IndoorMapZone>();
		}

		public async Task<bool> DeleteZoneAsync(string zoneId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var zone = await _indoorMapZonesRepository.GetByIdAsync(zoneId);

			if (zone == null)
				return false;

			zone.IsDeleted = true;
			await _indoorMapZonesRepository.SaveOrUpdateAsync(zone, cancellationToken);

			return true;
		}

		#endregion Zones CRUD

		#region Dispatch Integration

		public async Task<List<IndoorMapZone>> SearchZonesAsync(int departmentId, string searchTerm)
		{
			var zones = await _indoorMapZonesRepository.SearchZonesAsync(departmentId, searchTerm);

			if (zones != null)
				return zones.ToList();

			return new List<IndoorMapZone>();
		}

		public async Task<string> GetZoneDisplayNameAsync(string zoneId)
		{
			var zone = await _indoorMapZonesRepository.GetByIdAsync(zoneId);

			if (zone == null)
				return null;

			var floor = await _indoorMapFloorsRepository.GetByIdAsync(zone.IndoorMapFloorId);

			if (floor == null)
				return zone.Name;

			var map = await _indoorMapsRepository.GetByIdAsync(floor.IndoorMapId);

			if (map == null)
				return $"{floor.Name} > {zone.Name}";

			return $"{map.Name} > {floor.Name} > {zone.Name}";
		}

		#endregion Dispatch Integration
	}
}
