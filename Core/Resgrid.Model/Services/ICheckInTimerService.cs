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

		/// <summary>
		/// <see cref="GetActiveTimerStatusesForCallAsync"/> for several calls, keyed by CallId (an empty list for a call that
		/// is not active or has timers off). The department-wide reads (timer settings, call and unit types, the members'
		/// and units' latest states) are made once per department for the whole batch rather than once per call; each
		/// call's own check-ins and dispatches are still read per call.
		/// </summary>
		Task<Dictionary<int, List<CheckInTimerStatus>>> GetActiveTimerStatusesForCallsAsync(IEnumerable<Call> calls);

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
