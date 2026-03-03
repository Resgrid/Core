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
	}
}

