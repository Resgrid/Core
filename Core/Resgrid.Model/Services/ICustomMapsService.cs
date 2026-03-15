using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Resgrid.Model.Services
{
	public interface ICustomMapsService
	{
		// ── Maps ────────────────────────────────────────────────────────────────

		/// <summary>
		/// Saves (insert or update) a custom map.
		/// </summary>
		Task<CustomMap> SaveCustomMapAsync(CustomMap customMap, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets all active custom maps for a department.
		/// </summary>
		Task<List<CustomMap>> GetCustomMapsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets a custom map by id, including its floors.
		/// </summary>
		Task<CustomMap> GetCustomMapByIdAsync(string customMapId);

		/// <summary>
		/// Deletes a custom map and all child floors/zones.
		/// </summary>
		Task<bool> DeleteCustomMapAsync(string customMapId, CancellationToken cancellationToken = default);

		// ── Floors ──────────────────────────────────────────────────────────────

		/// <summary>
		/// Saves (insert or update) a floor within a custom map.
		/// </summary>
		Task<CustomMapFloor> SaveFloorAsync(CustomMapFloor floor, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets all floors for a custom map ordered by SortOrder.
		/// </summary>
		Task<List<CustomMapFloor>> GetFloorsForMapAsync(string customMapId);

		/// <summary>
		/// Gets a single floor with its zones.
		/// </summary>
		Task<CustomMapFloor> GetFloorByIdAsync(string customMapFloorId);

		/// <summary>
		/// Deletes a floor and all its zones.
		/// </summary>
		Task<bool> DeleteFloorAsync(string customMapFloorId, CancellationToken cancellationToken = default);

		// ── Zones ───────────────────────────────────────────────────────────────

		/// <summary>
		/// Saves (insert or update) a zone on a floor.
		/// </summary>
		Task<CustomMapZone> SaveZoneAsync(CustomMapZone zone, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets all zones for a given floor.
		/// </summary>
		Task<List<CustomMapZone>> GetZonesForFloorAsync(string customMapFloorId);

		/// <summary>
		/// Gets a single zone by id.
		/// </summary>
		Task<CustomMapZone> GetZoneByIdAsync(string customMapZoneId);

		/// <summary>
		/// Deletes a zone.
		/// </summary>
		Task<bool> DeleteZoneAsync(string customMapZoneId, CancellationToken cancellationToken = default);

		// ── Coordinate Resolution ───────────────────────────────────────────────

		/// <summary>
		/// Point-in-polygon hit test: given a latitude and longitude, returns the matching zone
		/// (or null if no zone contains the point) for the specified floor.
		/// Uses geo-projected coordinates for GPS interoperability.
		/// </summary>
		Task<CustomMapZone> ResolveCoordinateToZoneAsync(string customMapFloorId, double latitude, double longitude);

		/// <summary>
		/// Gets all searchable zones across all floors of a map (for name/coordinate search).
		/// </summary>
		Task<List<CustomMapZone>> GetSearchableZonesByMapIdAsync(string customMapId);

		// ── Floor Image Management ──────────────────────────────────────────────

		/// <summary>
		/// Uploads a background image for the given floor, selecting the correct storage
		/// strategy automatically (DatabaseBlob ≤ 10 MB, TiledPyramid otherwise).
		/// Persists the updated floor entity after saving.
		/// </summary>
		Task<CustomMapFloor> UploadFloorImageAsync(
			string customMapFloorId,
			IFormFile image,
			int departmentId,
			string tileBasePath,
			string tileBaseUrlTemplate,
			string imageUrlTemplate,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Removes the stored background image for the given floor and persists the change.
		/// </summary>
		Task<bool> DeleteFloorImageAsync(
			string customMapFloorId,
			string tileBasePath,
			CancellationToken cancellationToken = default);
	}
}
