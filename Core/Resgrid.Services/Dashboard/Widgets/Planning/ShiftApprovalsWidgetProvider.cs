using System;
using System.Collections.Generic;
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
	/// shifts.approvals (plan §7.6): the Shifts page's approvals badge, item by item — pending signups in slots the viewer
	/// supervises (ShiftManagementScope.CanManageShiftGroup) and pending trades the viewer approves under setting 121
	/// (ShiftTradeAuthority.IsApprover). A viewer with no approvals gets HasApprovals = false (the Approvals page refuses them).
	/// </summary>
	[DashboardWidget("shifts.approvals")]
	public class ShiftApprovalsWidgetProvider : IDashboardWidgetDataProvider
	{
		public const string ApprovalsUrl = "/User/Shifts/Approvals";

		private readonly IShiftsService _shiftsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public ShiftApprovalsWidgetProvider(IShiftsService shiftsService, IAuthorizationService authorizationService,
			IDepartmentSettingsService departmentSettingsService, IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService)
		{
			_shiftsService = shiftsService;
			_authorizationService = authorizationService;
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var data = new ShiftApprovalsWidgetData { ApprovalsUrl = ApprovalsUrl };

			var authority = new ShiftTradeAuthority
			{
				UserId = context.UserId,
				Scope = await _authorizationService.GetShiftManagementScopeAsync(context.UserId, departmentId) ?? ShiftManagementScope.None(),
				Settings = await _departmentSettingsService.GetShiftTradeApproverSettingsAsync(departmentId) ?? ShiftTradeApproverSettings.Default(),
				// The viewer's personnel roles, as the web tier resolved them (the page reads the same GetRolesForUserAsync).
				RoleIds = (context.Viewer?.PersonnelRoleIds ?? Array.Empty<int>()).Distinct().ToList()
			};

			data.HasApprovals = authority.HasApprovals;
			if (!authority.HasApprovals)
				return data;

			var shifts = (await _shiftsService.GetAllShiftsByDepartmentAsync(departmentId) ?? new List<Shift>())
				.Where(s => s != null && s.DepartmentId == departmentId)
				.GroupBy(s => s.ShiftId)
				.ToDictionary(g => g.Key, g => g.First());

			var signups = new List<ShiftSignup>();
			foreach (var signup in await _shiftsService.GetPendingShiftSignupsAsync(departmentId) ?? new List<ShiftSignup>())
			{
				if (signup == null || !shifts.TryGetValue(signup.ShiftId, out var shift) || !authority.Scope.CanManageShiftGroup(shift, signup.DepartmentGroupId))
					continue;

				signup.Shift = shift;
				signups.Add(signup);
			}

			var trades = new List<ShiftSignupTrade>();
			foreach (var trade in await _shiftsService.GetPendingTradesAsync(departmentId) ?? new List<ShiftSignupTrade>())
			{
				var source = trade?.SourceShiftSignup;

				// Static shift day trades never wait for approval; they have no source signup.
				if (source == null || !shifts.TryGetValue(source.ShiftId, out var shift) || !authority.IsApprover(shift, source.DepartmentGroupId))
					continue;

				source.Shift = shift;
				trades.Add(trade);
			}

			data.PendingSignupCount = signups.Count;
			data.PendingTradeCount = trades.Count;
			if (signups.Count == 0 && trades.Count == 0)
				return data;

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			var names = ShiftWidgetHelpers.Names(await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(departmentId));
			var groupNames = ShiftWidgetHelpers.GroupNames(await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId), departmentId);

			var items = new List<(DateTime Start, ShiftApprovalItem Item)>();
			foreach (var signup in signups)
			{
				var window = ShiftWidgetHelpers.Window(signup.Shift, signup.ShiftDay);
				var day = ShiftWidgetHelpers.FindDay(signup.Shift, signup.ShiftDay);
				items.Add((window.Start, new ShiftApprovalItem
				{
					Kind = "signup",
					Id = signup.ShiftSignupId,
					ShiftName = signup.Shift.Name,
					Color = signup.Shift.Color,
					GroupName = ShiftWidgetHelpers.GroupNameFor(groupNames, signup.DepartmentGroupId),
					UserName = ShiftWidgetHelpers.NameFor(names, signup.UserId),
					StartUtc = ShiftWidgetHelpers.ToUtc(window.Start, department),
					EndUtc = ShiftWidgetHelpers.ToUtc(window.End, department),
					Url = ShiftWidgetHelpers.ViewShiftUrl(day?.ShiftDayId, ApprovalsUrl)
				}));
			}

			foreach (var trade in trades)
			{
				var source = trade.SourceShiftSignup;
				var window = ShiftWidgetHelpers.Window(source.Shift, source.ShiftDay);
				var takerId = !string.IsNullOrWhiteSpace(trade.UserId) ? trade.UserId : trade.TargetShiftSignup?.UserId;
				items.Add((window.Start, new ShiftApprovalItem
				{
					Kind = "trade",
					Id = trade.ShiftSignupTradeId,
					ShiftName = source.Shift.Name,
					Color = source.Shift.Color,
					GroupName = ShiftWidgetHelpers.GroupNameFor(groupNames, source.DepartmentGroupId),
					UserName = ShiftWidgetHelpers.NameFor(names, takerId),
					RequesterName = ShiftWidgetHelpers.NameFor(names, source.UserId),
					StartUtc = ShiftWidgetHelpers.ToUtc(window.Start, department),
					EndUtc = ShiftWidgetHelpers.ToUtc(window.End, department),
					Url = ApprovalsUrl
				}));
			}

			data.Items = items.OrderBy(x => x.Start).Take(context.GetInt("maxItems", 5)).Select(x => x.Item).ToList();
			return data;
		}
	}
}
