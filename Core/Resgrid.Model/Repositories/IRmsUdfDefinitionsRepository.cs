using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IRmsUdfDefinitionsRepository : IRepository<UdfDefinition>
	{
		Task<UdfDefinition> GetActiveAsync(int departmentId, string key, int version);
		Task<UdfDefinition> GetScopedAsync(int departmentId, string definitionId, string key, int version);
		Task DeactivateAsync(int departmentId, string key, int version, CancellationToken ct);
		Task LockDepartmentAsync(int departmentId, CancellationToken ct);
		Task GuardRecordAsync(int departmentId, string recordId, CancellationToken ct);
		Task DeleteRecordValuesAsync(int departmentId, string recordId, CancellationToken ct);
	}
}
