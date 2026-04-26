using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;

namespace Resgrid.Model.Services
{
	public interface ICallDispatchStatusService
	{
		Task ApplyDispatchStatusesAsync(Call call, IEnumerable<int> groupIds = null, IEnumerable<int> unitIds = null, CancellationToken cancellationToken = default(CancellationToken));

		Task ApplyReleaseStatusesAsync(Call call, IEnumerable<int> groupIds = null, IEnumerable<int> unitIds = null, CancellationToken cancellationToken = default(CancellationToken));
	}
}
