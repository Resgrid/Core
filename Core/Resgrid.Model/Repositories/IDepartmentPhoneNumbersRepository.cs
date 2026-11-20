using System;
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

		/// <summary>
		/// Claims the row for a carrier release: sets ReleasedOn (the release began) only while the row is still in
		/// <paramref name="expectedStatus"/> and no release has begun. False when another writer got there first (a reactivation, or
		/// another release). A row whose release began is never reactivated (<see cref="TryReactivateAsync"/>).
		/// </summary>
		Task<bool> TryBeginReleaseAsync(string departmentPhoneNumberId, int expectedStatus, DateTime startedOn, CancellationToken cancellationToken = default);

		/// <summary>
		/// Lapsed → Active with the unpaid period's markers cleared (T0, release date, warnings), only while the row is still Lapsed and
		/// no release has begun. False when the row changed under the caller (released, being released, or already reactivated).
		/// </summary>
		Task<bool> TryReactivateAsync(string departmentPhoneNumberId, DateTime updatedOn, CancellationToken cancellationToken = default);
	}
}
