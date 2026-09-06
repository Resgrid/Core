using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Write side of the RMS-owned Lucene index. Only the worker process (command 44, in the process that also
	/// runs command 40) holds the single IndexWriter; web processes never resolve this in anger.
	/// </summary>
	public interface IRecordsSearchIndexer
	{
		Task<int> IndexAsync(IEnumerable<RecordsSearchDocumentSource> documents, CancellationToken cancellationToken = default);

		Task DeleteAsync(int departmentId, int sourceType, string sourceId, CancellationToken cancellationToken = default);

		Task DeleteDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);

		Task CommitAsync(CancellationToken cancellationToken = default);

		/// <summary>Expunges deleted documents from the configured index, commits replacement segments and refreshes this process's reader.</summary>
		Task ExpungeDeletesAsync(CancellationToken cancellationToken = default);

		Task<int> CountDocumentsAsync(int departmentId);
	}
}
