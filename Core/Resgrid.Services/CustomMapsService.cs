using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class CustomMapsService : ICustomMapsService
	{
		private readonly ICustomMapsRepository _customMapsRepository;
		private readonly ICustomMapFloorsRepository _customMapFloorsRepository;
		private readonly ICustomMapZonesRepository _customMapZonesRepository;
		private readonly ICustomMapImageService _customMapImageService;

		public CustomMapsService(
			ICustomMapsRepository customMapsRepository,
			ICustomMapFloorsRepository customMapFloorsRepository,
			ICustomMapZonesRepository customMapZonesRepository,
			ICustomMapImageService customMapImageService)
		{
			_customMapsRepository = customMapsRepository;
			_customMapFloorsRepository = customMapFloorsRepository;
			_customMapZonesRepository = customMapZonesRepository;
			_customMapImageService = customMapImageService;
		}

		// ── Maps ─────────────────────────────────────────────────────────────────

		public async Task<CustomMap> SaveCustomMapAsync(CustomMap customMap, CancellationToken cancellationToken = default)
		{
			return await _customMapsRepository.SaveOrUpdateAsync(customMap, cancellationToken);
		}

		public async Task<List<CustomMap>> GetCustomMapsForDepartmentAsync(int departmentId)
		{
			var result = await _customMapsRepository.GetCustomMapsByDepartmentIdAsync(departmentId);
			return result?.ToList() ?? new List<CustomMap>();
		}

		public async Task<CustomMap> GetCustomMapByIdAsync(string customMapId)
		{
			return await _customMapsRepository.GetCustomMapByIdWithFloorsAsync(customMapId);
		}

		public async Task<bool> DeleteCustomMapAsync(string customMapId, CancellationToken cancellationToken = default)
		{
			try
			{
				var floors = await _customMapFloorsRepository.GetFloorsByMapIdAsync(customMapId);
				if (floors != null)
				{
					foreach (var floor in floors)
					{
						var zones = await _customMapZonesRepository.GetZonesByFloorIdAsync(floor.CustomMapFloorId);
						if (zones != null)
						{
							foreach (var zone in zones)
								await _customMapZonesRepository.DeleteAsync(zone, cancellationToken);
						}

						await _customMapFloorsRepository.DeleteAsync(floor, cancellationToken);
					}
				}

				var map = await _customMapsRepository.GetByIdAsync(customMapId);
				if (map != null)
					return await _customMapsRepository.DeleteAsync(map, cancellationToken);

				return true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		// ── Floors ────────────────────────────────────────────────────────────────

		public async Task<CustomMapFloor> SaveFloorAsync(CustomMapFloor floor, CancellationToken cancellationToken = default)
		{
			return await _customMapFloorsRepository.SaveOrUpdateAsync(floor, cancellationToken);
		}

		public async Task<List<CustomMapFloor>> GetFloorsForMapAsync(string customMapId)
		{
			var result = await _customMapFloorsRepository.GetFloorsByMapIdAsync(customMapId);
			return result?.ToList() ?? new List<CustomMapFloor>();
		}

		public async Task<CustomMapFloor> GetFloorByIdAsync(string customMapFloorId)
		{
			return await _customMapFloorsRepository.GetFloorByIdWithZonesAsync(customMapFloorId);
		}

		public async Task<bool> DeleteFloorAsync(string customMapFloorId, CancellationToken cancellationToken = default)
		{
			try
			{
				var zones = await _customMapZonesRepository.GetZonesByFloorIdAsync(customMapFloorId);
				if (zones != null)
				{
					foreach (var zone in zones)
						await _customMapZonesRepository.DeleteAsync(zone, cancellationToken);
				}

				var floor = await _customMapFloorsRepository.GetByIdAsync(customMapFloorId);
				if (floor != null)
					return await _customMapFloorsRepository.DeleteAsync(floor, cancellationToken);

				return true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		// ── Zones ─────────────────────────────────────────────────────────────────

		public async Task<CustomMapZone> SaveZoneAsync(CustomMapZone zone, CancellationToken cancellationToken = default)
		{
			return await _customMapZonesRepository.SaveOrUpdateAsync(zone, cancellationToken);
		}

		public async Task<List<CustomMapZone>> GetZonesForFloorAsync(string customMapFloorId)
		{
			var result = await _customMapZonesRepository.GetZonesByFloorIdAsync(customMapFloorId);
			return result?.ToList() ?? new List<CustomMapZone>();
		}

		public async Task<CustomMapZone> GetZoneByIdAsync(string customMapZoneId)
		{
			return await _customMapZonesRepository.GetByIdAsync(customMapZoneId);
		}

		public async Task<bool> DeleteZoneAsync(string customMapZoneId, CancellationToken cancellationToken = default)
		{
			try
			{
				var zone = await _customMapZonesRepository.GetByIdAsync(customMapZoneId);
				if (zone != null)
					return await _customMapZonesRepository.DeleteAsync(zone, cancellationToken);

				return true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		// ── Coordinate Resolution ─────────────────────────────────────────────────

		public async Task<CustomMapZone> ResolveCoordinateToZoneAsync(string customMapFloorId, double latitude, double longitude)
		{
			try
			{
				var zones = await _customMapZonesRepository.GetZonesByFloorIdAsync(customMapFloorId);
				if (zones == null)
					return null;

				foreach (var zone in zones.Where(z => z.IsActive && !string.IsNullOrWhiteSpace(z.PolygonGeoJson)))
				{
					if (IsPointInPolygon(latitude, longitude, zone.PolygonGeoJson))
						return zone;
				}

				return null;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}

		public async Task<List<CustomMapZone>> GetSearchableZonesByMapIdAsync(string customMapId)
		{
			var result = await _customMapZonesRepository.GetSearchableZonesByMapIdAsync(customMapId);
			return result?.ToList() ?? new List<CustomMapZone>();
		}

		// ── Floor Image Management ─────────────────────────────────────────────

		public async Task<CustomMapFloor> UploadFloorImageAsync(
			string customMapFloorId,
			IFormFile image,
			int departmentId,
			string tileBasePath,
			string tileBaseUrlTemplate,
			string imageUrlTemplate,
			CancellationToken cancellationToken = default)
		{
			try
			{
				var floor = await _customMapFloorsRepository.GetByIdAsync(customMapFloorId);
				if (floor == null) return null;

				await _customMapImageService.SaveFloorImageAsync(
					floor, image, departmentId, tileBasePath, tileBaseUrlTemplate, cancellationToken);

				// For DatabaseBlob strategy, set the ImageUrl after we know the file id
				if ((CustomMapFloorStorageType)floor.StorageType == CustomMapFloorStorageType.DatabaseBlob
				    && floor.ImageFileId.HasValue
				    && !string.IsNullOrWhiteSpace(imageUrlTemplate))
				{
					floor.ImageUrl = imageUrlTemplate.Replace("{fileId}", floor.ImageFileId.Value.ToString());
				}

				return await _customMapFloorsRepository.SaveOrUpdateAsync(floor, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}

		public async Task<bool> DeleteFloorImageAsync(
			string customMapFloorId,
			string tileBasePath,
			CancellationToken cancellationToken = default)
		{
			try
			{
				var floor = await _customMapFloorsRepository.GetByIdAsync(customMapFloorId);
				if (floor == null) return false;

				await _customMapImageService.DeleteFloorImageAsync(floor, tileBasePath, cancellationToken);
				await _customMapFloorsRepository.SaveOrUpdateAsync(floor, cancellationToken);
				return true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		// ── Private Helpers ───────────────────────────────────────────────────────

		private static bool IsPointInPolygon(double latitude, double longitude, string polygonGeoJson)
		{
			try
			{
				var token = JToken.Parse(polygonGeoJson);

				if (token["type"]?.ToString() == "Feature")
					token = token["geometry"];

				if (token["type"]?.ToString() != "Polygon")
					return false;

				var coordinates = token["coordinates"]?[0] as JArray;
				if (coordinates == null || coordinates.Count < 3)
					return false;

				var ring = coordinates
					.Select(c => (lng: c[0].Value<double>(), lat: c[1].Value<double>()))
					.ToList();

				return RayCast(latitude, longitude, ring);
			}
			catch
			{
				return false;
			}
		}

		private static bool RayCast(double lat, double lng, System.Collections.Generic.List<(double lng, double lat)> ring)
		{
			var inside = false;
			var n = ring.Count;
			for (int i = 0, j = n - 1; i < n; j = i++)
			{
				var xi = ring[i].lng; var yi = ring[i].lat;
				var xj = ring[j].lng; var yj = ring[j].lat;

				var intersect = (yi > lat) != (yj > lat) &&
								lng < (xj - xi) * (lat - yi) / (yj - yi) + xi;
				if (intersect)
					inside = !inside;
			}

			return inside;
		}
	}
}

