using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentProfileRepository : IRepository<DepartmentProfile>
	{
		Task<DepartmentProfile> GetByDepartmentIdAsync(int departmentId);
	}

	public interface IDepartmentProfileMediaRepository : IRepository<DepartmentProfileMedia>
	{
		/// <summary>Every rendition row for the department without its bytes.</summary>
		Task<IEnumerable<DepartmentProfileMedia>> GetMetadataForDepartmentAsync(int departmentId);

		/// <summary>One rendition with its bytes.</summary>
		Task<DepartmentProfileMedia> GetAsync(int departmentId, int kind);

		/// <summary>Anonymous serving path: the rendition matching an opaque per-department key.</summary>
		Task<DepartmentProfileMedia> GetByMediaKeyAsync(string mediaKey, int kind);

		Task<int> DeleteForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);

		Task<int> UpdateMediaKeyAsync(int departmentId, string mediaKey, CancellationToken cancellationToken = default);
	}

	public interface IRmsRecordPrintLayoutsRepository : IRepository<RmsRecordPrintLayout>
	{
		Task<RmsRecordPrintLayout> GetAsync(int departmentId, int scope, string definitionKey);
	}
}
