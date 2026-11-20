using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Dedicated department numbers (DepartmentPhoneNumbers, M0323). Rows are read by department (GetAllByDepartmentIdAsync).</summary>
	public interface IDepartmentPhoneNumbersRepository : IRepository<DepartmentPhoneNumber>
	{
		/// <summary>The rows holding this E.164 number in one of the statuses, newest first.</summary>
		Task<List<DepartmentPhoneNumber>> GetByNumberAsync(string phoneNumber, IReadOnlyCollection<int> statuses, CancellationToken cancellationToken = default);

		/// <summary>
		/// Every row in one of the statuses (worker 80 lifecycle). Bounded by what departments actually hold: only Provisioning, Active
		/// and Lapsed rows are ever asked for, never released or failed history.
		/// </summary>
		Task<List<DepartmentPhoneNumber>> GetByStatusesAsync(IReadOnlyCollection<int> statuses, CancellationToken cancellationToken = default);
	}
}
