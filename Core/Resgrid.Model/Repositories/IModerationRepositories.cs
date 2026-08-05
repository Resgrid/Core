using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IModerationRequestRepository : IRepository<ModerationRequest>
	{
		Task<ModerationRequest> GetByItemAsync(int departmentId, int itemType, string itemId);
		Task<IEnumerable<ModerationRequest>> GetByItemsAndReporterAsync(int departmentId, int itemType,
			IEnumerable<string> itemIds, string reporterUserId);

		Task<IEnumerable<ModerationRequest>> SearchAsync(int departmentId, ModerationSearchCriteria criteria,
			IEnumerable<int> visibleGroupIds, string reporterUserId);
	}

	public interface IModerationReportRepository : IRepository<ModerationReport>
	{
		Task<ModerationReport> GetByRequestAndReporterAsync(string moderationRequestId, string reportedByUserId);
		Task<ModerationReport> GetByRequestAndReporterAsync(string moderationRequestId, string reportedByUserId,
			bool useUnitOfWork);
		Task<IEnumerable<ModerationReport>> GetByRequestAsync(string moderationRequestId);
		Task<IEnumerable<ModerationReport>> GetByRequestIdsAsync(IEnumerable<string> moderationRequestIds);
	}

	public interface IModerationActionRepository : IRepository<ModerationAction>
	{
		Task<IEnumerable<ModerationAction>> GetByRequestAsync(string moderationRequestId);
		Task<IEnumerable<ModerationAction>> GetByRequestIdsAsync(IEnumerable<string> moderationRequestIds);
	}
}
