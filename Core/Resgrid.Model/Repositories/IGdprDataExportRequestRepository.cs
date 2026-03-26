using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IGdprDataExportRequestRepository : IRepository<GdprDataExportRequest>
	{
		Task<IEnumerable<GdprDataExportRequest>> GetPendingRequestsAsync();
		Task<bool> TryClaimForProcessingAsync(string requestId, CancellationToken cancellationToken = default);
		Task<GdprDataExportRequest> GetByTokenAsync(string token);
		Task<GdprDataExportRequest> GetActiveRequestByUserIdAsync(string userId);
		Task<IEnumerable<GdprDataExportRequest>> GetExpiredRequestsAsync();
	}
}
