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
using Resgrid.Web.Areas.User.Models.Units;

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
		private readonly IRoutePlanRoutingService _routePlanRoutingService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly IRoutingService _routingService;
		private readonly IGeoService _geoService;

		public RoutesController(IRouteService routeService, IUnitsService unitsService, ICallsService callsService, IContactsService contactsService, IDepartmentsService departmentsService,
			IRoutePlanRoutingService routePlanRoutingService, IDepartmentGroupsService departmentGroupsService, Model.Services.IAuthorizationService authorizationService,
			IRoutingService routingService, IGeoService geoService)
		{
			_routingService = routingService;
			_geoService = geoService;
			_authorizationService = authorizationService;
			_routeService = routeService;
			_unitsService = unitsService;
			_callsService = callsService;
			_contactsService = contactsService;
			_departmentsService = departmentsService;
			_routePlanRoutingService = routePlanRoutingService;
			_departmentGroupsService = departmentGroupsService;
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
			model.Editor = await BuildEditorContextAsync(model.Units);
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

				// The route itself is generated on the server after the stops are saved; never take it from the form.
				await ApplyRoutingOptionsAsync(model.Plan, model.Plan, isNew: true);
				ClearGeneratedRoute(model.Plan);

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
							GeofenceRadiusMeters = ps.GeofenceRadius > 0 ? ps.GeofenceRadius : null,
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

				await RegenerateAsync(model.Plan.RoutePlanId);
				return RedirectToAction("Index");
			}

			model.Units = (await _unitsService.GetUnitsForDepartmentAsync(DepartmentId)).ToList();
			model.Contacts = (await _contactsService.GetAllContactsForDepartmentAsync(DepartmentId)).ToList();
			model.Editor = await BuildEditorContextAsync(model.Units);
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
			model.Editor = await BuildEditorContextAsync(model.Units);
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

				// RD2: apply the form's fields to the stored plan instead of saving the posted plan whole, which nulled the
				// generated route and every field the form does not carry.
				existing.Name = model.Plan.Name;
				existing.Description = model.Plan.Description;
				existing.UnitId = model.Plan.UnitId;
				existing.RouteStatus = model.Plan.RouteStatus;
				existing.RouteColor = model.Plan.RouteColor;
				existing.GeofenceRadiusMeters = model.Plan.GeofenceRadiusMeters;
				existing.UseStationAsStart = model.Plan.UseStationAsStart;
				existing.UseStationAsEnd = model.Plan.UseStationAsEnd;
				existing.OptimizeStopOrder = model.Plan.OptimizeStopOrder;
				existing.StartLatitude = model.Plan.StartLatitude;
				existing.StartLongitude = model.Plan.StartLongitude;
				existing.EndLatitude = model.Plan.EndLatitude;
				existing.EndLongitude = model.Plan.EndLongitude;
				await ApplyRoutingOptionsAsync(existing, model.Plan, isNew: false);
				existing.RouteIsStale = true;
				existing.UpdatedById = UserId;
				existing.UpdatedOn = DateTime.UtcNow;

				await _routeService.SaveRoutePlanAsync(existing, cancellationToken);
				await RegenerateAsync(existing.RoutePlanId);

				return RedirectToAction("Index");
			}

			var stored = await _routeService.GetRoutePlanByIdAsync(model.Plan?.RoutePlanId);
			if (stored == null || stored.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			model.Stops = await _routeService.GetRouteStopsForPlanAsync(stored.RoutePlanId);
			model.Schedules = await _routeService.GetSchedulesForPlanAsync(stored.RoutePlanId);
			model.Units = (await _unitsService.GetUnitsForDepartmentAsync(DepartmentId)).ToList();
			model.Contacts = (await _contactsService.GetAllContactsForDepartmentAsync(DepartmentId)).ToList();
			model.Editor = await BuildEditorContextAsync(model.Units);
			return View(model);
		}

		/// <summary>
		/// The best order for the plan's stops with its start and end fixed, against the current order (§10.6 Optimize
		/// order). Writes nothing; the editor shows both and applies the proposal only when accepted.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Route_Update)]
		public async Task<IActionResult> PreviewOptimization(string id, CancellationToken cancellationToken)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(id);
			if (plan == null || plan.DepartmentId != DepartmentId || plan.RouteStatus == (int)RouteStatus.Archived)
				return Json(new { successful = false });

			RoutePlanOptimizationPreview preview;
			try
			{
				preview = await _routePlanRoutingService.PreviewOptimizationAsync(id, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Previewing the optimized stop order for route plan {id}");
				return Json(new { successful = false });
			}

			var names = (await _routeService.GetRouteStopsForPlanAsync(id)).ToDictionary(s => s.RouteStopId, s => s.Name);
			object Stops(IEnumerable<string> order) => order.Select(stopId => new { id = stopId, name = names.TryGetValue(stopId, out var name) ? name : stopId }).ToList();

			return Json(new
			{
				successful = preview.Successful,
				changed = preview.Changed,
				current = Stops(preview.CurrentOrder),
				proposed = Stops(preview.ProposedOrder),
				currentDistanceMeters = preview.CurrentDistanceMeters,
				currentDurationSeconds = preview.CurrentDurationSeconds,
				proposedDistanceMeters = preview.ProposedDistanceMeters,
				proposedDurationSeconds = preview.ProposedDurationSeconds
			});
		}

		/// <summary>Renumbers the plan's stops 1..n in the posted order (drag reorder, accepted optimization) and regenerates the route.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Route_Update)]
		public async Task<IActionResult> ApplyStopOrder(string id, [FromForm] List<string> stopIds, CancellationToken cancellationToken)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(id);
			if (plan == null || plan.DepartmentId != DepartmentId || plan.RouteStatus == (int)RouteStatus.Archived)
				return Json(new { success = false });

			// Every stop of this plan exactly once: an order naming another plan's stop, or missing one, is refused.
			var planStops = (await _routeService.GetRouteStopsForPlanAsync(id)).Select(s => s.RouteStopId).ToHashSet();
			if (stopIds == null || stopIds.Count != planStops.Count || stopIds.Distinct().Count() != stopIds.Count || !stopIds.All(planStops.Contains))
				return Json(new { success = false });

			try
			{
				// Stored writes ignore request cancellation (a closed tab must not leave the stops half renumbered).
				await _routePlanRoutingService.ApplyOrderAsync(id, stopIds, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Applying a stop order to route plan {id}");
				return Json(new { success = false });
			}

			return Json(new { success = true });
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

			if (await CurrentRouteAsync(id) is { Stored: true })
				plan = await _routeService.GetRoutePlanByIdAsync(id) ?? plan;

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
			string latitude, string longitude, string address, int? callId, int? geofenceRadius,
			string plannedArrival, string plannedDeparture, int? dwellMinutes, string contactName, string contactNumber, string contactId, string notes,
			CancellationToken cancellationToken)
		{
			var plan = await _routeService.GetRoutePlanByIdAsync(routePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId)
				return Json(new { success = false, message = "Not found" });

			// Coordinates come from script as invariant numbers; model binding would read "39.5" as 395 under a German UI.
			if (!TryParseCoordinates(latitude, longitude, out var stopLatitude, out var stopLongitude))
				return Json(new { success = false, message = "Invalid location" });

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
				Latitude = stopLatitude,
				Longitude = stopLongitude,
				Address = address,
				CallId = callId,
				GeofenceRadiusMeters = geofenceRadius > 0 ? geofenceRadius : null,
				EstimatedDwellMinutes = dwellMinutes,
				ContactName = contactName,
				ContactNumber = contactNumber,
				ContactId = resolvedContactId,
				Notes = notes,
				// RD3: after the last stop, never on top of one (deletes renumber 1..n, so this is Count + 1).
				StopOrder = existingStops.Count == 0 ? 1 : existingStops.Max(s => s.StopOrder) + 1,
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
			await RegenerateAsync(routePlanId);
			return Json(new { success = true });
		}

		/// <summary>A stop marker dragged on the editor map: the stop's new location, then the route regenerated.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Route_Update)]
		public async Task<IActionResult> MoveStop(string stopId, string latitude, string longitude)
		{
			var stop = string.IsNullOrWhiteSpace(stopId) ? null : await _routeService.GetRouteStopByIdAsync(stopId);
			if (stop == null || stop.IsDeleted || !TryParseCoordinates(latitude, longitude, out var stopLatitude, out var stopLongitude))
				return Json(new { success = false });

			var plan = await _routeService.GetRoutePlanByIdAsync(stop.RoutePlanId);
			if (plan == null || plan.DepartmentId != DepartmentId || plan.RouteStatus == (int)RouteStatus.Archived)
				return Json(new { success = false });

			stop.Latitude = stopLatitude;
			stop.Longitude = stopLongitude;
			await _routeService.SaveRouteStopAsync(stop, CancellationToken.None);
			await RegenerateAsync(plan.RoutePlanId);

			return Json(new { success = true });
		}

		private static bool TryParseCoordinates(string latitude, string longitude, out decimal parsedLatitude, out decimal parsedLongitude)
		{
			parsedLongitude = 0;
			return decimal.TryParse(latitude, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsedLatitude) &&
			       decimal.TryParse(longitude, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsedLongitude) &&
			       parsedLatitude >= -90 && parsedLatitude <= 90 && parsedLongitude >= -180 && parsedLongitude <= 180;
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
					await RegenerateAsync(p.RoutePlanId);
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

			var unit = await _unitsService.GetUnitByIdAsync(model.SelectedUnitId);
			if (unit == null || unit.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			// Planned ETAs come from the stored route, so bring it up to date first (best effort).
			await CurrentRouteAsync(model.RoutePlanId);

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

			var route = await CurrentRouteAsync(id);
			if (route is { Stored: true })
				plan = await _routeService.GetRoutePlanByIdAsync(id) ?? plan;

			var model = new RouteDirectionsView();
			model.Plan = plan;
			model.ServerRoute = route is { Successful: true } ? route : null;
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
			model.CanSeeTrack = await _authorizationService.CanUserViewUnitLocationViaMatrixAsync(instance.UnitId, UserId, DepartmentId);
			if (model.CanSeeTrack)
				model.Deviations = await _routeService.GetDeviationsForInstanceAsync(instanceId);
			return View(model);
		}

		/// <summary>
		/// Routing options from the form: the legacy profile string as before, plus the profile, avoid options and stations
		/// when the form carries them. Posted stations and units must be the department's.
		/// </summary>
		private async Task ApplyRoutingOptionsAsync(RoutePlan target, RoutePlan posted, bool isNew)
		{
			var form = Request.HasFormContentType ? Request.Form : null;
			bool Posted(string field) => form != null && form.ContainsKey("Plan." + field);

			target.MapboxRouteProfile = posted.MapboxRouteProfile;
			var fromString = RoutingProfileNames.FromLegacyProfile(posted.MapboxRouteProfile);

			if (Posted(nameof(RoutePlan.RoutingProfile)) && Enum.IsDefined(typeof(RoutingProfile), posted.RoutingProfile))
			{
				target.RoutingProfile = posted.RoutingProfile;
				target.MapboxRouteProfile = RoutingProfileNames.ToLegacyProfile((RoutingProfile)posted.RoutingProfile);
			}
			else if (isNew || !Enum.IsDefined(typeof(RoutingProfile), target.RoutingProfile) ||
			         RoutingProfileNames.ToLegacyProfile((RoutingProfile)target.RoutingProfile) != RoutingProfileNames.ToLegacyProfile(fromString))
			{
				// A profile string that still names the stored profile keeps it (Truck is written as "driving").
				target.RoutingProfile = (int)fromString;
			}

			if (Posted(nameof(RoutePlan.AvoidOptions)) || isNew)
				target.AvoidOptions = Posted(nameof(RoutePlan.AvoidOptions)) ? posted.AvoidOptions & 15 : 0;

			if (Posted(nameof(RoutePlan.StartDepartmentGroupId)) || isNew)
				target.StartDepartmentGroupId = Posted(nameof(RoutePlan.StartDepartmentGroupId)) ? await DepartmentStationOrNullAsync(posted.StartDepartmentGroupId) : null;
			if (Posted(nameof(RoutePlan.EndDepartmentGroupId)) || isNew)
				target.EndDepartmentGroupId = Posted(nameof(RoutePlan.EndDepartmentGroupId)) ? await DepartmentStationOrNullAsync(posted.EndDepartmentGroupId) : null;

			if (target.UnitId.HasValue)
			{
				var unit = await _unitsService.GetUnitByIdAsync(target.UnitId.Value);
				if (unit == null || unit.DepartmentId != DepartmentId)
					target.UnitId = null;
			}
		}

		/// <summary>Stations (with coordinates), unit stations and vehicles, and routing capabilities for the editor.</summary>
		private async Task<RouteEditorContext> BuildEditorContextAsync(List<Unit> units)
		{
			var context = new RouteEditorContext
			{
				Metric = VehicleSizeInput.DefaultMetric(),
				VehicleLimits = VehicleSizeInput.LimitsFor(_routingService.ProviderName),
				TrafficAvailable = _routingService.Capabilities?.Traffic == true,
				Attribution = _routingService.Attribution
			};

			try
			{
				foreach (var station in await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>())
				{
					var point = await _geoService.GetStationCoordinatesAsync(station);
					context.Stations.Add(new RouteEditorStation { Id = station.DepartmentGroupId, Name = station.Name, Latitude = point?.Latitude, Longitude = point?.Longitude });
				}

				var types = (await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId) ?? new List<UnitType>())
					.GroupBy(t => t.Type ?? string.Empty).ToDictionary(g => g.Key, g => g.First());

				foreach (var unit in units ?? new List<Unit>())
				{
					if (unit.StationGroupId.HasValue)
						context.UnitStations[unit.UnitId] = unit.StationGroupId.Value;

					types.TryGetValue(unit.Type ?? string.Empty, out var type);
					// Null when neither the unit nor its type has limits.
					var vehicle = VehicleProfile.Resolve(unit, type);
					if (vehicle != null)
						context.UnitVehicles[unit.UnitId] = vehicle;
				}
			}
			catch (Exception ex)
			{
				// The editor works without the preview context; the page still has to open.
				Logging.LogException(ex, $"{nameof(BuildEditorContextAsync)}: editor context failed for departmentId {DepartmentId}.");
			}

			return context;
		}

		private async Task<int?> DepartmentStationOrNullAsync(int? departmentGroupId)
		{
			if (!departmentGroupId.HasValue)
				return null;

			var group = await _departmentGroupsService.GetGroupByIdAsync(departmentGroupId.Value, false);
			return group != null && group.DepartmentId == DepartmentId ? departmentGroupId : null;
		}

		private static void ClearGeneratedRoute(RoutePlan plan)
		{
			plan.MapboxRouteGeometry = null;
			plan.EstimatedDistanceMeters = null;
			plan.EstimatedDurationSeconds = null;
			plan.RouteDirectionsJson = null;
			plan.RouteProvider = null;
			plan.RouteComputedOn = null;
			plan.RouteInputHash = null;
			plan.RouteIsStale = true;
		}

		/// <summary>The plan's current route (regenerated when stale); null when routing fails, which never blocks the page.</summary>
		private async Task<RoutePlanRouteResult> CurrentRouteAsync(string routePlanId)
		{
			try
			{
				return await _routePlanRoutingService.GetCurrentRouteAsync(routePlanId, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Routing route plan {routePlanId}");
				return null;
			}
		}

		/// <summary>Regenerates after a save; a routing failure leaves the plan saved and stale (regenerated on read).</summary>
		private async Task RegenerateAsync(string routePlanId)
		{
			try
			{
				await _routePlanRoutingService.GenerateAsync(routePlanId, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Generating the route for route plan {routePlanId}");
			}
		}
	}
}
