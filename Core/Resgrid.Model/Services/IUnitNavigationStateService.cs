using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Live ETAs of units navigating in the Unit app (maplibre-mapping-routing-navigation-plan.md §9.4): the latest report
	/// per unit in Redis (two-minute lifetime), broadcast to the department as <c>unitNavigationUpdated</c>.
	/// </summary>
	public interface IUnitNavigationStateService
	{
		/// <summary>Stores the report (Arrived and Ended clear the unit's state instead) and announces the change.</summary>
		Task<UnitNavigationState> ReportAsync(UnitNavigationState report);

		Task<UnitNavigationState> GetForUnitAsync(int departmentId, int unitId);

		/// <summary>Every unit of the department navigating now.</summary>
		Task<List<UnitNavigationState>> GetActiveAsync(int departmentId);

		/// <summary>Ends the unit's navigation state (no-op when there is none) and announces it.</summary>
		Task ClearAsync(int departmentId, int unitId);

		/// <summary>
		/// Clears a call navigation when the unit reports a status whose base type ends the response (On Scene, Available,
		/// In Quarters, Unavailable/Out of Service, Cleared/Released, Returning).
		/// </summary>
		Task ClearForUnitStatusAsync(int departmentId, int unitId, int statusBaseType);
	}
}
