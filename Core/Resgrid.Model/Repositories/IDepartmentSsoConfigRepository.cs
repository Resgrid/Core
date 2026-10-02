using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for <see cref="DepartmentSsoConfig"/> entities.
	/// </summary>
	public interface IDepartmentSsoConfigRepository : IRepository<DepartmentSsoConfig>
	{
		/// <summary>Returns all SSO configurations for a department.</summary>
		Task<IEnumerable<DepartmentSsoConfig>> GetAllByDepartmentIdAsync(int departmentId);

		/// <summary>Returns the SSO config for a specific department and provider type.</summary>
		Task<DepartmentSsoConfig> GetByDepartmentIdAndTypeAsync(int departmentId, SsoProviderType providerType);

		/// <summary>Returns the SSO config matching the given SAML EntityId (for SP-initiated SAML lookups).</summary>
		Task<DepartmentSsoConfig> GetByEntityIdAsync(string entityId);

		/// <summary>
		/// Advances the provider step-up mapping version and clears its test result in one statement (passkey plan section
		/// 7.8: every change needs a new test). Returns the new version.
		/// </summary>
		Task<long> AdvanceFederatedMfaMappingVersionAsync(string departmentSsoConfigId, System.Threading.CancellationToken cancellationToken = default);

		/// <summary>Records a passed test only if the mapping is still at <paramref name="version"/>; false when it changed.</summary>
		Task<bool> TryRecordFederatedMfaTestAsync(string departmentSsoConfigId, long version, string userId, System.DateTime utcNow,
			System.Threading.CancellationToken cancellationToken = default);
	}
}

