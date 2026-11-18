using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Members released from open calls (CallPersonnelReleases, M0316).</summary>
	public interface ICallPersonnelReleasesRepository : IRepository<CallPersonnelRelease>
	{
		Task<IEnumerable<CallPersonnelRelease>> GetByCallIdAsync(int callId);

		/// <summary>The department's releases on the given calls (reports read many calls at once).</summary>
		Task<IEnumerable<CallPersonnelRelease>> GetForCallsAsync(int departmentId, IEnumerable<int> callIds);

		/// <summary>Removes the members' releases from the call (they were dispatched to it again). Returns the rows removed.</summary>
		Task<int> DeleteForUsersAsync(int callId, IEnumerable<string> userIds, CancellationToken cancellationToken = default);
	}
}
