using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.BadgeCounts
{
	/// <summary>
	/// Shift days today (the department's date) the member is on the roster of: their own signup, a standing assignment
	/// they are not off from, or a day they took over through a trade.
	/// </summary>
	public class ShiftsBadgeCountProvider : ModuleBadgeCountProvider
	{
		private readonly IBadgeCountsRepository _badgeCountsRepository;

		public ShiftsBadgeCountProvider(IBadgeCountsRepository badgeCountsRepository, IFeatureToggleService featureToggleService)
			: base(featureToggleService)
		{
			_badgeCountsRepository = badgeCountsRepository;
		}

		public override string Key => BadgeCountKeys.Shifts;

		public override string RequiredPolicy => "Shift_View";

		protected override bool IsDisabled(DepartmentModuleSettings settings) => settings.ShiftsDisabled;

		protected override async Task<BadgeCount> CountAsync(BadgeCountContext context)
		{
			var today = await context.GetLocalTodayAsync();

			return new BadgeCount(Key, await _badgeCountsRepository.CountShiftDaysForUserAsync(context.DepartmentId, context.UserId, today, today.AddDays(1)));
		}
	}

	/// <summary>
	/// Shift trade requests still in play for the member, for days from today on: those sent to them that they have not
	/// answered, and their own that nobody has taken yet or that wait for a supervisor. The badge is the sum.
	/// </summary>
	public class ShiftTradesBadgeCountProvider : ModuleBadgeCountProvider
	{
		private readonly IBadgeCountsRepository _badgeCountsRepository;

		public ShiftTradesBadgeCountProvider(IBadgeCountsRepository badgeCountsRepository, IFeatureToggleService featureToggleService)
			: base(featureToggleService)
		{
			_badgeCountsRepository = badgeCountsRepository;
		}

		public override string Key => BadgeCountKeys.ShiftTrades;

		public override string RequiredPolicy => "Shift_View";

		protected override bool IsDisabled(DepartmentModuleSettings settings) => settings.ShiftsDisabled;

		protected override async Task<BadgeCount> CountAsync(BadgeCountContext context)
		{
			var today = await context.GetLocalTodayAsync();
			var counts = await _badgeCountsRepository.GetShiftTradeCountsAsync(context.DepartmentId, context.UserId, today) ?? new ShiftTradeCounts();

			return new BadgeCount(Key, counts.AwaitingMe + counts.Mine, new Dictionary<string, int> { ["awaitingMe"] = counts.AwaitingMe, ["mine"] = counts.Mine });
		}
	}
}
