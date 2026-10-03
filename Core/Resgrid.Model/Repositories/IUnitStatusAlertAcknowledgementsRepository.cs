using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IUnitStatusAlertAcknowledgementsRepository : IRepository<UnitStatusAlertAcknowledgement>
	{
		/// <summary>
		/// The uncleared acknowledgements for the given status episodes. Callers pass the units' current state ids,
		/// so acknowledgements from episodes that have already ended are never read.
		/// </summary>
		Task<IEnumerable<UnitStatusAlertAcknowledgement>> GetActiveForUnitStatesAsync(int departmentId, IEnumerable<int> unitStateIds);

		/// <summary>The uncleared acknowledgements for one status episode (normally zero or one).</summary>
		Task<IEnumerable<UnitStatusAlertAcknowledgement>> GetActiveForUnitStateAsync(int departmentId, int unitId, int unitStateId);
	}
}
