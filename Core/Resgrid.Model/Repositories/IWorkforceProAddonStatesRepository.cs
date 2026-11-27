using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>What the Workforce Pro gate last saw of each department's add-on (M0423): one row per department, for the dunning grace.</summary>
	public interface IWorkforceProAddonStatesRepository : IRepository<WorkforceProAddonState>
	{
		Task<WorkforceProAddonState> GetForDepartmentAsync(int departmentId);

		/// <summary>Inserts or replaces the department's row (one per department; a concurrent insert falls back to an update).</summary>
		Task SaveForDepartmentAsync(WorkforceProAddonState state, CancellationToken cancellationToken = default);
	}
}
