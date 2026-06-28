using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Manages incident-scoped ad-hoc resources (units and personnel created on the fly for non-Resgrid
	/// resources), unit rosters, and forming a unit from on-scene personnel (§3.10).
	/// </summary>
	public interface IIncidentResourcesService
	{
		// Ad-hoc units
		Task<IncidentAdHocUnit> CreateAdHocUnitAsync(IncidentAdHocUnit unit, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<IncidentAdHocUnit> GetAdHocUnitByIdAsync(string incidentAdHocUnitId);
		Task<List<IncidentAdHocUnit>> GetAdHocUnitsForCallAsync(int departmentId, int callId);
		Task<bool> ReleaseAdHocUnitAsync(int departmentId, string incidentAdHocUnitId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		// Ad-hoc personnel
		Task<IncidentAdHocPersonnel> CreateAdHocPersonnelAsync(IncidentAdHocPersonnel personnel, string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<IncidentAdHocPersonnel>> GetAdHocPersonnelForCallAsync(int departmentId, int callId);
		Task<bool> ReleaseAdHocPersonnelAsync(int departmentId, string incidentAdHocPersonnelId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		// Roster building
		Task<IncidentAdHocPersonnel> AssignPersonnelToUnitAsync(int departmentId, string incidentAdHocPersonnelId, int ridingResourceKind, string ridingResourceId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Forms a new ad-hoc unit and attaches the given ad-hoc personnel to it as its roster.</summary>
		Task<IncidentAdHocUnit> FormUnitFromPersonnelAsync(IncidentAdHocUnit unit, List<string> adHocPersonnelIds, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Offline-first delta pull for ad-hoc resources: returns the department's ad-hoc units and personnel whose
		/// ModifiedOn is newer than <paramref name="sinceUtc"/> (released rows included so the client reconciles them).
		/// Aggregated into the unified /Sync/Changes payload by SyncController. See offline-first-architecture.md.
		/// </summary>
		Task<(List<IncidentAdHocUnit> Units, List<IncidentAdHocPersonnel> Personnel)> GetAdHocChangesSinceAsync(int departmentId, System.DateTime sinceUtc);

		/// <summary>
		/// Returns all ACTIVE (non-released) ad-hoc units + personnel across the department's active incidents in one
		/// batched read (one scan per ad-hoc table), for the shift-start bundle — replaces the per-incident N+1 lookups.
		/// </summary>
		Task<(List<IncidentAdHocUnit> Units, List<IncidentAdHocPersonnel> Personnel)> GetActiveAdHocResourcesForDepartmentAsync(int departmentId);
	}
}
