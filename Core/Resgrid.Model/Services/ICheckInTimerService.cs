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

		// ── New: per-user and per-call personnel check-in status ────────────────

		/// <summary>
		/// Returns a check-in summary for every active call (with check-in timers
		/// enabled) that <paramref name="userId"/> has been dispatched on.
		/// Used by API Endpoint 1.
		/// </summary>
		/// <param name="userId">The ASP.NET Identity user identifier to query for.</param>
		/// <param name="departmentId">Dept scope (from JWT claims) – prevents cross-dept data access.</param>
		Task<List<UserCallCheckInSummary>> GetUserActiveCallCheckInSummariesAsync(string userId, int departmentId);

		/// <summary>
		/// For a call that has a personnel check-in timer active, returns the current
		/// check-in status for every person dispatched on that call.
		/// Used by API Endpoint 2.
		/// </summary>
		/// <param name="call">The call to evaluate. Must have <see cref="Call.CheckInTimersEnabled"/> = true.</param>
		Task<List<PersonnelCallCheckInStatus>> GetCallPersonnelCheckInStatusesAsync(Call call);
	}
}
