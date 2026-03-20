using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Framework;
using Resgrid.Web.Areas.User.Models.Routes;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class RoutesController : SecureBaseController
	{
		private readonly IRouteService _routeService;
		private readonly IUnitsService _unitsService;
		private readonly ICallsService _callsService;
		private readonly IContactsService _contactsService;
		private readonly IDepartmentsService _departmentsService;

		public RoutesController(IRouteService routeService, IUnitsService unitsService, ICallsService callsService, IContactsService contactsService, IDepartmentsService departmentsService)
		{
			_routeService = routeService;
			_unitsService = unitsService;
			_callsService = callsService;
			_contactsService = contactsService;
			_departmentsService = departmentsService;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_View)]
		public async Task<IActionResult> Index()
		{
			var model = new RouteIndexView();
			var allPlans = await _routeService.GetRoutePlansForDepartmentAsync(DepartmentId);
			model.Plans = allPlans.Where(p => p.RouteStatus != (int)RouteStatus.Archived).ToList();
			foreach (var plan in model.Plans)
			{
				var stops = await _routeService.GetRouteStopsForPlanAsync(plan.RoutePlanId);
				model.StopCounts[plan.RoutePlanId] = stops.Count;
			}
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_Create)]
		public async Task<IActionResult> New()
		{
			var model = new RouteNewView();
			model.Units = (await _unitsService.GetUnitsForDepartmentAsync(DepartmentId)).ToList();
			model.Contacts = (await _contactsService.GetAllContactsForDepartmentAsync(DepartmentId)).ToList();
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Route_Create)]
		public async Task<IActionResult> New(RouteNewView model, CancellationToken cancellationToken)
		{
			// Deserialize stops before saving anything so a bad payload does not leave an orphaned plan.
			List<PendingStopDto> pendingStops = null;
			if (!string.IsNullOrWhiteSpace(model.PendingStopsJson))
			{
				try
				{
					var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
					pendingStops = System.Text.Json.JsonSerializer.Deserialize<List<PendingStopDto>>(model.PendingStopsJson, options);
				}
				catch (System.Text.Json.JsonException)
				{
					ModelState.AddModelError(nameof(model.PendingStopsJson), "Stop data is invalid and could not be parsed.");
				}
			}

			if (ModelState.IsValid)
			{
				model.Plan.DepartmentId = DepartmentId;
				model.Plan.AddedById = UserId;
				model.Plan.AddedOn = DateTime.UtcNow;
				model.Plan.IsDeleted = false;

				await _routeService.SaveRoutePlanAsync(model.Plan, cancellationToken);

				var dept = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
				var deptTimeZone = !string.IsNullOrWhiteSpace(dept?.TimeZone)
					? DateTimeHelpers.WindowsToIana(dept.TimeZone)
					: "UTC";

				if (pendingStops != null)
				{
					for (int i = 0; i < pendingStops.Count; i++)
					{
						var ps = pendingStops[i];

						string resolvedContactId = null;
						if (!string.IsNullOrWhiteSpace(ps.ContactId))
						{
							var contact = await _contactsService.GetContactByIdAsync(ps.ContactId);
							if (contact != null && contact.DepartmentId == DepartmentId)
								resolvedContactId = ps.ContactId;
						}

						var stop = new RouteStop
						{
							RoutePlanId = model.Plan.RoutePlanId,
							Name = ps.Name,
							Description = ps.Description,
							StopType = ps.StopType,
							Priority = ps.Priority,
							Latitude = ps.Latitude,
							Longitude = ps.Longitude,
							Address = ps.Address,
							CallId = ps.CallId,
							EstimatedDwellMinutes = ps.DwellMinutes,
							ContactName = ps.ContactName,
							ContactNumber = ps.ContactNumber,
							ContactId = resolvedContactId,
							Notes = ps.Notes,
							StopOrder = i + 1,
							AddedOn = DateTime.UtcNow,
							IsDeleted = false
						};

						if (!string.IsNullOrWhiteSpace(ps.PlannedArrival) && DateTime.TryParse(ps.PlannedArrival, out var arrivalDt))
							stop.PlannedArrivalTime = DateTimeHelpers.ConvertToUtc(DateTime.SpecifyKind(arrivalDt, DateTimeKind.Unspecified), deptTimeZone);
						if (!string.IsNullOrWhiteSpace(ps.PlannedDeparture) && DateTime.TryParse(ps.PlannedDeparture, out var departureDt))
							stop.PlannedDepartureTime = DateTimeHelpers.ConvertToUtc(DateTime.SpecifyKind(departureDt, DateTimeKind.Unspecified), deptTimeZone);

						await _routeService.SaveRouteStopAsync(stop, cancellationToken);
					}
				}

				return RedirectToAction("Index");
			}

			model.Units = (await _unitsService.GetUnitsForDepartmentAsync(DepartmentId)).ToList();
			model.Contacts = (await _contactsService.GetAllContactsForDepartmentAsync(DepartmentId)).ToList();
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_Update)]
		public async Task<IActionResult> Edit(string id)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(id);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return RedirectToAction("Index");
			if (plan.RouteStatus == (int)RouteStatus.Archived)
				return RedirectToAction("ArchivedView", new { id });

			var model = new RouteEditView();
			model.Plan = plan;
			model.Stops = await _routeService.GetRouteStopsForPlanAsync(id);
			model.Schedules = await _routeService.GetSchedulesForPlanAsync(id);
			model.Units = (await _unitsService.GetUnitsForDepartmentAsync(DepartmentId)).ToList();
			model.Contacts = (await _contactsService.GetAllContactsForDepartmentAsync(DepartmentId)).ToList();
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
				if (existing.RouteStatus == (int)RouteStatus.Archived)
					return RedirectToAction("ArchivedView", new { id = model.Plan.RoutePlanId });

				model.Plan.DepartmentId = DepartmentId;
				model.Plan.UpdatedById = UserId;
				model.Plan.UpdatedOn = DateTime.UtcNow;
				model.Plan.AddedById = existing.AddedById;
				model.Plan.AddedOn = existing.AddedOn;

				await _routeService.SaveRoutePlanAsync(model.Plan, cancellationToken);

				return RedirectToAction("Index");
			}

			model.Units = (await _unitsService.GetUnitsForDepartmentAsync(DepartmentId)).ToList();
			model.Contacts = (await _contactsService.GetAllContactsForDepartmentAsync(DepartmentId)).ToList();
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Route_Delete)]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(id);
			if (plan != null && plan.DepartmentId == DepartmentId && plan.RouteStatus != (int)RouteStatus.Archived)
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

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_View)]
		public async Task<IActionResult> GetContactsForStop()
		{
			var contacts = await _contactsService.GetAllContactsForDepartmentAsync(DepartmentId);
			var result = contacts.OrderBy(c => c.GetName()).Select(c => new
			{
				id = c.ContactId,
				name = c.GetName(),
				phone = c.CellPhoneNumber ?? c.OfficePhoneNumber ?? c.HomePhoneNumber ?? string.Empty
			}).ToList();
			return Json(result);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Route_Update)]
		public async Task<IActionResult> AddStop(string routePlanId, string name, string description, int stopType, int priority,
			decimal latitude, decimal longitude, string address, int? callId, int? geofenceRadius,
			string plannedArrival, string plannedDeparture, int? dwellMinutes, string contactName, string contactNumber, string contactId, string notes,
			CancellationToken cancellationToken)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return Json(new { success = false, message = "Not found" });

			var existingStops = await _routeService.GetRouteStopsForPlanAsync(routePlanId);

			string resolvedContactId = null;
			if (!string.IsNullOrWhiteSpace(contactId))
			{
				var contact = await _contactsService.GetContactByIdAsync(contactId);
				if (contact != null && contact.DepartmentId == DepartmentId)
					resolvedContactId = contactId;
			}

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
				ContactId = resolvedContactId,
				Notes = notes,
				StopOrder = existingStops.Count + 1,
				AddedOn = DateTime.UtcNow,
				IsDeleted = false
			};

			var dept2 = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var deptTimeZone2 = !string.IsNullOrWhiteSpace(dept2?.TimeZone)
				? DateTimeHelpers.WindowsToIana(dept2.TimeZone)
				: "UTC";

			if (!string.IsNullOrWhiteSpace(plannedArrival) && DateTime.TryParse(plannedArrival, out var arrivalDt))
				stop.PlannedArrivalTime = DateTimeHelpers.ConvertToUtc(DateTime.SpecifyKind(arrivalDt, DateTimeKind.Unspecified), deptTimeZone2);
			if (!string.IsNullOrWhiteSpace(plannedDeparture) && DateTime.TryParse(plannedDeparture, out var departureDt))
				stop.PlannedDepartureTime = DateTimeHelpers.ConvertToUtc(DateTime.SpecifyKind(departureDt, DateTimeKind.Unspecified), deptTimeZone2);

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
		[Authorize(Policy = ResgridResources.Route_Update)]
		public async Task<IActionResult> StartRoute(string id)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(id);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return RedirectToAction("Index");
			if (plan.RouteStatus != (int)RouteStatus.Active)
				return RedirectToAction("View", new { id });

			var model = new RouteStartView();
			model.Plan = plan;
			model.RoutePlanId = id;
			model.Units = (await _unitsService.GetUnitsForDepartmentAsync(DepartmentId)).ToList();
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Route_Update)]
		public async Task<IActionResult> StartRoute(RouteStartView model, CancellationToken cancellationToken)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(model.RoutePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return RedirectToAction("Index");
			if (plan.RouteStatus != (int)RouteStatus.Active)
				return RedirectToAction("View", new { id = model.RoutePlanId });

			try
			{
				var instance = await _routeService.StartRouteAsync(model.RoutePlanId, model.SelectedUnitId, UserId, cancellationToken);
				return RedirectToAction("InstanceDetail", new { instanceId = instance.RouteInstanceId });
			}
			catch (InvalidOperationException ex)
			{
				// Unit already has an active route — let the user pick a different unit.
				ModelState.AddModelError(nameof(model.SelectedUnitId), ex.Message);
				model.Plan = plan;
				model.Units = (await _unitsService.GetUnitsForDepartmentAsync(DepartmentId)).ToList();
				return View(model);
			}
			catch (ArgumentException)
			{
				// Route plan disappeared between validation and start — redirect to Index.
				return RedirectToAction("Index");
			}
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_View)]
		public async Task<IActionResult> Directions(string id)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(id);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return RedirectToAction("Index");
			if (plan.RouteStatus == (int)RouteStatus.Archived)
				return RedirectToAction("ArchivedView", new { id });

			var model = new RouteDirectionsView();
			model.Plan = plan;
			model.Stops = (await _routeService.GetRouteStopsForPlanAsync(id)).OrderBy(s => s.StopOrder).ToList();
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_View)]
		public async Task<IActionResult> ArchivedRoutes()
		{
			var model = new ArchivedRouteIndexView();
			var allPlans = await _routeService.GetRoutePlansForDepartmentAsync(DepartmentId);
			model.Plans = allPlans.Where(p => p.RouteStatus == (int)RouteStatus.Archived).ToList();
			foreach (var plan in model.Plans)
			{
				var stops = await _routeService.GetRouteStopsForPlanAsync(plan.RoutePlanId);
				model.StopCounts[plan.RoutePlanId] = stops.Count;
			}
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_View)]
		public async Task<IActionResult> ArchivedView(string id)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(id);
			if (plan == null || plan.DepartmentId != DepartmentId || plan.RouteStatus != (int)RouteStatus.Archived)
				return RedirectToAction("ArchivedRoutes");

			var model = new RouteDetailView();
			model.Plan = plan;
			model.Stops = await _routeService.GetRouteStopsForPlanAsync(id);
			return View(model);
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
