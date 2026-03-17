using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Routes;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class RoutesController : SecureBaseController
	{
		private readonly IRouteService _routeService;
		private readonly IUnitsService _unitsService;

		public RoutesController(IRouteService routeService, IUnitsService unitsService)
		{
			_routeService = routeService;
			_unitsService = unitsService;
		}

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			var model = new RouteIndexView();
			model.Plans = await _routeService.GetRoutePlansForDepartmentAsync(DepartmentId);
			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> New()
		{
			var model = new RouteNewView();
			model.Units = (await _unitsService.GetUnitsForDepartmentAsync(DepartmentId)).ToList();
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
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
