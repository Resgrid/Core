using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ICheckInTimerService
	{
		// Configuration CRUD
		Task<List<CheckInTimerConfig>> GetTimerConfigsForDepartmentAsync(int departmentId);
		Task<CheckInTimerConfig> SaveTimerConfigAsync(CheckInTimerConfig config, CancellationToken cancellationToken = default);
		Task<bool> DeleteTimerConfigAsync(string configId, int departmentId, CancellationToken cancellationToken = default);

		// Override CRUD
		Task<List<CheckInTimerOverride>> GetTimerOverridesForDepartmentAsync(int departmentId);
		Task<CheckInTimerOverride> SaveTimerOverrideAsync(CheckInTimerOverride ovr, CancellationToken cancellationToken = default);
		Task<bool> DeleteTimerOverrideAsync(string overrideId, int departmentId, CancellationToken cancellationToken = default);

		// Timer Resolution
		Task<List<ResolvedCheckInTimer>> ResolveAllTimersForCallAsync(Call call);

		// Check-in Operations
		Task<CheckInRecord> PerformCheckInAsync(CheckInRecord record, CancellationToken cancellationToken = default);
		Task<List<CheckInRecord>> GetCheckInsForCallAsync(int callId);
		Task<CheckInRecord> GetLastCheckInAsync(int callId, string userId, int? unitId);

		// Timer Status Computation
		Task<List<CheckInTimerStatus>> GetActiveTimerStatusesForCallAsync(Call call);
	}
}
