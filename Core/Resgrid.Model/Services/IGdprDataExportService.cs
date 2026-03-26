using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IGdprDataExportService
	{
		Task<GdprDataExportRequest> CreateExportRequestAsync(string userId, int departmentId, CancellationToken cancellationToken = default);
		Task<GdprDataExportRequest> GetActiveRequestByUserIdAsync(string userId);
		Task<GdprDataExportRequest> GetRequestByTokenAsync(string token);
		Task ProcessPendingRequestsAsync(CancellationToken cancellationToken = default);
		Task ExpireOldRequestsAsync(CancellationToken cancellationToken = default);
		Task MarkDownloadedAsync(GdprDataExportRequest request, CancellationToken cancellationToken = default);
	}
}
