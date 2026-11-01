using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Dashboard.Widgets.Personnel;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Personnel
{
	/// <summary>
	/// personnel.statusSummary (plan §7.3): the status table's people (DashboardPersonnelReads) counted by their latest status
	/// (IActionLogsService.GetLastActionLogsForDepartmentAsync); no status inside the auto-available window counts as the
	/// department's Available status, exactly as the table shows it.
	/// </summary>
	[DashboardWidget("personnel.statusSummary")]
	public class PersonnelStatusSummaryWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IActionLogsService _actionLogsService;
		private readonly IUsersService _usersService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly ICustomStateService _customStateService;

		public PersonnelStatusSummaryWidgetProvider(IActionLogsService actionLogsService, IUsersService usersService, IDepartmentsService departmentsService,
			IAuthorizationService authorizationService, ICustomStateService customStateService)
		{
			_actionLogsService = actionLogsService;
			_usersService = usersService;
			_departmentsService = departmentsService;
			_authorizationService = authorizationService;
			_customStateService = customStateService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var members = await DashboardPersonnelReads.GetVisibleMembersAsync(_usersService, _departmentsService, _authorizationService, departmentId, context.UserId);
			var data = new PersonnelStatusSummaryWidgetData { Total = members.Count };
			if (members.Count == 0)
				return data;

			var logs = (await _actionLogsService.GetLastActionLogsForDepartmentAsync(departmentId) ?? new List<ActionLog>())
				.Where(l => l != null && l.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(l.UserId))
				.GroupBy(l => l.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.ActionLogId).First(), StringComparer.OrdinalIgnoreCase);

			var palette = await DashboardStatusPalette.LoadAsync(_customStateService, departmentId);
			var defaultDetail = await _customStateService.GetDefaultPersonnelStatusAsync(departmentId);
			var defaultChip = defaultDetail != null && defaultDetail.CustomStateDetailId > 0
				? DashboardStatusPalette.FromCustomStatus(defaultDetail)
				: palette.PersonnelStatus((int)ActionTypes.StandingBy);

			var chips = members.Select(m => logs.TryGetValue(m.UserId, out var log) ? palette.PersonnelStatus(log.ActionTypeId) : defaultChip).ToList();

			data.Available = chips.Count(c => c.Availability == (int)AvailabilityClass.Available);
			data.Committed = chips.Count(c => c.Availability == (int)AvailabilityClass.Committed);
			data.Delayed = chips.Count(c => c.Availability == (int)AvailabilityClass.Delayed);
			data.Unavailable = chips.Count(c => c.Availability == (int)AvailabilityClass.Unavailable);
			data.Unknown = chips.Count(c => c.Availability == (int)AvailabilityClass.Unknown);
			data.ByStatus = chips
				.GroupBy(c => c.Id)
				.Select(g => new PersonnelSummaryItem { Status = g.First(), Count = g.Count() })
				.OrderByDescending(i => i.Count).ThenBy(i => i.Status.Text)
				.ToList();

			return data;
		}
	}
}
