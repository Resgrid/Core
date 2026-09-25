using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IAdpAuditRepository
	{
		Task AppendAsync(AdpAuditEvent record, CancellationToken cancellationToken = default);
		Task<IReadOnlyList<AdpAuditEvent>> ReadAsync(int departmentId, CancellationToken cancellationToken = default);
	}
}
