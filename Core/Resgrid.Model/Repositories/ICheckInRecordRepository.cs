using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface ICheckInRecordRepository : IRepository<CheckInRecord>
	{
		Task<IEnumerable<CheckInRecord>> GetByCallIdAsync(int callId);
		Task<CheckInRecord> GetLastCheckInForUserOnCallAsync(int callId, string userId);
		Task<CheckInRecord> GetLastCheckInForUnitOnCallAsync(int callId, int unitId);
		Task<IEnumerable<CheckInRecord>> GetByDepartmentIdAndDateRangeAsync(int departmentId, DateTime start, DateTime end);
	}
}
