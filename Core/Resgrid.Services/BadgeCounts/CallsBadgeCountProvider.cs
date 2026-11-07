using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.BadgeCounts
{
	/// <summary>
	/// Active calls the member can see — the rows v4 Calls/GetActiveCalls returns. Group-scoped dispatch is off by default,
	/// and admins and department-wide roles see everything: those viewers share one COUNT per department. A scoped member
	/// gets the dispatch-scope filter, as the call list applies it. Cached per department (or per scoped member) on the
	/// department's calls version, which CallsService rolls on every save, delete and reopen.
	/// </summary>
	public class CallsBadgeCountProvider : IBadgeCountProvider
	{
		private readonly ICallsService _callsService;
		private readonly IDispatchScopeService _dispatchScopeService;
		private readonly ICacheProvider _cacheProvider;

		public CallsBadgeCountProvider(ICallsService callsService, IDispatchScopeService dispatchScopeService, ICacheProvider cacheProvider)
		{
			_callsService = callsService;
			_dispatchScopeService = dispatchScopeService;
			_cacheProvider = cacheProvider;
		}

		public string Key => BadgeCountKeys.Calls;

		public string RequiredPolicy => "Call_View";

		public async Task<BadgeCount> GetCountAsync(BadgeCountContext context)
		{
			return new BadgeCount(Key, await GetOpenCallsCountAsync(context.DepartmentId, context.UserId));
		}

		public async Task<int> GetOpenCallsCountAsync(int departmentId, string userId)
		{
			var scope = await _dispatchScopeService.GetScopeForUserAsync(departmentId, userId);
			var isDepartmentWide = scope == null || scope.IsDepartmentWide;

			async Task<int> countCalls()
			{
				if (isDepartmentWide)
					return await _callsService.GetActiveCallsForDepartmentAsync(departmentId);

				var visible = await _dispatchScopeService.FilterCallsForUserAsync(departmentId, userId, await _callsService.GetActiveCallsByDepartmentAsync(departmentId));
				return visible?.Count ?? 0;
			}

			if (_cacheProvider == null || !Config.SystemBehaviorConfig.CacheEnabled)
				return await countCalls();

			var version = await _cacheProvider.GetStringAsync(BadgeCountCacheKeys.OpenCallsVersion(departmentId));
			var key = BadgeCountCacheKeys.OpenCalls(departmentId, isDepartmentWide ? "all" : userId.ToLowerInvariant(), string.IsNullOrWhiteSpace(version) ? "0" : version);

			return await _cacheProvider.RetrieveAsync(key, countCalls, BadgeCountCacheKeys.CountCacheLength);
		}
	}
}
