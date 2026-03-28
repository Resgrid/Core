using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface ICheckInTimerConfigRepository : IRepository<CheckInTimerConfig>
	{
		Task<IEnumerable<CheckInTimerConfig>> GetByDepartmentIdAsync(int departmentId);
		Task<CheckInTimerConfig> GetByDepartmentAndTargetAsync(int departmentId, int timerTargetType, int? unitTypeId);
	}
}
