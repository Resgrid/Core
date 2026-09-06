using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Serializes each index mutation with retention and verifies the current SQL source before invoking the synchronous index writer.</summary>
	public interface IRmsSearchWriteFence
	{
		Task<int> WithLiveSourceAsync(RecordsSearchDocumentSource source, Func<RecordsSearchDocumentSource, int> write, CancellationToken cancellationToken = default);
	}
}
