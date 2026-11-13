using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// A department's POI capabilities (PoiCapabilities, M0315) and which POIs offer them (PoiCapabilityAssignments). Every
	/// query begins at DepartmentId.
	/// </summary>
	public interface IPoiCapabilitiesRepository : IRepository<PoiCapability>
	{
		Task<IEnumerable<PoiCapabilityAssignment>> GetAssignmentsByDepartmentIdAsync(int departmentId);

		Task<IEnumerable<PoiCapabilityAssignment>> GetAssignmentsByPoiIdAsync(int departmentId, int poiId);

		/// <summary>Makes <paramref name="capabilityIds"/> the POI's capabilities, in one transaction.</summary>
		Task ReplacePoiAssignmentsAsync(int departmentId, int poiId, IEnumerable<string> capabilityIds, CancellationToken cancellationToken = default);

		/// <summary>Removes every capability from the POIs (they are being deleted). Returns the rows removed.</summary>
		Task<int> DeleteAssignmentsForPoisAsync(int departmentId, IEnumerable<int> poiIds, CancellationToken cancellationToken = default);

		/// <summary>Takes the capability off every POI (it is being deleted). Returns the rows removed.</summary>
		Task<int> DeleteAssignmentsForCapabilityAsync(int departmentId, string poiCapabilityId, CancellationToken cancellationToken = default);
	}

	/// <summary>Destination availability reports (PoiAvailabilities, M0315), one per POI. Every query begins at DepartmentId.</summary>
	public interface IPoiAvailabilitiesRepository : IRepository<PoiAvailability>
	{
		Task<PoiAvailability> GetByPoiIdAsync(int departmentId, int poiId);

		/// <summary>Removes the POIs' reports (they reopened or are being deleted). Returns the rows removed.</summary>
		Task<int> DeleteForPoisAsync(int departmentId, IEnumerable<int> poiIds, CancellationToken cancellationToken = default);

		/// <summary>
		/// Takes a deleted capability out of every report that names it. Throws <see cref="System.InvalidOperationException"/>
		/// when a report kept changing under it and still names the capability, so the capability is not deleted.
		/// </summary>
		Task RemoveCapabilityAsync(int departmentId, string poiCapabilityId, CancellationToken cancellationToken = default);
	}
}
