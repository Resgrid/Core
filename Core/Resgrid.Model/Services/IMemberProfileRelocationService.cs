using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Moves a member's legacy global-profile data — identification number and home/mailing address
	/// — onto their department-scoped DepartmentMemberSensitiveData row (ADP plan section 5.1).
	///
	/// M0134 does the bulk of this in SQL at deploy time, but it can only touch rows that are still
	/// plaintext: writing a cleartext address into an enrolled department's row would poison it.
	/// This service is the path for everything SQL cannot do — departments already enrolled in ADP
	/// (the move goes through the ADP write pipeline and is enveloped as it lands), departments that
	/// enroll later (relocation runs as the first step of the encryption night, so nothing is left
	/// behind in the legacy location), and members who join after the migration ran.
	///
	/// Every pass is idempotent and non-destructive: a target field that already holds a value —
	/// plaintext or ciphertext — is never overwritten, and a member is marked relocated exactly once.
	/// </summary>
	public interface IMemberProfileRelocationService
	{
		/// <summary>
		/// Departments with at least one member whose legacy data has not been relocated. Reads empty
		/// once the move is complete, which is the precondition for the contract migration that drops
		/// the legacy columns.
		/// </summary>
		Task<IReadOnlyList<int>> GetDepartmentIdsWithOutstandingDataAsync();

		/// <summary>
		/// Relocates every unmarked member of one department. Safe to call at any point in the ADP
		/// state machine: values land through the normal write path, so they are enveloped whenever
		/// the department is encrypting new writes, regardless of where a migration cursor sits.
		/// </summary>
		Task<MemberProfileRelocationResult> RelocateDepartmentAsync(int departmentId,
			CancellationToken cancellationToken = default);
	}
}
