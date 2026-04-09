using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class WeatherAlertsController : SecureBaseController
	{
		#region Private Members and Constructors

		private readonly IWeatherAlertService _weatherAlertService;
		private readonly IDepartmentsService _departmentsService;

		public WeatherAlertsController(IWeatherAlertService weatherAlertService, IDepartmentsService departmentsService)
		{
			_weatherAlertService = weatherAlertService;
			_departmentsService = departmentsService;
		}

		#endregion Private Members and Constructors

		public async Task<IActionResult> Index()
		{
			ViewBag.DepartmentId = DepartmentId;

			return View();
		}

		public async Task<IActionResult> Details(string id)
		{
			if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out var alertId))
				return RedirectToAction("Index");

			var alert = await _weatherAlertService.GetAlertByIdAsync(alertId);

			if (alert == null || alert.DepartmentId != DepartmentId)
				return RedirectToAction("Index");

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			ViewBag.Department = department;

			return View(alert);
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Settings()
		{
			var sources = await _weatherAlertService.GetSourcesByDepartmentIdAsync(DepartmentId);

			ViewBag.Sources = sources;
			ViewBag.DepartmentId = DepartmentId;

			return View();
		}

		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Zones()
		{
			var zones = await _weatherAlertService.GetZonesByDepartmentIdAsync(DepartmentId);

			ViewBag.Zones = zones;
			ViewBag.DepartmentId = DepartmentId;

			return View();
		}

		public async Task<IActionResult> History()
		{
			ViewBag.DepartmentId = DepartmentId;

			return View();
		}
	}
}
