using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Personnel;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Personnel
{
	/// <summary>
	/// personnel.staffingSummary (plan §7.3): the status table's people (DashboardPersonnelReads) counted by their latest
	/// staffing level (IUserStateService.GetLatestStatesForDepartmentAsync; no row reads as Available, as on the table), per
	/// group, and rostered on a running shift (IShiftsService.GetOnDutyUserIdsForGroupsAsync, one schedule load).
	/// </summary>
	[DashboardWidget("personnel.staffingSummary")]
	public class PersonnelStaffingSummaryWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IUserStateService _userStateService;
		private readonly IUsersService _usersService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly ICustomStateService _customStateService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IShiftsService _shiftsService;

		public PersonnelStaffingSummaryWidgetProvider(IUserStateService userStateService, IUsersService usersService, IDepartmentsService departmentsService,
			IAuthorizationService authorizationService, ICustomStateService customStateService, IDepartmentGroupsService departmentGroupsService,
			IShiftsService shiftsService)
		{
			_userStateService = userStateService;
			_usersService = usersService;
			_departmentsService = departmentsService;
			_authorizationService = authorizationService;
			_customStateService = customStateService;
			_departmentGroupsService = departmentGroupsService;
			_shiftsService = shiftsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var members = await DashboardPersonnelReads.GetVisibleMembersAsync(_usersService, _departmentsService, _authorizationService, departmentId, context.UserId);
			var data = new PersonnelStaffingSummaryWidgetData { Total = members.Count };
			if (members.Count == 0)
				return data;

			var states = (await _userStateService.GetLatestStatesForDepartmentAsync(departmentId) ?? new List<UserState>())
				.Where(s => s != null && !string.IsNullOrWhiteSpace(s.UserId))
				.GroupBy(s => s.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Timestamp).First(), StringComparer.OrdinalIgnoreCase);

			var palette = await DashboardStatusPalette.LoadAsync(_customStateService, departmentId);
			var people = members.Select(m => (Member: m, Chip: palette.Staffing(states.TryGetValue(m.UserId, out var state) ? state.State : (int)UserStateTypes.Available))).ToList();

			data.AvailableCount = people.Count(p => p.Chip.Availability == (int)AvailabilityClass.Available);
			data.ByStaffing = people
				.GroupBy(p => p.Chip.Id)
				.Select(g => new PersonnelSummaryItem { Status = g.First().Chip, Count = g.Count() })
				.OrderByDescending(i => i.Count).ThenBy(i => i.Status.Text)
				.ToList();

			var groups = (await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>())
				.Where(g => g != null && g.DepartmentId == departmentId)
				.ToList();
			if (groups.Count == 0)
				return data;

			var visibleIds = new HashSet<string>(members.Select(m => m.UserId), StringComparer.OrdinalIgnoreCase);
			var onDuty = await _shiftsService.GetOnDutyUserIdsForGroupsAsync(departmentId, groups.Select(g => g.DepartmentGroupId), context.NowUtc)
				?? new Dictionary<int, List<string>>();

			var onDutyAnywhere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var group in groups.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
			{
				var inGroup = people.Where(p => p.Member.DepartmentGroupId == group.DepartmentGroupId).ToList();
				int? groupOnDuty = null;

				// An empty list means no running shift covers the group, so there is no on-duty figure to show.
				if (onDuty.TryGetValue(group.DepartmentGroupId, out var rostered) && rostered != null && rostered.Count > 0)
				{
					var visibleRostered = rostered.Where(visibleIds.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
					groupOnDuty = visibleRostered.Count;
					onDutyAnywhere.UnionWith(visibleRostered);
				}

				data.ByGroup.Add(new PersonnelStaffingGroup
				{
					GroupId = group.DepartmentGroupId,
					Name = group.Name,
					Total = inGroup.Count,
					AvailableCount = inGroup.Count(p => p.Chip.Availability == (int)AvailabilityClass.Available),
					OnDutyCount = groupOnDuty
				});
			}

			if (data.ByGroup.Any(g => g.OnDutyCount.HasValue))
				data.OnDutyCount = onDutyAnywhere.Count;

			return data;
		}
	}
}
