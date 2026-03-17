using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Routes;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class RoutesController : SecureBaseController
	{
		private readonly IRouteService _routeService;
		private readonly IUnitsService _unitsService;
		private readonly ICallsService _callsService;

		public RoutesController(IRouteService routeService, IUnitsService unitsService, ICallsService callsService)
		{
			_routeService = routeService;
			_unitsService = unitsService;
			_callsService = callsService;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_View)]
		public async Task<IActionResult> Index()
		{
			var model = new RouteIndexView();
			model.Plans = await _routeService.GetRoutePlansForDepartmentAsync(DepartmentId);
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_Create)]
		public async Task<IActionResult> New()
		{
			var model = new RouteNewView();
			model.Units = (await _unitsService.GetUnitsForDepartmentAsync(DepartmentId)).ToList();
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Route_Create)]
		public async Task<IActionResult> New(RouteNewView model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				model.Plan.DepartmentId = DepartmentId;
				model.Plan.AddedById = UserId;
				model.Plan.AddedOn = DateTime.UtcNow;
				model.Plan.IsDeleted = false;

				await _routeService.SaveRoutePlanAsync(model.Plan, cancellationToken);

				return RedirectToAction("Index");
			}

			model.Units = (await _unitsService.GetUnitsForDepartmentAsync(DepartmentId)).ToList();
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_Update)]
		public async Task<IActionResult> Edit(string id)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(id);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			var model = new RouteEditView();
			model.Plan = plan;
			model.Stops = await _routeService.GetRouteStopsForPlanAsync(id);
			model.Schedules = await _routeService.GetSchedulesForPlanAsync(id);
			model.Units = (await _unitsService.GetUnitsForDepartmentAsync(DepartmentId)).ToList();
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Route_Update)]
		public async Task<IActionResult> Edit(RouteEditView model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				var existing = await _routeService.GetRoutePlanByIdAsync(model.Plan.RoutePlanId);
				if (existing == null || existing.DepartmentId != DepartmentId)
					return RedirectToAction("Index");

				model.Plan.DepartmentId = DepartmentId;
				model.Plan.UpdatedById = UserId;
				model.Plan.UpdatedOn = DateTime.UtcNow;
				model.Plan.AddedById = existing.AddedById;
				model.Plan.AddedOn = existing.AddedOn;

				await _routeService.SaveRoutePlanAsync(model.Plan, cancellationToken);

				return RedirectToAction("Index");
			}

			model.Units = (await _unitsService.GetUnitsForDepartmentAsync(DepartmentId)).ToList();
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Route_Delete)]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(id);
			if (plan != null && plan.DepartmentId == DepartmentId)
			{
				await _routeService.DeleteRoutePlanAsync(id, cancellationToken);
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_View)]
		public async Task<IActionResult> View(string id)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(id);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			var model = new RouteDetailView();
			model.Plan = plan;
			model.Stops = await _routeService.GetRouteStopsForPlanAsync(id);
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_View)]
		public async Task<IActionResult> Instances(string routePlanId)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			var instances = await _routeService.GetInstancesForDepartmentAsync(DepartmentId);
			var filtered = instances.Where(i => i.RoutePlanId == routePlanId).ToList();

			var model = new RouteInstancesView();
			model.Plan = plan;
			model.Instances = filtered;
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_View)]
		public async Task<IActionResult> ActiveRoutes()
		{
			var instances = await _routeService.GetInstancesForDepartmentAsync(DepartmentId);
			var active = instances.Where(i => i.Status == (int)RouteInstanceStatus.InProgress || i.Status == (int)RouteInstanceStatus.Paused).ToList();
			var plans = await _routeService.GetRoutePlansForDepartmentAsync(DepartmentId);

			var model = new ActiveRoutesView();
			model.Instances = active;
			model.Plans = plans;
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_View)]
		public async Task<IActionResult> GetCallsForLinking()
		{
			var calls = await _callsService.GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(DepartmentId);
			var active = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var all = calls.Union(active).Distinct().OrderBy(c => c.Name).Select(c => new { id = c.CallId, name = c.Name, address = c.Address }).ToList();
			return Json(all);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Route_Update)]
		public async Task<IActionResult> AddStop(string routePlanId, string name, string description, int stopType, int priority,
			decimal latitude, decimal longitude, string address, int? callId, int? geofenceRadius,
			string plannedArrival, string plannedDeparture, int? dwellMinutes, string contactName, string contactNumber, string notes,
			CancellationToken cancellationToken)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return Json(new { success = false, message = "Not found" });

			var existingStops = await _routeService.GetRouteStopsForPlanAsync(routePlanId);
			var stop = new RouteStop
			{
				RoutePlanId = routePlanId,
				Name = name,
				Description = description,
				StopType = stopType,
				Priority = priority,
				Latitude = latitude,
				Longitude = longitude,
				Address = address,
				CallId = callId,
				GeofenceRadiusMeters = geofenceRadius,
				EstimatedDwellMinutes = dwellMinutes,
				ContactName = contactName,
				ContactNumber = contactNumber,
				Notes = notes,
				StopOrder = existingStops.Count + 1,
				AddedOn = DateTime.UtcNow,
				IsDeleted = false
			};

			if (!string.IsNullOrWhiteSpace(plannedArrival) && DateTime.TryParse(plannedArrival, out var arrivalDt))
				stop.PlannedArrivalTime = arrivalDt.ToUniversalTime();
			if (!string.IsNullOrWhiteSpace(plannedDeparture) && DateTime.TryParse(plannedDeparture, out var departureDt))
				stop.PlannedDepartureTime = departureDt.ToUniversalTime();

			await _routeService.SaveRouteStopAsync(stop, cancellationToken);
			return Json(new { success = true });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Route_Delete)]
		public async Task<IActionResult> DeleteStop(string stopId, CancellationToken cancellationToken)
		{
			// Verify ownership by loading the stop via the plan
			// We load all stops for the department's plans to validate ownership
			var deleted = false;
			var plans = await _routeService.GetRoutePlansForDepartmentAsync(DepartmentId);
			foreach (var p in plans)
			{
				var stops = await _routeService.GetRouteStopsForPlanAsync(p.RoutePlanId);
				if (stops.Any(s => s.RouteStopId == stopId))
				{
					deleted = await _routeService.DeleteRouteStopAsync(stopId, cancellationToken);
					break;
				}
			}

			return Json(new { success = deleted });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_View)]
		public async Task<IActionResult> InstanceDetail(string instanceId)
		{
			var instance = await _routeService.GetInstanceByIdAsync(instanceId);
			if (instance == null || instance.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			var plan = await _routeService.GetRoutePlanByIdAsync(instance.RoutePlanId);
			var stops = await _routeService.GetInstanceStopsAsync(instanceId);

			var model = new RouteInstanceDetailView();
			model.Instance = instance;
			model.Plan = plan;
			model.Stops = stops;
			return View(model);
		}
	}
}
