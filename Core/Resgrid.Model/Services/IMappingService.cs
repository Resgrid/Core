using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IMappingService
	{
		/// <summary>
		/// Saves the poi type asynchronous.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;PoiType&gt;.</returns>
		Task<PoiType> SavePOITypeAsync(PoiType type, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Department Import quiet overload: the side effects named by <paramref name="effects"/> are skipped or deferred to the import engine.</summary>
		Task<PoiType> SavePOITypeAsync(PoiType type, Onboarding.ImportEffectPolicy effects, CancellationToken cancellationToken);

		/// <summary>
		/// Saves the poi asynchronous.
		/// </summary>
		/// <param name="poi">The poi.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Poi&gt;.</returns>
		Task<Poi> SavePOIAsync(Poi poi, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Department Import quiet overload: the side effects named by <paramref name="effects"/> are skipped or deferred to the import engine.</summary>
		Task<Poi> SavePOIAsync(Poi poi, Onboarding.ImportEffectPolicy effects, CancellationToken cancellationToken);

		/// <summary>
		/// Gets the poi types for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;PoiType&gt;&gt;.</returns>
		Task<List<PoiType>> GetPOITypesForDepartmentAsync(int departmentId);

		Task<List<Poi>> GetPOIsForDepartmentAsync(int departmentId);

		Task<List<Poi>> GetDestinationPOIsForDepartmentAsync(int departmentId);

		Task<Poi> GetPOIByIdAsync(int poiId);

		Task<Poi> GetDestinationPOIByIdAsync(int departmentId, int poiId);

		/// <summary>The department's POI capabilities (e.g. "Cath Lab"), by sort order then name.</summary>
		Task<List<PoiCapability>> GetPoiCapabilitiesForDepartmentAsync(int departmentId);

		/// <summary>One of the department's POI capabilities, or null when it is not the department's.</summary>
		Task<PoiCapability> GetPoiCapabilityByIdAsync(int departmentId, string poiCapabilityId);

		/// <summary>
		/// Adds or renames a capability. The name is trimmed and must be unique in the department (case-insensitive); a
		/// duplicate or blank name is an ArgumentException. A capability of another department is an ArgumentException.
		/// </summary>
		Task<PoiCapability> SavePoiCapabilityAsync(PoiCapability capability, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// The department's capabilities with these names (case-insensitive), adding the missing ones (POI import). Blank
		/// names are skipped and names are cut to <see cref="PoiCapability.MaxNameLength"/>.
		/// </summary>
		Task<List<PoiCapability>> GetOrAddPoiCapabilitiesByNameAsync(int departmentId, IEnumerable<string> names, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Deletes the capability, takes it off every POI and out of every availability report.</summary>
		Task<bool> DeletePoiCapabilityAsync(int departmentId, string poiCapabilityId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>The ids of the capabilities the POI offers.</summary>
		Task<List<string>> GetPoiCapabilityIdsForPoiAsync(int departmentId, int poiId);

		/// <summary>Makes the given capabilities (ids of the department's capabilities; others are ignored) the POI's.</summary>
		Task SetPoiCapabilitiesAsync(int departmentId, int poiId, IEnumerable<string> poiCapabilityIds, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Fills <see cref="Poi.CapabilityIds"/>, <see cref="Poi.Capabilities"/> and <see cref="Poi.Availability"/> (current
		/// reports only) on the department's POIs. Two queries at most, one when the department has no capabilities.
		/// </summary>
		Task PopulatePoiDetailsAsync(int departmentId, IEnumerable<Poi> pois);

		/// <summary>The department's current availability reports (not open, not expired), by POI id.</summary>
		Task<Dictionary<int, PoiAvailability>> GetCurrentPoiAvailabilitiesAsync(int departmentId);

		/// <summary>
		/// Sets a destination's availability. Open (or an expiry in the past) deletes its report. Unavailable capabilities
		/// are limited to the department's; the note is trimmed and cut to <see cref="PoiAvailability.MaxNoteLength"/>; an
		/// expiry is capped at <see cref="PoiAvailabilityRules.MaxExpiry"/> from now. A POI of another department, or one that
		/// is not a destination, is an ArgumentException. Returns the current report, or null when open.
		/// </summary>
		Task<PoiAvailability> SetPoiAvailabilityAsync(int departmentId, int poiId, PoiAvailabilityStatuses status, IEnumerable<string> unavailableCapabilityIds,
			string note, DateTime? expiresOnUtc, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the type by identifier asynchronous.
		/// </summary>
		/// <param name="poiTypeId">The poi type identifier.</param>
		/// <returns>Task&lt;PoiType&gt;.</returns>
		Task<PoiType> GetTypeByIdAsync(int poiTypeId);

		/// <summary>
		/// Deletes the poi type asynchronous.
		/// </summary>
		/// <param name="poiTypeId">The poi type identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeletePOITypeAsync(int poiTypeId, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> DeletePOIAsync(int poiId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes many POIs of one type at once (the same removal as <see cref="DeletePOIAsync"/>) and returns how many were deleted.
		/// Ids outside the type are ignored. When <paramref name="expectedCount"/> is given it must equal the number of listed POIs in
		/// the type, or nothing is deleted (ArgumentException). A type of another department is an ArgumentException.
		/// </summary>
		Task<int> DeletePOIsAsync(int departmentId, int poiTypeId, IEnumerable<int> poiIds, int? expectedCount, CancellationToken cancellationToken = default(CancellationToken));

		Task<MapLayer> SaveMapLayerAsync(MapLayer mapLayer);

		Task<List<MapLayer>> GetMapLayersForTypeDepartmentAsync(int departmentId, MapLayerTypes type);

		Task<MapLayer> GetMapLayersByIdAsync(string id);
	}
}
