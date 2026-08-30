using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Department DEK version lifecycle (ADP plan sections 4.3 and 11.3): provision, activate,
	/// resolve by envelope version, retire after rotation. Key metadata only — this service never
	/// sees plaintext key material; wrapped blobs come from IKeyWrappingProvider and unwrapping
	/// happens exclusively inside the Protected Data Broker. Key rows are never deleted here;
	/// cryptographic erasure is a separate dual-controlled retention operation.
	/// </summary>
	public interface IDepartmentKeyService
	{
		/// <summary>The department's single Active key version, or null when none is provisioned.</summary>
		Task<DepartmentDataProtectionKey> GetActiveKeyAsync(int departmentId);

		/// <summary>Resolves the key row an rgdp envelope references; null when unknown (fail closed upstream).</summary>
		Task<DepartmentDataProtectionKey> GetKeyByVersionAsync(int departmentId, int version);

		/// <summary>
		/// Provisions the next key version for the department (version 1 at enrollment, N+1 at
		/// rotation): generates a wrapped DEK via the provider, persists it Pending, then activates it
		/// and moves any previously Active version to Retiring. Returns the new Active row. Idempotent
		/// guard: refuses (returns the existing row) when a Pending/Active row already exists at the
		/// computed version.
		/// </summary>
		Task<DepartmentDataProtectionKey> ProvisionNextKeyVersionAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Every key version the department has ever held, in any status. Used by rotation to find the
		/// superseded versions it may retire, and by support to answer "which versions exist" without
		/// touching key material.
		/// </summary>
		Task<IReadOnlyList<DepartmentDataProtectionKey>> GetAllVersionsAsync(int departmentId);

		/// <summary>
		/// Marks a Retiring version Retired once rotation re-encryption has verified no envelope still
		/// references it. Never deletes the row.
		/// </summary>
		Task<bool> RetireKeyVersionAsync(int departmentId, int version, CancellationToken cancellationToken = default);
	}
}
