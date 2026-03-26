using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IGdprDataExportRequestRepository : IRepository<GdprDataExportRequest>
	{
		Task<IEnumerable<GdprDataExportRequest>> GetPendingRequestsAsync();
		Task<GdprDataExportRequest> GetByTokenAsync(string token);
		Task<GdprDataExportRequest> GetActiveRequestByUserIdAsync(string userId);
		Task<IEnumerable<GdprDataExportRequest>> GetExpiredRequestsAsync();
	}
}
