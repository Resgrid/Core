using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Operations;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Operations
{
	/// <summary>
	/// units.alerts (plan §7.2): the Big Board's Unit Alerts widget on the server. Each visible unit's current status is timed
	/// against the department's Unit Status Timers (setting 62) with UnitStatusAlertEvaluator, and matched to the
	/// acknowledgement of that status episode (v4 UnitStatusAlerts/GetActiveAcknowledgements, units filtered by the matrix
	/// the same way). Acknowledging is for Call_Create holders, as on the Big Board.
	/// </summary>
	[DashboardWidget("units.alerts")]
	public class UnitsAlertsWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IUnitStatusAlertsService _unitStatusAlertsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public UnitsAlertsWidgetProvider(IUnitsService unitsService, ICustomStateService customStateService, IAuthorizationService authorizationService,
			IDepartmentSettingsService departmentSettingsService, IUnitStatusAlertsService unitStatusAlertsService, IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService)
		{
			_unitsService = unitsService;
			_customStateService = customStateService;
			_authorizationService = authorizationService;
			_departmentSettingsService = departmentSettingsService;
			_unitStatusAlertsService = unitStatusAlertsService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var now = context.NowUtc;
			var thresholds = await _departmentSettingsService.GetUnitStatusThresholdsAsync(departmentId);

			var data = new UnitsAlertsWidgetData
			{
				TimersConfigured = thresholds != null && !thresholds.IsEmpty,
				NoteRequirement = (int)(thresholds?.AcknowledgementNoteRequirement ?? UnitStatusAlertNoteRequirements.Optional),
				CanAcknowledge = context.Viewer != null && context.Viewer.HasPolicy("Call_Create"),
				// v4 routes, reached through the web BFF (WebApiBffController.AllowedPrefixes lists api/v4/UnitStatusAlerts/).
				Acknowledge = new DashboardWidgetEndpoint
				{
					Url = "/api/web-bff/api/v4/UnitStatusAlerts/Acknowledge", Method = "POST", Body = "json",
					Parameters = new[] { "UnitId", "UnitStateId", "Level", "Mode", "MuteMinutes", "Note" }, Available = true
				},
				Clear = new DashboardWidgetEndpoint { Url = "/api/web-bff/api/v4/UnitStatusAlerts/Clear/{id}", Method = "DELETE", Body = "query", Available = true }
			};

			if (!data.TimersConfigured)
				return data;

			var states = await DashboardUnitReads.GetVisibleLatestStatesAsync(_unitsService, _authorizationService, departmentId, context.UserId);
			if (states.Count == 0)
				return data;

			var palette = await DashboardStatusPalette.LoadAsync(_customStateService, departmentId);
			var overdue = new List<(UnitState State, DashboardStatusChip Chip, UnitStatusAlertLevels Level, UnitStatusThreshold Threshold)>();

			foreach (var state in states.Where(s => s.UnitStateId > 0))
			{
				var chip = palette.Unit(state.State);
				var level = UnitStatusAlertEvaluator.Evaluate(thresholds, chip.BaseType, state.Timestamp, now);
				if (level != UnitStatusAlertLevels.None)
					overdue.Add((state, chip, level, thresholds.Find(chip.BaseType)));
			}

			if (overdue.Count == 0)
				return data;

			var acknowledgements = (await _unitStatusAlertsService.GetCurrentAcknowledgementsForDepartmentAsync(departmentId) ?? new List<UnitStatusAlertAcknowledgement>())
				.Where(a => a != null && a.DepartmentId == departmentId)
				.ToList();
			var names = acknowledgements.Count > 0
				? await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>()
				: new List<PersonName>();
			var stations = (await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>())
				.Where(g => g != null && g.DepartmentId == departmentId)
				.ToDictionary(g => g.DepartmentGroupId, g => g.Name);

			var rows = new List<(UnitsAlertRow Row, int Order)>();
			foreach (var (state, chip, level, threshold) in overdue)
			{
				var acknowledgement = acknowledgements
					.Where(a => a.UnitId == state.UnitId && a.UnitStateId == state.UnitStateId)
					.OrderByDescending(a => a.AcknowledgedOn)
					.FirstOrDefault();
				var mode = UnitStatusAlertEvaluator.Resolve(acknowledgement, state.UnitStateId, level, now);

				var row = new UnitsAlertRow
				{
					UnitId = state.UnitId,
					UnitName = state.Unit.Name,
					Type = string.IsNullOrWhiteSpace(state.Unit.Type) ? null : state.Unit.Type,
					StationName = state.Unit.StationGroupId.HasValue && stations.TryGetValue(state.Unit.StationGroupId.Value, out var station) ? station : null,
					UnitStateId = state.UnitStateId,
					Status = chip,
					StatusOnUtc = state.Timestamp,
					Level = (int)level,
					WarnSeconds = threshold?.WarnSeconds ?? 0,
					AlertSeconds = threshold?.AlertSeconds ?? 0,
					NoteRequired = thresholds.RequiresAcknowledgementNote(level),
					Url = DashboardUnitReads.UnitUrl(state.UnitId)
				};

				if (mode.HasValue)
				{
					var by = names.FirstOrDefault(n => string.Equals(n.UserId, acknowledgement.AcknowledgedByUserId, StringComparison.OrdinalIgnoreCase));
					row.Acknowledgement = new UnitsAlertAcknowledgement
					{
						AcknowledgementId = acknowledgement.UnitStatusAlertAcknowledgementId,
						Mode = (int)mode.Value,
						Level = acknowledgement.Level,
						Note = acknowledgement.Note,
						AcknowledgedByName = by == null ? null : $"{by.FirstName} {by.LastName}".Trim(),
						AcknowledgedOnUtc = acknowledgement.AcknowledgedOn,
						MutedUntilUtc = acknowledgement.MutedUntil
					};
				}

				// Unacknowledged first, then acknowledged, then muted (the Big Board's collapsed section).
				var order = !mode.HasValue ? 0 : mode.Value == UnitStatusAlertAcknowledgementModes.Muted ? 2 : 1;
				rows.Add((row, order));
			}

			data.AlertCount = rows.Count(r => r.Row.Level == (int)UnitStatusAlertLevels.Alert);
			data.WarningCount = rows.Count(r => r.Row.Level == (int)UnitStatusAlertLevels.Warn);
			data.MutedCount = rows.Count(r => r.Order == 2);

			var showMuted = context.GetBool("showMuted", true);
			data.Alerts = rows
				.Where(r => showMuted || r.Order != 2)
				.OrderBy(r => r.Order)
				.ThenByDescending(r => r.Row.Level)
				.ThenBy(r => r.Row.StatusOnUtc)
				.Select(r => r.Row)
				.ToList();

			return data;
		}
	}
}
