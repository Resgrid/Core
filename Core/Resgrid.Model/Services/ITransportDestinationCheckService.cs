using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Transport destination checks (<see cref="DepartmentSettingTypes.TransportDestinationChecks"/>, rules in
	/// <see cref="TransportDestinationRules"/>): records on the call when a unit's transport status goes to a destination other
	/// than the call's assigned one. Runs from <c>UnitsService</c> after a status is saved.
	/// </summary>
	public interface ITransportDestinationCheckService
	{
		/// <summary>
		/// After a unit status is saved: when the department turned the checks on, the status is a transport status (base type
		/// Transporting or At Hospital) whose destination is a POI other than its working call's assigned destination, and the
		/// unit's previous status was not already headed to that POI, adds a call note naming both destinations and the reason
		/// (the status note). Never throws: the status is already saved.
		/// </summary>
		/// <returns>The note added, or null when there was nothing to record.</returns>
		Task<CallNote> OnUnitStateSavedAsync(int departmentId, UnitState state, UnitState previousState, CancellationToken cancellationToken = default(CancellationToken));
	}
}
