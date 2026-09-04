using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Read side of the RMS-owned Lucene index (RMS plan section 5.10). The department filter and, under
	/// group-scoped visibility, the group-scope clause are injected from authenticated state by the caller,
	/// never from request input. Every hit must still be re-checked by the caller before it is shown.
	/// </summary>
	public interface IRecordsSearchService
	{
		/// <summary>True when the host is enabled and an index exists to read.</summary>
		bool IsAvailable { get; }

		Task<RecordsSearchResult> SearchAsync(int departmentId, RecordsSearchRequest request, CancellationToken cancellationToken = default);

		Task<RecordsSearchHealth> GetHealthAsync();
	}
}
