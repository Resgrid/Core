using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Protocol text as dispatched, once per protocol and content hash (M0337).</summary>
	public interface IDispatchProtocolSnapshotsRepository : IRepository<DispatchProtocolSnapshot>
	{
		/// <summary>
		/// Stores the snapshot unless the protocol already has one with the same hash. Either way nothing is duplicated, and two
		/// calls attaching the same text at the same moment do not fail.
		/// </summary>
		Task InsertIfMissingAsync(DispatchProtocolSnapshot snapshot, CancellationToken cancellationToken = default);

		/// <summary>The department's snapshots with any of the given hashes.</summary>
		Task<IEnumerable<DispatchProtocolSnapshot>> GetByHashesAsync(int departmentId, IEnumerable<string> contentHashes);
	}
}
