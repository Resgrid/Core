using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IAdpAuditRepository
	{
		Task AppendAsync(AdpAuditEvent record, CancellationToken cancellationToken = default);
		/// <summary>One page of the department's chain, in sequence order, starting after <paramref name="afterSequence"/>.</summary>
		Task<IReadOnlyList<AdpAuditEvent>> ReadAsync(int departmentId, long afterSequence, int take, CancellationToken cancellationToken = default);
	}
}
