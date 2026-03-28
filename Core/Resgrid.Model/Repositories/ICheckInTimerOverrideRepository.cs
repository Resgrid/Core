using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface ICheckInTimerOverrideRepository : IRepository<CheckInTimerOverride>
	{
		Task<IEnumerable<CheckInTimerOverride>> GetByDepartmentIdAsync(int departmentId);
		Task<IEnumerable<CheckInTimerOverride>> GetMatchingOverridesAsync(int departmentId, int? callTypeId, int? callPriority);
	}
}
