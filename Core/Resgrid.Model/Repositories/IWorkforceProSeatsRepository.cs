using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Workforce Pro seats (the Workforce Pro migration): one row per member a manager switched Workforce Pro on for, per department.</summary>
	public interface IWorkforceProSeatsRepository : IRepository<WorkforceProSeat>
	{
		/// <summary>The department's seat rows, oldest first (the licensing order).</summary>
		Task<List<WorkforceProSeat>> GetForDepartmentAsync(int departmentId);

		/// <summary>Adds a seat unless the member already holds one (a repeat is not an error); true when a row was written.</summary>
		Task<bool> AddAsync(WorkforceProSeat seat, CancellationToken cancellationToken = default);

		/// <summary>Removes the members' seats in this department; the rows removed. Never touches a wellness record.</summary>
		Task<int> RemoveAsync(int departmentId, IEnumerable<string> userIds, CancellationToken cancellationToken = default);
	}
}
