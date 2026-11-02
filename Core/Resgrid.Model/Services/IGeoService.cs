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
		/// Routed drive time through the install's routing provider for a vehicle with size and weight limits (plan §10.3);
		/// -1 when only a straight-line estimate is available.
		/// </summary>
		Task<double> GetEtaInSecondsAsync(string start, string destination, VehicleProfile vehicle);

		/// <summary>
		/// Routed drive times from several origins to one destination in one matrix request (plan §6.2). Each entry is the
		/// time for the origin at the same index, or -1 when it could not be routed (callers keep their own estimate).
		/// </summary>
		Task<List<double>> GetEtasInSecondsAsync(IReadOnlyList<string> origins, string destination, VehicleProfile vehicle = null);

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
		/// Groups of any type (Station or Organizational) whose boundary polygon contains the point.
		/// Groups without a parseable boundary are skipped. Nested boundaries all match, so a point
		/// inside a station's area under a service area returns both groups.
		/// </summary>
		Task<List<DepartmentGroup>> GetGroupsWithBoundaryContainingPointAsync(int departmentId, double latitude, double longitude);

		/// <summary>
		/// All station groups with resolvable coordinates ordered by straight-line
		/// distance to the point (nearest first), with geofence containment flagged.
		/// </summary>
		Task<List<StationDistanceResult>> OrderStationsByDistanceAsync(int departmentId, double latitude, double longitude);
	}
}
