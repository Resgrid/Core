using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Mapping;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Mapping → Road Closures (M0314): draw closed or slowed roads and areas with their details and closure times. Anyone who
	/// can see calls can see them (dispatch ranking already routes around them); department admins and members who can
	/// create calls (dispatchers) can add, change, switch off and delete them.
	/// </summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Call_View)]
	public class RoadClosuresController : SecureBaseController
	{
		private readonly IRoadClosureService _roadClosureService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Mapping.RoadClosures> _localizer;

		public RoadClosuresController(IRoadClosureService roadClosureService, IDepartmentsService departmentsService,
			IDepartmentSettingsService departmentSettingsService, IStringLocalizer<Resgrid.Localization.Areas.User.Mapping.RoadClosures> localizer)
		{
			_roadClosureService = roadClosureService;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_localizer = localizer;
		}

		/// <summary>Department admins and members who can create calls manage closures.</summary>
		internal static bool CanManage() => ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || ClaimsAuthorizationHelper.CanCreateCall();

		[HttpGet]
		public async Task<IActionResult> Index(string filter = null)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var now = DateTime.UtcNow;
			var closures = await _roadClosureService.GetClosuresForDepartmentAsync(DepartmentId);

			var model = new RoadClosuresIndexView
			{
				CanManage = CanManage(),
				Filter = NormalizeFilter(filter)
			};

			await SetCenterAsync(department, (lat, lng) => { model.CenterLatitude = lat; model.CenterLongitude = lng; });

			foreach (var closure in closures)
			{
				var windows = closure.Windows;
				var window = RoadClosureWindows.CurrentOrNext(windows, now);

				model.Closures.Add(new RoadClosureRow
				{
					RoadClosureId = closure.RoadClosureId,
					Name = closure.Name,
					ClosureType = Enum.IsDefined(typeof(RoadClosureTypes), closure.ClosureType) ? (RoadClosureTypes)closure.ClosureType : RoadClosureTypes.Other,
					Severity = closure.SeverityValue,
					State = closure.StateAt(now),
					AppliesTo = closure.AppliesToModes,
					Reason = closure.Reason,
					ClosedBy = closure.ClosedBy,
					IsEnabled = closure.IsEnabled,
					WindowStartsLocal = window?.StartsOn.TimeConverter(department),
					WindowEndsLocal = window?.EndsOn?.TimeConverter(department),
					WindowCount = windows.Count,
					GeometryJson = RoadClosureShape.Normalize(closure.GeometryJson)
				});
			}

			// Running closures first, then the ones still to come, then switched-off and ended ones.
			model.Closures = model.Closures
				.OrderBy(c => c.State)
				.ThenBy(c => c.WindowStartsLocal ?? DateTime.MaxValue)
				.ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> New()
		{
			if (!CanManage())
				return Unauthorized();

			var model = new RoadClosureEditView();
			await SetCenterAsync(await _departmentsService.GetDepartmentByIdAsync(DepartmentId), (lat, lng) => { model.CenterLatitude = lat; model.CenterLongitude = lng; });
			return View("Edit", model);
		}

		[HttpGet]
		public async Task<IActionResult> Edit(string id)
		{
			if (!CanManage())
				return Unauthorized();

			var closure = await _roadClosureService.GetClosureByIdAsync(DepartmentId, id);
			if (closure == null)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var model = RoadClosureEditView.From(closure, utc => utc.TimeConverter(department));
			model.UpdatedOnLocal = closure.UpdatedOn.TimeConverter(department);
			await SetCenterAsync(department, (lat, lng) => { model.CenterLatitude = lat; model.CenterLongitude = lng; });

			return View("Edit", model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Save(RoadClosureEditView model, CancellationToken cancellationToken)
		{
			if (!CanManage())
				return Unauthorized();

			model ??= new RoadClosureEditView();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			RoadClosure closure;
			if (model.IsNew)
			{
				closure = new RoadClosure { DepartmentId = DepartmentId };
			}
			else
			{
				closure = await _roadClosureService.GetClosureByIdAsync(DepartmentId, model.RoadClosureId);
				if (closure == null)
					return NotFound();
			}

			var windows = new List<RoadClosureWindow>();
			foreach (var input in (model.Windows ?? new List<RoadClosureWindowInput>()).Where(w => w != null && !(string.IsNullOrWhiteSpace(w.StartsOn) && string.IsNullOrWhiteSpace(w.EndsOn))))
			{
				if (!RoadClosureWindowInput.TryParse(input.StartsOn, out var startLocal) ||
					(!string.IsNullOrWhiteSpace(input.EndsOn) && !RoadClosureWindowInput.TryParse(input.EndsOn, out _)))
				{
					ModelState.AddModelError(nameof(model.Windows), _localizer["ErrorWindowInvalid"]);
					break;
				}

				DateTime? endUtc = null;
				if (RoadClosureWindowInput.TryParse(input.EndsOn, out var endLocal))
				{
					if (endLocal <= startLocal)
					{
						ModelState.AddModelError(nameof(model.Windows), _localizer["ErrorWindowEndsBeforeStart"]);
						break;
					}

					endUtc = DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified).DepartmentLocalToUtc(department);
				}

				windows.Add(new RoadClosureWindow
				{
					StartsOn = DateTime.SpecifyKind(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc),
					EndsOn = endUtc.HasValue ? DateTime.SpecifyKind(endUtc.Value, DateTimeKind.Utc) : (DateTime?)null
				});
			}

			if (windows.Count > RoadClosureWindows.MaxWindows)
				ModelState.AddModelError(nameof(model.Windows), _localizer["ErrorTooManyWindows", RoadClosureWindows.MaxWindows]);

			closure.Name = model.Name;
			closure.ClosureType = model.ClosureType;
			closure.Severity = model.Severity;
			closure.SpeedPercent = model.SpeedPercent;
			closure.DetourMinutes = model.DetourMinutes;
			closure.AppliesTo = (int)model.AppliesTo;
			closure.GeometryJson = model.GeometryJson;
			closure.IsEnabled = model.IsEnabled;
			closure.Reason = model.Reason;
			closure.ClosedBy = model.ClosedBy;
			closure.Authority = model.Authority;
			closure.ContactName = model.ContactName;
			closure.ContactDetails = model.ContactDetails;
			closure.Notes = model.Notes;
			closure.WindowsJson = windows.Count == 0 ? null : JsonConvert.SerializeObject(windows);

			if (ModelState.IsValid)
			{
				try
				{
					await _roadClosureService.SaveClosureAsync(closure, UserId, cancellationToken);
					return RedirectToAction("Index", new { area = "User" });
				}
				catch (RoadClosureValidationException ex)
				{
					ModelState.AddModelError(ex.Field ?? string.Empty, ErrorText(ex));
				}
			}

			await SetCenterAsync(department, (lat, lng) => { model.CenterLatitude = lat; model.CenterLongitude = lng; });
			return View("Edit", model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SetEnabled(string id, bool enabled, CancellationToken cancellationToken)
		{
			if (!CanManage())
				return Unauthorized();

			if (!await _roadClosureService.SetClosureEnabledAsync(DepartmentId, id, enabled, UserId, cancellationToken))
				return NotFound();

			return RedirectToAction("Index", new { area = "User" });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			if (!CanManage())
				return Unauthorized();

			if (!await _roadClosureService.DeleteClosureAsync(DepartmentId, id, cancellationToken))
				return NotFound();

			return RedirectToAction("Index", new { area = "User" });
		}

		private string ErrorText(RoadClosureValidationException ex)
		{
			switch (ex.Error)
			{
				case RoadClosureValidationErrors.NameRequired:
					return _localizer["ErrorNameRequired"];
				case RoadClosureValidationErrors.NameTooLong:
					return _localizer["ErrorNameTooLong", RoadClosure.MaxNameLength];
				case RoadClosureValidationErrors.ShapeRequired:
					return _localizer["ErrorShapeRequired"];
				case RoadClosureValidationErrors.ShapeTooLarge:
					return _localizer["ErrorShapeTooLarge", RoadClosureShape.MaxVertices];
				case RoadClosureValidationErrors.ShapeInvalid:
					return _localizer["ErrorShapeInvalid"];
				case RoadClosureValidationErrors.TravelModeRequired:
					return _localizer["ErrorTravelModeRequired"];
				case RoadClosureValidationErrors.TooManyWindows:
					return _localizer["ErrorTooManyWindows", RoadClosureWindows.MaxWindows];
				case RoadClosureValidationErrors.WindowEndsBeforeStart:
					return _localizer["ErrorWindowEndsBeforeStart"];
				case RoadClosureValidationErrors.TextTooLong:
					return _localizer["ErrorTextTooLong"];
				default:
					return _localizer["ErrorInvalid"];
			}
		}

		private async Task SetCenterAsync(Department department, Action<double, double> set)
		{
			double latitude = 39.14086268299356, longitude = -119.7583809000;

			try
			{
				var coordinates = department != null ? await _departmentSettingsService.GetMapCenterCoordinatesAsync(department) : null;
				if (coordinates?.Latitude != null && coordinates.Longitude != null)
				{
					latitude = coordinates.Latitude.Value;
					longitude = coordinates.Longitude.Value;
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "Reading the department map center for road closures failed.");
			}

			set(latitude, longitude);
		}

		/// <summary>The list shows "current" (in effect now or still to come) or "all"; anything else is "current".</summary>
		private static string NormalizeFilter(string filter)
		{
			return string.Equals((filter ?? string.Empty).Trim(), "all", StringComparison.OrdinalIgnoreCase) ? "all" : "current";
		}
	}
}
