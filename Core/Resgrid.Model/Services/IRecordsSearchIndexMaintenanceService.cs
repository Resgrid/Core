using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Worker command 44: keeps the records index in step with RmsRecordSearchProjections for every activated
	/// department. A generation-key change rebuilds the department; otherwise documents modified since the last
	/// sweep are re-indexed and soft-deleted projections are removed.
	/// </summary>
	public interface IRecordsSearchIndexMaintenanceService
	{
		Task<RecordsSearchIndexSweepResult> SweepAsync(CancellationToken cancellationToken = default);

		/// <summary>Forces a full rebuild of one department regardless of its generation key.</summary>
		Task<RecordsSearchIndexSweepResult> RebuildDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);
	}
}
