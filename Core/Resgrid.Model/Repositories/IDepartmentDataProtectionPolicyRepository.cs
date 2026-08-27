using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentDataProtectionPolicyRepository : IRepository<DepartmentDataProtectionPolicy>
	{
		Task<DepartmentDataProtectionPolicy> GetByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Compare-and-swap state transition: moves the department's policy from
		/// <paramref name="expectedState"/> to <paramref name="newState"/> only when the row still holds
		/// the expected state, so two concurrent commands cannot both win a transition. Returns the
		/// number of rows affected (0 = lost the race / wrong state).
		/// </summary>
		Task<int> TryTransitionStateAsync(int departmentId, DepartmentDataProtectionState expectedState,
			DepartmentDataProtectionState newState, int? activeMigrationKind, string updatedByUserId,
			CancellationToken cancellationToken);

		/// <summary>
		/// Atomically increments the department's policy epoch (revoking outstanding grants) and returns
		/// the new epoch value; 0 when the department has no policy row.
		/// </summary>
		Task<long> IncrementPolicyEpochAsync(int departmentId, string updatedByUserId, CancellationToken cancellationToken);
	}
}
