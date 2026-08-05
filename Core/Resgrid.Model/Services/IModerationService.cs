using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IModerationService
	{
		Task<ModerationReport> FlagAsync(int departmentId, string reportedByUserId, ModerationItemType itemType,
			string itemId, ModerationReason reason, string note, ChatModerationContext context = null,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> CanModerateAsync(int departmentId, string userId);

		Task<List<ModerationRequest>> SearchRequestsAsync(int departmentId, string viewerUserId,
			ModerationSearchCriteria criteria);

		Task<ModerationRequest> GetRequestAsync(string moderationRequestId, int departmentId, string viewerUserId);

		Task<ModerationRequest> GetReporterRequestAsync(int departmentId, string reporterUserId,
			ModerationItemType itemType, string itemId);
		Task<List<ModerationRequest>> GetReporterRequestsAsync(int departmentId, string reporterUserId,
			ModerationItemType itemType, IEnumerable<string> itemIds);

		Task<ModerationRequest> CompleteRequestAsync(string moderationRequestId, int departmentId,
			string completedByUserId, ModerationDisposition disposition, string adminNote,
			ChatModerationContext context = null, CancellationToken cancellationToken = default(CancellationToken));

		Task NotifyReportersAsync(string moderationRequestId,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> RecordEvidenceAccessAsync(string moderationRequestId, int departmentId, string viewedByUserId,
			ChatModerationContext context = null, CancellationToken cancellationToken = default(CancellationToken));
	}
}
