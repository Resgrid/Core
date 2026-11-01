using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Planning
{
	/// <summary>
	/// shifts.mine (plan §7.6): the Your Shifts page's two lists for the active department — the member's signups (denied
	/// ones left out, as there) that have not ended and fall within the window, and the open trade requests sent to them.
	/// </summary>
	[DashboardWidget("shifts.mine")]
	public class ShiftsMineWidgetProvider : IDashboardWidgetDataProvider
	{
		public const string YourShiftsUrl = "/User/Shifts/YourShifts";

		private readonly IShiftsService _shiftsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public ShiftsMineWidgetProvider(IShiftsService shiftsService, IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService)
		{
			_shiftsService = shiftsService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			var localNow = ShiftWidgetHelpers.LocalNow(context.NowUtc, department);
			var lastDay = localNow.Date.AddDays(context.GetInt("days", 30));
			var maxItems = context.GetInt("maxItems", 5);

			var data = new ShiftsMineWidgetData { YourShiftsUrl = YourShiftsUrl };

			// A member of several departments has signups in each; the widget, like the page, is for the active one.
			var signups = (await _shiftsService.GetShiftSignupsForUserAsync(context.UserId) ?? new List<ShiftSignup>())
				.Where(s => s?.Shift != null && s.Shift.DepartmentId == departmentId && !s.Denied && s.ShiftDay.Date <= lastDay)
				.GroupBy(s => s.ShiftSignupId)
				.Select(g => g.First())
				.Select(s => (Signup: s, Window: ShiftWidgetHelpers.Window(s.Shift, s.ShiftDay)))
				.Where(x => x.Window.End > localNow)
				.OrderBy(x => x.Window.Start)
				.Take(maxItems)
				.ToList();

			var trades = (await _shiftsService.GetOpenTradeRequestsForUserAsync(context.UserId) ?? new List<ShiftSignupTrade>())
				.Where(t => t?.SourceShiftSignup?.Shift != null && t.SourceShiftSignup.Shift.DepartmentId == departmentId)
				.GroupBy(t => t.ShiftSignupTradeId)
				.Select(g => g.First())
				.Select(t => (Trade: t, Window: ShiftWidgetHelpers.Window(t.SourceShiftSignup.Shift, t.SourceShiftSignup.ShiftDay)))
				.Where(x => x.Window.End > localNow)
				.OrderBy(x => x.Window.Start)
				.Take(maxItems)
				.ToList();

			if (signups.Count == 0 && trades.Count == 0)
				return data;

			var groupNames = signups.Any(x => x.Signup.DepartmentGroupId.HasValue)
				? ShiftWidgetHelpers.GroupNames(await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId), departmentId)
				: new Dictionary<int, string>();
			var names = trades.Count > 0
				? ShiftWidgetHelpers.Names(await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(departmentId))
				: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach (var (signup, window) in signups)
			{
				var day = ShiftWidgetHelpers.FindDay(signup.Shift, signup.ShiftDay);
				data.Signups.Add(new ShiftsMineSignup
				{
					ShiftSignupId = signup.ShiftSignupId,
					ShiftId = signup.ShiftId,
					ShiftDayId = day?.ShiftDayId,
					ShiftName = signup.Shift.Name,
					Color = signup.Shift.Color,
					GroupName = ShiftWidgetHelpers.GroupNameFor(groupNames, signup.DepartmentGroupId),
					StartUtc = ShiftWidgetHelpers.ToUtc(window.Start, department),
					EndUtc = ShiftWidgetHelpers.ToUtc(window.End, department),
					ApprovalPending = signup.ApprovalPending,
					HasTrade = signup.Trade != null && !signup.Trade.IsTradeComplete(),
					Url = ShiftWidgetHelpers.ViewShiftUrl(day?.ShiftDayId, YourShiftsUrl)
				});
			}

			foreach (var (trade, window) in trades)
			{
				data.TradeRequests.Add(new ShiftsMineTrade
				{
					ShiftSignupTradeId = trade.ShiftSignupTradeId,
					ShiftName = trade.SourceShiftSignup.Shift.Name,
					Color = trade.SourceShiftSignup.Shift.Color,
					StartUtc = ShiftWidgetHelpers.ToUtc(window.Start, department),
					EndUtc = ShiftWidgetHelpers.ToUtc(window.End, department),
					RequesterName = ShiftWidgetHelpers.NameFor(names, trade.GetSourceUserId()),
					Url = "/User/Shifts/ProcessTrade?shiftSignupTradeId=" + trade.ShiftSignupTradeId.ToString(CultureInfo.InvariantCulture)
				});
			}

			return data;
		}
	}
}
