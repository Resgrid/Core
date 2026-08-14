using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IGeoService
	/// </summary>
	public interface IGeoService
	{
		/// <summary>
		/// Gets the personnel eta in seconds asynchronous.
		/// </summary>
		/// <param name="log">The log.</param>
		/// <returns>Task&lt;System.Double&gt;.</returns>
		Task<double> GetPersonnelEtaInSecondsAsync(ActionLog log);

		/// <summary>
		/// Gets the eta in seconds asynchronous.
		/// </summary>
		/// <param name="start">The start.</param>
		/// <param name="destination">The destination.</param>
		/// <returns>Task&lt;System.Double&gt;.</returns>
		Task<double> GetEtaInSecondsAsync(string start, string destination);

		/// <summary>
		/// Best-effort coordinates for a station group: its stored Latitude/Longitude
		/// first, then the centroid of its geofence polygon, then the geocoded group
		/// address (GetMapCenterCoordinatesForGroupAsync fallback chain). Null when
		/// nothing usable exists.
		/// </summary>
		Task<GeoMath.GeoPoint?> GetStationCoordinatesAsync(DepartmentGroup group);

		/// <summary>
		/// Station groups whose geofence polygon contains the point, nearest first.
		/// Stations without a parseable geofence are skipped.
		/// </summary>
		Task<List<StationDistanceResult>> GetStationsContainingPointAsync(int departmentId, double latitude, double longitude);

		/// <summary>
		/// All station groups with resolvable coordinates ordered by straight-line
		/// distance to the point (nearest first), with geofence containment flagged.
		/// </summary>
		Task<List<StationDistanceResult>> OrderStationsByDistanceAsync(int departmentId, double latitude, double longitude);
	}
}
