using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IModerationRequestRepository : IRepository<ModerationRequest>
	{
		Task<ModerationRequest> GetByItemAsync(int departmentId, int itemType, string itemId);

		Task<IEnumerable<ModerationRequest>> SearchAsync(int departmentId, ModerationSearchCriteria criteria,
			IEnumerable<int> visibleGroupIds, string reporterUserId);
	}

	public interface IModerationReportRepository : IRepository<ModerationReport>
	{
		Task<ModerationReport> GetByRequestAndReporterAsync(string moderationRequestId, string reportedByUserId);
		Task<IEnumerable<ModerationReport>> GetByRequestAsync(string moderationRequestId);
	}

	public interface IModerationActionRepository : IRepository<ModerationAction>
	{
		Task<IEnumerable<ModerationAction>> GetByRequestAsync(string moderationRequestId);
	}
}
