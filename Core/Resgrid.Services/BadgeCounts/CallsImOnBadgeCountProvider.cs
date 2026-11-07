using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.BadgeCounts
{
	/// <summary>
	/// Active calls dispatched to the member — to them, their group, a role they hold or a unit they have a seat on (the
	/// Responder home's "calls I'm on" card). One COUNT; cached per member on the department's calls version, so a new or
	/// changed call counts at once. A member always keeps the calls they are dispatched on, whatever the dispatch scope.
	/// </summary>
	public class CallsImOnBadgeCountProvider : IBadgeCountProvider
	{
		private readonly ICallsService _callsService;
		private readonly ICacheProvider _cacheProvider;

		public CallsImOnBadgeCountProvider(ICallsService callsService, ICacheProvider cacheProvider)
		{
			_callsService = callsService;
			_cacheProvider = cacheProvider;
		}

		public string Key => BadgeCountKeys.CallsImOn;

		public string RequiredPolicy => "Call_View";

		public async Task<BadgeCount> GetCountAsync(BadgeCountContext context)
		{
			Task<int> count() => _callsService.GetActiveCallsCountDispatchedToUserAsync(context.DepartmentId, context.UserId);

			if (!BadgeCountCache.IsEnabled(_cacheProvider))
				return new BadgeCount(Key, await count());

			var version = await BadgeCountCache.GetCallsVersionAsync(_cacheProvider, context.DepartmentId);

			return new BadgeCount(Key, await BadgeCountCache.RetrieveAsync(_cacheProvider, BadgeCountCacheKeys.CallsImOn(context.DepartmentId, context.UserId.ToLowerInvariant(), version), count, BadgeCountCacheKeys.CountCacheLength));
		}
	}
}
