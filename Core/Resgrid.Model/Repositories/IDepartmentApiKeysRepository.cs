using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Department API keys (DepartmentApiKeys, M0265).</summary>
	public interface IDepartmentApiKeysRepository : IRepository<DepartmentApiKey>
	{
		Task<DepartmentApiKey> GetBySecretHashAsync(string secretHash);

		Task<List<DepartmentApiKey>> GetAllForDepartmentAsync(int departmentId);

		/// <summary>Records a use of the key without rewriting the rest of the row.</summary>
		Task MarkUsedAsync(string departmentApiKeyId, DateTime usedOn, string ipAddress, CancellationToken cancellationToken = default);
	}
}
