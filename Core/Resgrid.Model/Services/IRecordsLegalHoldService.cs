using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IRecordsLegalHoldService
	{
		Task<List<RmsRecordLegalHold>> GetAsync(int departmentId, string userId);
		Task<RmsRecordLegalHold> PlaceAsync(int departmentId, string userId, RmsRecordLegalHold input, CancellationToken cancellationToken = default);
		Task ReleaseAsync(int departmentId, string userId, string holdId, long expectedVersion, string reason, CancellationToken cancellationToken = default);
	}
}
