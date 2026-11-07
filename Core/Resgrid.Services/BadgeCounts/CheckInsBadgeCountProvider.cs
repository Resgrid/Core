using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.BadgeCounts
{
	/// <summary>
	/// Check-in timers in warning or overdue across the active calls the member can see (the Responder home's "pending /
	/// missed check-ins" card). Only calls with check-in timers on are read, once per department for department-wide viewers
	/// (per scoped member otherwise), and kept briefly: a timer's state moves with the clock, so it is never cached long.
	/// </summary>
	public class CheckInsBadgeCountProvider : IBadgeCountProvider
	{
		/// <summary>A timer can turn from warning to overdue at any minute.</summary>
		private static readonly TimeSpan CheckInsCacheLength = TimeSpan.FromSeconds(30);

		private readonly ICallsService _callsService;
		private readonly IDispatchScopeService _dispatchScopeService;
		private readonly ICheckInTimerService _checkInTimerService;
		private readonly ICacheProvider _cacheProvider;

		public CheckInsBadgeCountProvider(ICallsService callsService, IDispatchScopeService dispatchScopeService, ICheckInTimerService checkInTimerService, ICacheProvider cacheProvider)
		{
			_callsService = callsService;
			_dispatchScopeService = dispatchScopeService;
			_checkInTimerService = checkInTimerService;
			_cacheProvider = cacheProvider;
		}

		public string Key => BadgeCountKeys.CheckIns;

		public string RequiredPolicy => "Call_View";

		public async Task<BadgeCount> GetCountAsync(BadgeCountContext context)
		{
			var departmentId = context.DepartmentId;
			var userId = context.UserId;
			var scope = await _dispatchScopeService.GetScopeForUserAsync(departmentId, userId);
			var isDepartmentWide = scope == null || scope.IsDepartmentWide;

			async Task<int> countTimers()
			{
				var calls = (await _callsService.GetActiveCallsByDepartmentAsync(departmentId)).Where(call => call != null && call.CheckInTimersEnabled).ToList();

				if (calls.Count == 0)
					return 0;

				if (!isDepartmentWide)
					calls = await _dispatchScopeService.FilterCallsForUserAsync(departmentId, userId, calls) ?? new System.Collections.Generic.List<Call>();

				if (calls.Count == 0)
					return 0;

				// One batch: the department's timer settings and member and unit states are read once, not once per call.
				var statusesByCall = await _checkInTimerService.GetActiveTimerStatusesForCallsAsync(calls);

				return statusesByCall?.Values.Sum(statuses => statuses?.Count(IsPendingOrMissed) ?? 0) ?? 0;
			}

			if (!BadgeCountCache.IsEnabled(_cacheProvider))
				return new BadgeCount(Key, await countTimers());

			var version = await BadgeCountCache.GetCallsVersionAsync(_cacheProvider, departmentId);
			var key = BadgeCountCacheKeys.CheckIns(departmentId, isDepartmentWide ? "all" : userId.ToLowerInvariant(), version);

			return new BadgeCount(Key, await BadgeCountCache.RetrieveAsync(_cacheProvider, key, countTimers, CheckInsCacheLength));
		}

		/// <summary>
		/// A timer due within its warning threshold ("Warning") or past due ("Critical", the timer service's overdue state);
		/// "Green" timers need nothing yet.
		/// </summary>
		private static bool IsPendingOrMissed(CheckInTimerStatus status) => status != null && (status.Status == "Warning" || status.Status == "Critical");
	}
}
