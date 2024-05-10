using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.WebCore.Areas.User.Models.Voice;
using Resgrid.Web.Areas.User.Models.Workshifts;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using System;
using Resgrid.Model.Helpers;
using System.Linq;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class WorkshiftsController : SecureBaseController
	{
		private readonly IWorkShiftsService _workShiftsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUnitsService _unitsService;
		private readonly IUsersService _usersService;
		
		public WorkshiftsController(IWorkShiftsService workShiftsService, IAuthorizationService authorizationService,
			IDepartmentsService departmentsService, IUnitsService unitsService, IUsersService usersService)
		{
			_workShiftsService = workShiftsService;
			_authorizationService = authorizationService;
			_departmentsService = departmentsService;
			_unitsService = unitsService;
			_usersService = usersService;
		}

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			return View();
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Create)]
		public async Task<IActionResult> New()
		{
			var model = new NewWorkshiftView();
			model.Shift = new Workshift();

			var dep = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Shift.Start = DateTime.UtcNow.TimeConverter(dep);
			model.Shift.End = DateTime.UtcNow.AddDays(1).TimeConverter(dep);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_Create)]
		public async Task<IActionResult> New(NewWorkshiftView model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				model.Shift.DepartmentId = DepartmentId;
				model.Shift.AddedOn = DateTime.UtcNow;
				model.Shift.AddedById = UserId;

				if (model.UnitsAssigned != null && model.UnitsAssigned.Any())
				{
					model.Shift.Entities = new List<WorkshiftEntity>();
					//List<string> unitIds = model.UnitsAssigned.Split(',').ToList();

					foreach (var unitId in model.UnitsAssigned)
					{
						var unit = new WorkshiftEntity();
						unit.BackingId = unitId;
						model.Shift.Entities.Add(unit);
					}
				}

				var savedShift = await _workShiftsService.AddWorkshiftAsync(model.Shift, cancellationToken);

				return RedirectToAction("Index", "Shifts");
			}
			
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public async Task<IActionResult> Edit(string shiftId)
		{
			var model = new NewWorkshiftView();
			model.Shift = await _workShiftsService.GetWorkshiftByIdAsync(shiftId);

			if (model.Shift == null)
				Unauthorized();

			if (model.Shift.DepartmentId != DepartmentId)
				Unauthorized();

			var dep = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.Shift.Start = model.Shift.Start.TimeConverter(dep);
			model.Shift.End = model.Shift.End.TimeConverter(dep);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_Create)]
		public async Task<IActionResult> Edit(NewWorkshiftView model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				model.Shift.DepartmentId = DepartmentId;
				model.Shift.AddedOn = DateTime.UtcNow;
				model.Shift.AddedById = UserId;

				if (model.UnitsAssigned != null && model.UnitsAssigned.Any())
				{
					model.Shift.Entities = new List<WorkshiftEntity>();
					//List<string> unitIds = model.UnitsAssigned.Split(',').ToList();

					foreach (var unitId in model.UnitsAssigned)
					{
						var unit = new WorkshiftEntity();
						unit.BackingId = unitId;
						model.Shift.Entities.Add(unit);
					}
				}

				var savedShift = await _workShiftsService.EditWorkshiftAsync(model.Shift, UserId, IpAddressHelper.GetRequestIP(Request, true), $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);

				return RedirectToAction("Index", "Shifts");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ViewDay(string dayId)
		{
			var model = new ViewWorkshiftDayView();
			model.Day = await _workShiftsService.GetWorkshiftDayByIdAsync(dayId);

			if (model.Day == null)
				Unauthorized();

			model.Shift = await _workShiftsService.GetWorkshiftByIdAsync(model.Day.WorkshiftId);

			if (model.Shift == null)
				Unauthorized();

			if (model.Shift.DepartmentId != DepartmentId)
				Unauthorized();

			model.Units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			model.Personnel = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(DepartmentId, false, false, false);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Delete)]
		public async Task<IActionResult> DeleteShift(string shiftId)
		{
			var model = new DeleteStaticShiftView();

			model.Shift = await _workShiftsService.GetWorkshiftByIdAsync(shiftId);

			if (model.Shift == null)
				Unauthorized();

			if (model.Shift.DepartmentId != DepartmentId)
				Unauthorized();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_Delete)]
		public async Task<IActionResult> DeleteShift(DeleteStaticShiftView model, CancellationToken cancellationToken)
		{
			model.Shift = await _workShiftsService.GetWorkshiftByIdAsync(model.Shift.WorkshiftId);

			if (model.Shift == null)
				Unauthorized();

			if (model.Shift.DepartmentId != DepartmentId)
				Unauthorized();

			await _workShiftsService.DeleteWorkshiftByIdAsync(model.Shift.WorkshiftId, UserId, DepartmentId, IpAddressHelper.GetRequestIP(Request, true), $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}", cancellationToken);

			return RedirectToAction("Index", "Shifts");
		}
	}
}
