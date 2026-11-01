using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Helpers;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Operations
{
	/// <summary>
	/// units.status (plan §7.2): the Units module's status board for the viewer (DashboardUnitReads), filtered by station and
	/// unit type, grouped by station or type. Destinations resolve like the unit timeline (DestinationResolutionHelper) against
	/// the viewer's dispatch-scoped active calls, so an out-of-scope call is never named. The status note goes through
	/// IProtectedReadService.ResolveUnitStatesForReadAsync with the caller's grant (ADP unitstates.note).
	/// </summary>
	[DashboardWidget("units.status")]
	public class UnitsStatusWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly ICallsService _callsService;
		private readonly IDispatchScopeService _dispatchScopeService;
		private readonly IProtectedReadService _protectedReadService;

		public UnitsStatusWidgetProvider(IUnitsService unitsService, ICustomStateService customStateService, IAuthorizationService authorizationService,
			IDepartmentGroupsService departmentGroupsService, ICallsService callsService, IDispatchScopeService dispatchScopeService,
			IProtectedReadService protectedReadService)
		{
			_unitsService = unitsService;
			_customStateService = customStateService;
			_authorizationService = authorizationService;
			_departmentGroupsService = departmentGroupsService;
			_callsService = callsService;
			_dispatchScopeService = dispatchScopeService;
			_protectedReadService = protectedReadService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var groupBy = context.GetString("groupBy", "station");
			var data = new UnitsStatusWidgetData { GroupBy = groupBy };

			var states = await DashboardUnitReads.GetVisibleLatestStatesAsync(_unitsService, _authorizationService, departmentId, context.UserId);
			var stations = (await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>())
				.Where(g => g != null && g.DepartmentId == departmentId).ToList();

			// Settings name department ids: only this department's stations and unit types count.
			var stationId = DashboardCallReads.ParseId(context.GetString("station"));
			if (stationId.HasValue)
			{
				if (stations.Any(s => s.DepartmentGroupId == stationId.Value))
				{
					var stationIds = DepartmentGroupHierarchy.GetSelfAndDescendantIds(stations, stationId.Value);
					states = states.Where(s => s.Unit.StationGroupId.HasValue && stationIds.Contains(s.Unit.StationGroupId.Value)).ToList();
				}
				else
				{
					data.StationFilterIgnored = true;
				}
			}

			var typeIds = DashboardCallReads.ParseIds(context.GetStrings("unitTypes"));
			if (typeIds.Count > 0)
			{
				var typeNames = new HashSet<string>((await _unitsService.GetUnitTypesForDepartmentAsync(departmentId) ?? new List<UnitType>())
					.Where(t => t != null && t.DepartmentId == departmentId && typeIds.Contains(t.UnitTypeId) && !string.IsNullOrWhiteSpace(t.Type))
					.Select(t => t.Type.Trim()), StringComparer.OrdinalIgnoreCase);

				if (typeNames.Count > 0)
					states = states.Where(s => !string.IsNullOrWhiteSpace(s.Unit.Type) && typeNames.Contains(s.Unit.Type.Trim())).ToList();
			}

			data.TotalCount = states.Count;

			var palette = await DashboardStatusPalette.LoadAsync(_customStateService, departmentId);
			var rows = states.Select(s => (State: s, Chip: palette.Unit(s.State))).ToList();

			if (context.GetBool("hideAvailable", false))
				rows = rows.Where(r => r.Chip.Availability != (int)AvailabilityClass.Available).ToList();

			data.ShownCount = rows.Count;
			if (rows.Count == 0)
				return data;

			var crew = (await _unitsService.GetAllActiveRolesForUnitsByDepartmentIdAsync(departmentId) ?? new List<UnitActiveRole>())
				.Where(r => r != null && r.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(r.UserId))
				.GroupBy(r => r.UnitId)
				.ToDictionary(g => g.Key, g => g.Select(r => r.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count());

			List<Call> calls = null;
			if (rows.Any(r => r.State.UnitStateId > 0 && r.State.DestinationId.HasValue && CallStatusLinkage.LinkedCallId(r.State.DestinationId, r.State.DestinationType).HasValue))
			{
				calls = await DashboardCallReads.GetScopedActiveCallsAsync(_callsService, _dispatchScopeService, departmentId, context.UserId, context.NowUtc);
				foreach (var call in calls)
					call.Name = DashboardCallReads.Safe(call.Name);
			}

			// One protected-read batch for the notes being shown (passes through untouched when the department isn't enforced).
			var withNotes = rows.Select(r => r.State).Where(s => s.UnitStateId > 0 && !string.IsNullOrEmpty(s.Note)).ToList();
			if (withNotes.Count > 0)
				await _protectedReadService.ResolveUnitStatesForReadAsync(departmentId, withNotes, context.ProtectedGrantToken, context.UserId, cancellationToken);

			var stationNames = stations.ToDictionary(s => s.DepartmentGroupId, s => s.Name);

			var unitRows = rows.Select(r =>
			{
				var state = r.State;
				var unit = state.Unit;
				var hasStatus = state.UnitStateId > 0;
				var destination = hasStatus
					? DestinationResolutionHelper.Resolve(state.DestinationId, state.DestinationType, palette.UnitOption(state.State)?.DetailType, calls, stations, null)
					: null;

				return new UnitsStatusRow
				{
					UnitId = unit.UnitId,
					Name = unit.Name,
					Type = string.IsNullOrWhiteSpace(unit.Type) ? null : unit.Type,
					StationGroupId = unit.StationGroupId,
					StationName = unit.StationGroupId.HasValue && stationNames.TryGetValue(unit.StationGroupId.Value, out var stationName) ? stationName : null,
					Status = r.Chip,
					HasStatus = hasStatus,
					StatusOnUtc = hasStatus ? state.Timestamp : (DateTime?)null,
					UnitStateId = state.UnitStateId,
					DestinationName = string.IsNullOrWhiteSpace(destination?.Name) ? null : ProtectedDataEnvelope.SafeDisplay(destination.Name),
					DestinationType = string.IsNullOrWhiteSpace(destination?.Name) ? 0 : destination.DestinationType ?? 0,
					DestinationId = string.IsNullOrWhiteSpace(destination?.Name) ? null : destination.DestinationId,
					CrewCount = crew.TryGetValue(unit.UnitId, out var seated) ? seated : 0,
					Note = hasStatus ? DashboardUnitReads.ProtectedNote(state.Note, context.Viewer) : null,
					Url = DashboardUnitReads.UnitUrl(unit.UnitId)
				};
			}).ToList();

			switch (groupBy)
			{
				case "type":
					data.Groups = unitRows
						.GroupBy(u => u.Type ?? string.Empty, StringComparer.OrdinalIgnoreCase)
						.OrderBy(g => g.Key.Length == 0 ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
						.Select(g => new UnitsStatusGroup
						{
							Key = g.Key.Length == 0 ? "type:none" : "type:" + g.Key,
							Name = g.Key.Length == 0 ? null : g.First().Type,
							Units = g.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList()
						}).ToList();
					break;
				case "none":
					data.Groups.Add(new UnitsStatusGroup { Key = "all", Units = unitRows.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList() });
					break;
				default:
					data.Groups = unitRows
						.GroupBy(u => u.StationName == null ? (int?)null : u.StationGroupId)
						.OrderBy(g => g.Key.HasValue ? 0 : 1).ThenBy(g => g.First().StationName, StringComparer.OrdinalIgnoreCase)
						.Select(g => new UnitsStatusGroup
						{
							Key = g.Key.HasValue ? "station:" + g.Key.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "station:none",
							Name = g.Key.HasValue ? g.First().StationName : null,
							Units = g.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList()
						}).ToList();
					break;
			}

			return data;
		}
	}
}
