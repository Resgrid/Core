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
	/// units.routes (plan §7.2): the Routes module's Active Routes page (in-progress and paused instances, through the
	/// geometry-free summary read IRouteService.GetActiveInstancesForDepartmentAsync) and its open deviations
	/// (GetUnacknowledgedDeviationsAsync). Deviation positions are left out: they need the unit-location matrix, and the
	/// widget has no map.
	/// </summary>
	[DashboardWidget("units.routes")]
	public class UnitsRoutesWidgetProvider : IDashboardWidgetDataProvider
	{
		private const int MaxRows = 10;

		private readonly IRouteService _routeService;
		private readonly IUnitsService _unitsService;

		public UnitsRoutesWidgetProvider(IRouteService routeService, IUnitsService unitsService)
		{
			_routeService = routeService;
			_unitsService = unitsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var active = (await _routeService.GetActiveInstancesForDepartmentAsync(departmentId) ?? new List<RouteInstance>())
				.Where(i => i != null && i.DepartmentId == departmentId &&
					(i.Status == (int)RouteInstanceStatus.InProgress || i.Status == (int)RouteInstanceStatus.Paused))
				.ToList();
			var deviations = (await _routeService.GetUnacknowledgedDeviationsAsync(departmentId) ?? new List<RouteDeviation>())
				.Where(d => d != null && !d.IsAcknowledged)
				.ToList();

			var data = new UnitsRoutesWidgetData
			{
				ActiveRouteCount = active.Count,
				UnacknowledgedDeviationCount = deviations.Count,
				AcknowledgeDeviation = new DashboardWidgetEndpoint { Url = "/api/web-bff/api/v4/Routes/AcknowledgeDeviation/{id}", Method = "POST", Body = "query", Available = true }
			};

			if (active.Count == 0 && deviations.Count == 0)
				return data;

			var plans = (await _routeService.GetRoutePlansForDepartmentAsync(departmentId) ?? new List<RoutePlan>())
				.Where(p => p != null && p.DepartmentId == departmentId)
				.GroupBy(p => p.RoutePlanId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			var units = active.Count == 0
				? new Dictionary<int, Unit>()
				: (await _unitsService.GetUnitsForDepartmentAsync(departmentId) ?? new List<Unit>())
					.Where(u => u != null && u.DepartmentId == departmentId && !u.IsDeleted)
					.GroupBy(u => u.UnitId)
					.ToDictionary(g => g.Key, g => g.First());

			var instances = active
				.GroupBy(i => i.RouteInstanceId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			var deviationCounts = deviations
				.GroupBy(d => d.RouteInstanceId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

			data.ActiveRoutes = active
				.OrderByDescending(i => i.ActualStartOn ?? i.AddedOn)
				.Take(MaxRows)
				.Select(i =>
				{
					plans.TryGetValue(i.RoutePlanId ?? string.Empty, out var plan);
					units.TryGetValue(i.UnitId, out var unit);
					deviationCounts.TryGetValue(i.RouteInstanceId ?? string.Empty, out var count);

					return new UnitsRouteInstance
					{
						RouteInstanceId = i.RouteInstanceId,
						RoutePlanId = i.RoutePlanId,
						PlanName = plan?.Name,
						PlanColor = DashboardStatusPalette.Hex(plan?.RouteColor),
						UnitId = i.UnitId,
						UnitName = unit?.Name,
						Status = i.Status,
						StartedOnUtc = i.ActualStartOn,
						StopsCompleted = i.StopsCompleted,
						StopsTotal = i.StopsTotal,
						DeviationCount = count,
						Url = InstanceUrl(i.RouteInstanceId)
					};
				}).ToList();

			data.Deviations = deviations
				.OrderByDescending(d => d.DetectedOn)
				.Take(MaxRows)
				.Select(d =>
				{
					instances.TryGetValue(d.RouteInstanceId ?? string.Empty, out var instance);
					RoutePlan plan = null;
					Unit unit = null;
					if (instance != null)
					{
						plans.TryGetValue(instance.RoutePlanId ?? string.Empty, out plan);
						units.TryGetValue(instance.UnitId, out unit);
					}

					return new UnitsRouteDeviation
					{
						RouteDeviationId = d.RouteDeviationId,
						RouteInstanceId = d.RouteInstanceId,
						PlanName = plan?.Name,
						UnitName = unit?.Name,
						DetectedOnUtc = d.DetectedOn,
						DeviationType = d.DeviationType,
						DistanceMeters = d.DeviationDistanceMeters,
						Url = InstanceUrl(d.RouteInstanceId)
					};
				}).ToList();

			return data;
		}

		private static string InstanceUrl(string routeInstanceId) =>
			string.IsNullOrWhiteSpace(routeInstanceId) ? null : "/User/Routes/InstanceDetail?instanceId=" + Uri.EscapeDataString(routeInstanceId);
	}
}
