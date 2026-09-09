using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;

namespace Resgrid.Model.Repositories
{
	public interface IChecklistRepository
	{
		Task LockDepartmentAsync(int departmentId, CancellationToken ct = default);
		Task<T> GetAsync<T>(int departmentId, string id, CancellationToken ct = default) where T : ChecklistRow;
		Task<List<T>> ListAsync<T>(int departmentId, string parentId = null, int skip = 0, int take = 100, CancellationToken ct = default) where T : ChecklistRow;
		Task WriteAsync<T>(T row, bool insert, CancellationToken ct = default) where T : ChecklistRow;
		Task ReplaceAnswersAsync(int departmentId, string completionId, IEnumerable<ChecklistCompletionItem> items, CancellationToken ct = default);
		Task DeleteFileAsync(int departmentId, string id, CancellationToken ct = default);
		Task<ChecklistCompletionFile> GetFileMetadataAsync(int departmentId, string id);
	}
}
