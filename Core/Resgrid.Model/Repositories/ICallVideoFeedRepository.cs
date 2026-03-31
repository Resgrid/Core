using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface ICallVideoFeedRepository : IRepository<CallVideoFeed>
	{
		Task<IEnumerable<CallVideoFeed>> GetByCallIdAsync(int callId);
		Task<IEnumerable<CallVideoFeed>> GetByDepartmentIdAsync(int departmentId);
	}
}
