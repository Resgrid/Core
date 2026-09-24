using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.Calendar;
using Resgrid.Web.Areas.User.Models.Shifts;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class ShiftsController : SecureBaseController
	{
		// The shift calendars load everything at once when they do not send a range.
		private const int CalendarDaysBack = 60;
		private const int CalendarDaysAhead = 180;
		private const int MaxCalendarRangeDays = 400;

		private const string MessageKey = "ShiftsMessage";
		private const string MessageTypeKey = "ShiftsMessageType";

		private static readonly string[] ShiftDateFormats = { "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd", "yyyy/MM/dd" };

		private readonly IShiftsService _shiftsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IDepartmentsService _departmentService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly IWorkShiftsService _workshiftsService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Shifts.Shifts> _localizer;

		public ShiftsController(IShiftsService shiftsService, IDepartmentGroupsService departmentGroupsService, IDepartmentsService departmentService,
			IPersonnelRolesService personnelRolesService, IEventAggregator eventAggregator, IDepartmentSettingsService departmentSettingsService,
			Model.Services.IAuthorizationService authorizationService, IWorkShiftsService workshiftsService,
			IStringLocalizer<Resgrid.Localization.Areas.User.Shifts.Shifts> localizer)
		{
			_shiftsService = shiftsService;
			_departmentGroupsService = departmentGroupsService;
			_departmentService = departmentService;
			_personnelRolesService = personnelRolesService;
			_eventAggregator = eventAggregator;
			_departmentSettingsService = departmentSettingsService;
			_authorizationService = authorizationService;
			_workshiftsService = workshiftsService;
			_localizer = localizer;
		}

		#region Shift definitions

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> Index()
		{
			var model = new ShiftsIndexModel();
			model.Shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId) ?? new List<Shift>();

			var scope = await GetScopeAsync();

			model.IsUserAdminOrGroupAdmin = scope.IsSupervisor;
			model.CanUpdateShifts = User.HasClaim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update);
			model.CanDeleteShifts = User.HasClaim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete);
			model.ManageableShiftIds = new HashSet<int>(model.Shifts.Where(x => x != null && scope.CanManageShift(x)).Select(x => x.ShiftId));

			if (scope.IsSupervisor)
			{
				var pending = await GetPendingApprovalsAsync(scope, model.Shifts);
				model.PendingApprovalsCount = pending.Signups.Count + pending.Trades.Count;
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Create)]
		public async Task<IActionResult> NewShift()
		{
			var model = new NewShiftView();
			model.Shift = new Shift();

			await SetNewShiftScopeAsync(model, await GetScopeAsync());
			ViewBag.ShiftAssignmentTypes = model.AssignmentType.ToSelectList();

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_Create)]
		public async Task<IActionResult> NewShift(NewShiftView model, IFormCollection form, CancellationToken cancellationToken)
		{
			if (model == null)
				return RedirectToAction("NewShift");

			model.Shift = model.Shift ?? new Shift();
			model.Shift.AssignmentType = (int)model.AssignmentType;
			model.Shift.DepartmentId = DepartmentId;
			model.Shift.ScheduleType = (int)ShiftScheduleTypes.Manual;
			model.Shift.Name = form["Shift_Name"];
			model.Shift.Code = form["Shift_Code"];
			model.Shift.StartTime = form["Shift_StartTime"];
			model.Shift.EndTime = form["Shift_EndTime"];
			model.Shift.RequireApproval = model.RequireApproval;

			var scope = await GetScopeAsync();
			await SetNewShiftScopeAsync(model, scope);
			ViewBag.ShiftAssignmentTypes = model.AssignmentType.ToSelectList();

			var groups = ReadShiftGroups(form, null);
			var personnel = model.AssignmentType == ShiftAssignmentTypes.Assigned ? ReadPersonnel(form) : new List<ShiftPerson>();

			// A group-scoped supervisor (a contracted provider) builds shifts for their own groups only, and needs at least
			// one of them on the shift to be able to manage it afterwards.
			if (!scope.AllGroups)
			{
				if (!groups.Any() || groups.Any(x => !scope.GroupIds.Contains(x.DepartmentGroupId)) ||
				    personnel.Any(x => x.GroupId.HasValue && !scope.GroupIds.Contains(x.GroupId.Value)))
					ModelState.AddModelError("", _localizer["ShiftGroupsOutsideScope"]);
			}

			if (ModelState.IsValid)
			{
				if (groups.Any())
					model.Shift.Groups = new Collection<ShiftGroup>(groups);

				if (!String.IsNullOrWhiteSpace(model.Dates))
				{
					model.Shift.Days = new Collection<ShiftDay>();
					var dates = model.Dates.Split(char.Parse(","));

					foreach (var date in dates)
					{
						var shiftDate = DateTimeHelpers.ConvertKendoCalDate(date);

						if (model.Shift.Days.Count == 0)
							model.Shift.StartDay = shiftDate;

						model.Shift.Days.Add(new ShiftDay { Day = shiftDate });
					}
				}
				else
				{
					model.Shift.StartDay = DateTime.UtcNow.TimeConverter(await _departmentService.GetDepartmentByIdAsync(DepartmentId, false));
				}

				if (model.AssignmentType == ShiftAssignmentTypes.Assigned)
					model.Shift.Personnel = new Collection<ShiftPerson>(personnel);

				var newShift = await _shiftsService.SaveShiftAsync(model.Shift, cancellationToken);

				var number = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
				_eventAggregator.SendMessage<ShiftCreatedEvent>(new ShiftCreatedEvent() { DepartmentId = DepartmentId, DepartmentNumber = number, Item = newShift });

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public async Task<IActionResult> EditShiftDetails(int shiftId)
		{
			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);

			if (shift == null)
				return RedirectToAction("Index");

			if (shift.DepartmentId != DepartmentId)
				return Unauthorized();

			var scope = await GetScopeAsync();

			if (!scope.CanManageShift(shift))
				return Unauthorized();

			var model = new EditShiftView();
			model.Shift = await _shiftsService.PopulateShiftData(shift, true, true, true, false, false);
			model.AssignmentType = (ShiftAssignmentTypes)shift.AssignmentType;
			model.RequireApproval = shift.RequireApproval == true;
			await SetEditShiftScopeAsync(model, scope);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public async Task<IActionResult> EditShiftDetails(EditShiftView model, IFormCollection form, CancellationToken cancellationToken)
		{
			if (model?.Shift == null)
				return RedirectToAction("Index");

			var shift = await _shiftsService.GetShiftByIdAsync(model.Shift.ShiftId);

			if (shift == null)
				return RedirectToAction("Index");

			if (shift.DepartmentId != DepartmentId)
				return Unauthorized();

			var scope = await GetScopeAsync();

			if (!scope.CanManageShift(shift))
				return Unauthorized();

			if (ModelState.IsValid)
			{
				shift.Name = model.Shift.Name;
				shift.Code = model.Shift.Code;
				shift.Color = model.Shift.Color;
				shift.StartTime = model.Shift.StartTime;
				shift.EndTime = model.Shift.EndTime;
				shift.RequireApproval = model.RequireApproval;

				// Only the shift row: days, groups, personnel and signups have their own screens, and a cascading save of
				// this loaded instance would re-sync them.
				await _shiftsService.UpdateShiftAsync(shift, cancellationToken);

				// The assignment type comes from the database (the form never posts it). Only an Assigned shift has a
				// standing roster, and only once the page has loaded it into the pickers: saving before they finished
				// loading would otherwise take everyone off the shift.
				if (shift.AssignmentType == (int)ShiftAssignmentTypes.Assigned &&
				    String.Equals(form["PersonnelLoaded"], "true", StringComparison.OrdinalIgnoreCase))
				{
					var personnel = ReadPersonnel(form);

					if (!scope.AllGroups)
					{
						// A group-scoped supervisor only gets pickers for their own groups; people placed in any other group
						// stay where they are.
						personnel.RemoveAll(x => x.GroupId.HasValue && !scope.GroupIds.Contains(x.GroupId.Value));

						foreach (var kept in (shift.Personnel ?? new List<ShiftPerson>()).Where(x => x != null && x.GroupId.HasValue && !scope.GroupIds.Contains(x.GroupId.Value)))
						{
							if (!personnel.Any(x => SameUser(x.UserId, kept.UserId)))
								personnel.Add(new ShiftPerson { UserId = kept.UserId, GroupId = kept.GroupId });
						}
					}

					await _shiftsService.UpdateShiftPersonnel(shift, personnel, cancellationToken);
				}

				var number = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
				_eventAggregator.SendMessage<ShiftUpdatedEvent>(new ShiftUpdatedEvent() { DepartmentId = DepartmentId, DepartmentNumber = number, Item = shift });

				return RedirectToAction("Index");
			}

			model.Shift = await _shiftsService.PopulateShiftData(shift, true, true, true, false, false);
			model.AssignmentType = (ShiftAssignmentTypes)shift.AssignmentType;
			await SetEditShiftScopeAsync(model, scope);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public async Task<IActionResult> EditShiftDays(int shiftId)
		{
			var model = new EditShiftView();

			// A missing or bogus shiftId binds to 0 and GetShiftByIdAsync returns null for it.
			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);

			if (shift == null)
				return RedirectToAction("Index");

			if (shift.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!(await GetScopeAsync()).CanManageShift(shift))
				return Unauthorized();

			model.Shift = shift;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public async Task<IActionResult> EditShiftDays(EditShiftView model, CancellationToken cancellationToken)
		{
			if (model?.Shift == null)
				return RedirectToAction("Index");

			var shift = await _shiftsService.GetShiftByIdAsync(model.Shift.ShiftId);

			if (shift == null)
				return RedirectToAction("Index");

			if (shift.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!(await GetScopeAsync()).CanManageShift(shift))
				return Unauthorized();

			var days = new List<ShiftDay>();
			DateTime? startDay = null;

			if (!String.IsNullOrWhiteSpace(model.Dates))
			{
				var dates = model.Dates.Split(char.Parse(","));

				// A shift that has no days yet comes back from the JSON projection with Days null
				// rather than an empty collection, and that is exactly the case this branch is for.
				var hasExistingDays = shift.Days != null && shift.Days.Count > 0;

				for (int i = 0; i < dates.Length; i++)
				{
					var date = DateTimeHelpers.ConvertKendoCalDate(dates[i]);

					// First posted date wins, matching how NewShift seeds StartDay on creation.
					if (!hasExistingDays && i == 0)
						startDay = date;

					days.Add(new ShiftDay { Day = date });
				}
			}
			else
			{
				startDay = DateTime.UtcNow.TimeConverter(await _departmentService.GetDepartmentByIdAsync(DepartmentId, false));
			}

			await _shiftsService.UpdateShiftDatesAsync(shift, days, cancellationToken);

			// StartDay used to be assigned to the posted model, which is never saved, so it never
			// reached the database. Write it to the loaded shift once the days are in.
			if (startDay.HasValue && shift.StartDay != startDay.Value)
				await _shiftsService.UpdateShiftStartDayAsync(shift, startDay.Value, cancellationToken);

			var number = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
			_eventAggregator.SendMessage<ShiftDaysAddedEvent>(new ShiftDaysAddedEvent() { DepartmentId = DepartmentId, DepartmentNumber = number, Item = shift });

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public async Task<IActionResult> EditShiftGroups(int shiftId)
		{
			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);

			if (shift == null)
				return RedirectToAction("Index");

			if (shift.DepartmentId != DepartmentId)
				return Unauthorized();

			var scope = await GetScopeAsync();

			if (!scope.CanManageShift(shift))
				return Unauthorized();

			var model = new EditShiftView();
			model.Shift = await _shiftsService.PopulateShiftData(shift, true, true, true, false, false);
			SetScope(model, scope);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public async Task<IActionResult> EditShiftGroups(EditShiftView model, IFormCollection form, CancellationToken cancellationToken)
		{
			if (model?.Shift == null)
				return RedirectToAction("Index");

			var shift = await _shiftsService.GetShiftByIdAsync(model.Shift.ShiftId);

			if (shift == null)
				return RedirectToAction("Index");

			if (shift.DepartmentId != DepartmentId)
				return Unauthorized();

			var scope = await GetScopeAsync();

			if (!scope.CanManageShift(shift))
				return Unauthorized();

			model.Shift = shift;
			SetScope(model, scope);

			if (ModelState.IsValid)
			{
				// The loaded shift's own groups: existing rows keep their ids.
				var groups = ReadShiftGroups(form, shift.Groups);

				if (!scope.AllGroups && (!groups.Any() || groups.Any(x => !scope.GroupIds.Contains(x.DepartmentGroupId))))
				{
					ModelState.AddModelError("", _localizer["ShiftGroupsOutsideScope"]);
					return View(model);
				}

				await _shiftsService.UpdateShiftGroupsAsync(shift, groups, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_Delete)]
		public async Task<IActionResult> DeleteShift(int shiftId, CancellationToken cancellationToken)
		{
			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);

			if (shift == null)
				return RedirectToAction("Index");

			if (shift.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!(await GetScopeAsync()).CanManageShift(shift))
				return Unauthorized();

			await _shiftsService.DeleteShift(shift, cancellationToken);
			SetMessage(_localizer["ShiftDeleted"]);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ShiftCalendar(int shiftId)
		{
			var model = new ShiftCalendarView();
			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);

			if (shift == null)
				return RedirectToAction("Index");

			if (shift.DepartmentId != DepartmentId)
				return Unauthorized();

			model.Shift = shift;

			return View(model);
		}

		#endregion Shift definitions

		#region Shift day

		/// <summary>
		/// The old sign-up page. Every shift day now has one page (<see cref="ViewShift"/>) with the roster, needs,
		/// sign-up buttons and supervisor tools, so this only forwards to it.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult Signup(int shiftDayId)
		{
			return RedirectToAction("ViewShift", new { shiftDayId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ViewShift(int shiftDayId)
		{
			var schedule = await _shiftsService.GetShiftDayScheduleAsync(shiftDayId);

			if (schedule?.Shift == null || schedule.Day == null)
				return RedirectToAction("Index");

			if (schedule.Shift.DepartmentId != DepartmentId)
				return Unauthorized();

			var lookups = await GetLookupsAsync();

			return View(await BuildShiftDayViewAsync(schedule, lookups));
		}

		/// <summary>Signing up changes state, so it is a POST; an old link to it just lands on the day.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult ShiftDaySignup(int shiftDayId)
		{
			return RedirectToAction("ViewShift", new { shiftDayId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ShiftDaySignup(int shiftDayId, int? groupId, CancellationToken cancellationToken)
		{
			var (day, shift) = await GetShiftDayWithShiftAsync(shiftDayId);

			if (day == null || shift == null)
				return RedirectToAction("Index");

			if (shift.DepartmentId != DepartmentId)
				return Unauthorized();

			var result = await _shiftsService.SignupUserForShiftDayAsync(shiftDayId, groupId > 0 ? groupId : null, UserId, cancellationToken);

			if (!result.Success)
			{
				SetError(result.Error);
				return RedirectToAction("ViewShift", new { shiftDayId });
			}

			return RedirectToAction("SignupSuccess", new { shiftSignupId = result.Item.ShiftSignupId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> DeleteShiftDaySignup(int shiftSignupId, int shiftDayId, CancellationToken cancellationToken)
		{
			var (signup, denied) = await GetDeletableSignupAsync(shiftSignupId);

			if (denied != null)
				return denied;

			if (signup != null)
				await DeleteSignupUnlessTradedAsync(signup, cancellationToken);

			return RedirectToAction("ViewShift", new { shiftDayId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> SignupSuccess(int shiftSignupId)
		{
			var model = new ShiftSignupView();
			model.Signup = await _shiftsService.GetShiftSignupByIdAsync(shiftSignupId);

			if (model.Signup == null || !SameUser(model.Signup.UserId, UserId))
				return RedirectToAction("YourShifts");

			model.Signup.Shift = await _shiftsService.GetShiftByIdAsync(model.Signup.ShiftId);

			if (model.Signup.Shift == null || model.Signup.Shift.DepartmentId != DepartmentId)
				return RedirectToAction("YourShifts");

			model.ShiftDayId = model.Signup.Shift.Days?.FirstOrDefault(x => x != null && x.Day.Date == model.Signup.ShiftDay.Date)?.ShiftDayId;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> AssignToShiftDay(int shiftDayId, string userId, int? groupId, CancellationToken cancellationToken)
		{
			var (day, shift) = await GetShiftDayWithShiftAsync(shiftDayId);

			if (day == null || shift == null)
				return RedirectToAction("Index");

			if (shift.DepartmentId != DepartmentId)
				return Unauthorized();

			int? group = groupId > 0 ? groupId : null;

			if (!(await GetScopeAsync()).CanManageShiftGroup(shift, group))
				return Unauthorized();

			if (String.IsNullOrWhiteSpace(userId))
			{
				SetMessage(_localizer["SelectPersonToAdd"], "danger");
				return RedirectToAction("ViewShift", new { shiftDayId });
			}

			var result = await _shiftsService.AssignUserToShiftDayAsync(shiftDayId, userId, group, UserId, cancellationToken);

			if (result.Success)
				SetMessage(_localizer["PersonAddedToDay"]);
			else
				SetError(result.Error);

			return RedirectToAction("ViewShift", new { shiftDayId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> RemoveFromShiftDay(int shiftDayId, string userId, string note, CancellationToken cancellationToken)
		{
			var schedule = await _shiftsService.GetShiftDayScheduleAsync(shiftDayId);

			if (schedule?.Shift == null)
				return RedirectToAction("Index");

			if (schedule.Shift.DepartmentId != DepartmentId)
				return Unauthorized();

			var entries = schedule.Roster.Where(x => SameUser(x.UserId, userId)).ToList();

			if (!entries.Any())
			{
				SetError(ShiftActionErrors.NotOnShift);
				return RedirectToAction("ViewShift", new { shiftDayId });
			}

			// The person comes off the whole day, so every slot they hold on it has to be one the caller supervises.
			var scope = await GetScopeAsync();

			if (!entries.All(x => scope.CanManageShiftGroup(schedule.Shift, x.DepartmentGroupId)))
				return Unauthorized();

			var result = await _shiftsService.RemoveUserFromShiftDayAsync(shiftDayId, entries[0].UserId, UserId, note, cancellationToken);

			if (result.Success)
				SetMessage(_localizer["PersonRemovedFromDay"]);
			else
				SetError(result.Error);

			return RedirectToAction("ViewShift", new { shiftDayId });
		}

		#endregion Shift day

		#region Approvals and on duty

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> Approvals()
		{
			var lookups = await GetLookupsAsync();

			if (!lookups.Scope.IsSupervisor)
				return Unauthorized();

			var model = new ApprovalsView { Department = lookups.Department };
			var shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId) ?? new List<Shift>();
			var pending = await GetPendingApprovalsAsync(lookups.Scope, shifts);

			foreach (var signup in pending.Signups)
			{
				var day = FindDay(signup.Shift, signup.ShiftDay);
				var window = ShiftTimeWindow.GetWindow(signup.ShiftDay, signup.Shift.StartTime, signup.Shift.EndTime, signup.Shift.Hours);

				model.Signups.Add(new PendingSignupRow
				{
					ShiftSignupId = signup.ShiftSignupId,
					ShiftDayId = day?.ShiftDayId,
					UserName = lookups.NameFor(signup.UserId),
					Roles = lookups.RoleNamesFor(signup.UserId),
					ShiftName = signup.Shift.Name,
					GroupName = lookups.GroupNameFor(signup.DepartmentGroupId),
					Start = window.Start,
					End = window.End,
					SignupTimestamp = lookups.Department != null ? signup.SignupTimestamp.TimeConverter(lookups.Department) : signup.SignupTimestamp
				});
			}

			foreach (var trade in pending.Trades)
			{
				var source = trade.SourceShiftSignup;
				var day = FindDay(source.Shift, source.ShiftDay);
				var window = ShiftTimeWindow.GetWindow(source.ShiftDay, source.Shift.StartTime, source.Shift.EndTime, source.Shift.Hours);
				var takerId = !String.IsNullOrWhiteSpace(trade.UserId) ? trade.UserId : trade.TargetShiftSignup?.UserId;

				model.Trades.Add(new PendingTradeRow
				{
					ShiftSignupTradeId = trade.ShiftSignupTradeId,
					ShiftDayId = day?.ShiftDayId,
					ShiftName = source.Shift.Name,
					GroupName = lookups.GroupNameFor(source.DepartmentGroupId),
					Start = window.Start,
					End = window.End,
					RequesterName = lookups.NameFor(source.UserId),
					TakerName = lookups.NameFor(takerId),
					TakerRoles = lookups.RoleNamesFor(takerId),
					SwapBackDay = trade.TargetShiftSignup?.ShiftDay,
					SwapBackShiftName = trade.TargetShiftSignup != null ? shifts.FirstOrDefault(x => x.ShiftId == trade.TargetShiftSignup.ShiftId)?.Name : null,
					Note = trade.Note
				});
			}

			model.Signups = model.Signups.OrderBy(x => x.Start).ToList();
			model.Trades = model.Trades.OrderBy(x => x.Start).ToList();

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ReviewSignup(int shiftSignupId, bool approve, string note, int? shiftDayId, CancellationToken cancellationToken)
		{
			var signup = await _shiftsService.GetShiftSignupByIdAsync(shiftSignupId);
			var shift = signup != null ? await _shiftsService.GetShiftByIdAsync(signup.ShiftId) : null;

			if (shift == null)
			{
				SetError(ShiftActionErrors.NotFound);
				return ReturnAfterReview(shiftDayId);
			}

			if (shift.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!(await GetScopeAsync()).CanManageShiftGroup(shift, signup.DepartmentGroupId))
				return Unauthorized();

			var result = await _shiftsService.ReviewShiftSignupAsync(shiftSignupId, approve, UserId, note, cancellationToken);

			if (result.Success)
				SetMessage(approve ? _localizer["SignupApproved"] : _localizer["SignupDenied"]);
			else
				SetError(result.Error);

			return ReturnAfterReview(shiftDayId);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ReviewTrade(int shiftSignupTradeId, bool approve, string note, int? shiftDayId, CancellationToken cancellationToken)
		{
			var trade = await _shiftsService.GetShiftTradeByIdAsync(shiftSignupTradeId);
			var source = trade?.SourceShiftSignup;
			var shift = source != null ? await _shiftsService.GetShiftByIdAsync(source.ShiftId) : null;

			if (shift == null)
			{
				SetError(ShiftActionErrors.NotFound);
				return ReturnAfterReview(shiftDayId);
			}

			if (shift.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!(await GetScopeAsync()).CanManageShiftGroup(shift, source.DepartmentGroupId))
				return Unauthorized();

			var result = await _shiftsService.ReviewTradeAsync(shiftSignupTradeId, approve, UserId, note, cancellationToken);

			if (result.Success)
				SetMessage(approve ? _localizer["TradeApproved"] : _localizer["TradeDenied"]);
			else
				SetError(result.Error);

			return ReturnAfterReview(shiftDayId);
		}

		/// <summary>
		/// Who is on duty right now, for dispatchers: approved roster entries on every running shift day, with single-day
		/// edits and completed trades applied.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> OnDuty()
		{
			var lookups = await GetLookupsAsync(includeScope: false);
			var model = new OnDutyView { Department = lookups.Department, LocalNow = lookups.LocalNow };
			var schedules = await _shiftsService.GetActiveShiftDaySchedulesAsync(DepartmentId, DateTime.UtcNow) ?? new List<ShiftDaySchedule>();

			foreach (var schedule in schedules.Where(x => x?.Shift != null && x.Day != null).OrderBy(x => x.Day.Start).ThenBy(x => x.Shift.Name))
			{
				var shiftView = new OnDutyShiftView
				{
					ShiftId = schedule.Shift.ShiftId,
					ShiftDayId = schedule.Day.ShiftDayId,
					ShiftName = schedule.Shift.Name,
					Color = schedule.Shift.Color,
					Start = schedule.Day.Start,
					End = schedule.Day.End,
					OpenSlots = schedule.OpenSlots()
				};

				var onDuty = schedule.Roster.Where(x => x.IsOnDuty()).ToList();
				var shiftGroupIds = (schedule.Shift.Groups ?? new List<ShiftGroup>()).Where(x => x != null).Select(x => (int?)x.DepartmentGroupId).ToList();

				// The shift's own groups first (shown even when nobody is on them), then anyone placed in another group,
				// then people with no group.
				var groupIds = shiftGroupIds
					.Concat(onDuty.Where(x => x.DepartmentGroupId.HasValue).Select(x => x.DepartmentGroupId))
					.Distinct()
					.ToList();

				if (onDuty.Any(x => !x.DepartmentGroupId.HasValue))
					groupIds.Add(null);

				foreach (var groupId in groupIds)
				{
					var groupView = new OnDutyGroupView
					{
						GroupId = groupId,
						GroupName = groupId.HasValue ? lookups.GroupNameFor(groupId) : _localizer["NoGroup"].Value
					};

					foreach (var entry in onDuty.Where(x => x.DepartmentGroupId == groupId).OrderBy(x => lookups.NameFor(x.UserId)))
					{
						if (groupView.People.Any(x => SameUser(x.UserId, entry.UserId)))
							continue;

						groupView.People.Add(new OnDutyPersonView
						{
							UserId = entry.UserId,
							Name = lookups.NameFor(entry.UserId),
							Roles = lookups.RoleNamesFor(entry.UserId),
							Source = entry.Source,
							TradedFromName = entry.TradedFromUserId != null ? lookups.NameFor(entry.TradedFromUserId) : null
						});
					}

					shiftView.Groups.Add(groupView);
				}

				model.Shifts.Add(shiftView);
			}

			return View(model);
		}

		#endregion Approvals and on duty

		#region Your shifts and trades

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> YourShifts()
		{
			var lookups = await GetLookupsAsync(includeScope: false);
			var model = new YourShiftsView
			{
				Department = lookups.Department,
				LocalNow = lookups.LocalNow,
				UserId = UserId,
				Names = lookups.Names,
				GroupNames = lookups.GroupNames
			};

			// A member of several departments has signups in each; this page is for the active one.
			model.Signups = (await _shiftsService.GetShiftSignupsForUserAsync(UserId) ?? new List<ShiftSignup>())
				.Where(x => x?.Shift != null && x.Shift.DepartmentId == DepartmentId)
				.GroupBy(x => x.ShiftSignupId)
				.Select(x => x.First())
				.OrderBy(x => x.ShiftDay)
				.ToList();

			model.Trades = (await _shiftsService.GetOpenTradeRequestsForUserAsync(UserId) ?? new List<ShiftSignupTrade>())
				.Where(x => x?.SourceShiftSignup?.Shift != null && x.SourceShiftSignup.Shift.DepartmentId == DepartmentId)
				.OrderBy(x => x.SourceShiftSignup.ShiftDay)
				.ToList();

			foreach (var signup in model.Signups)
			{
				var day = FindDay(signup.Shift, signup.ShiftDay);

				if (day != null)
					model.ShiftDayIds[signup.ShiftSignupId] = day.ShiftDayId;
			}

			// Signups are listed without the denied ones, so days a supervisor turned down or took the caller off come from
			// the schedules. Someone put back on the day is on its roster again and not listed.
			var today = lookups.LocalNow.Date;
			var schedules = await _shiftsService.GetShiftDaySchedulesForDateRangeAsync(DepartmentId, today.AddDays(-7), today.AddDays(90)) ?? new List<ShiftDaySchedule>();

			foreach (var schedule in schedules.Where(x => x?.Shift != null && x.Day != null))
			{
				if (schedule.Roster.Any(x => SameUser(x.UserId, UserId)))
					continue;

				var denied = schedule.Signups.Where(x => SameUser(x.UserId, UserId) && x.Denied).OrderByDescending(x => x.ReviewedOn).FirstOrDefault();

				if (denied == null)
					continue;

				denied.Shift = schedule.Shift;
				model.RemovedSignups.Add(denied);
				model.ShiftDayIds[denied.ShiftSignupId] = schedule.Day.ShiftDayId;
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> DeclineShiftDay(int shiftSignupId, CancellationToken cancellationToken)
		{
			var (signup, denied) = await GetDeletableSignupAsync(shiftSignupId);

			if (denied != null)
				return denied;

			if (signup != null)
				await DeleteSignupUnlessTradedAsync(signup, cancellationToken);

			return RedirectToAction("YourShifts");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> RequestTrade(int shiftSignUpId)
		{
			var model = new RequestTradeView();
			model.Signup = await _shiftsService.GetShiftSignupByIdAsync(shiftSignUpId);

			if (model.Signup == null)
				return RedirectToAction("YourShifts");

			// A trade is offered on your own signup, so only its owner gets this page.
			if (!SameUser(model.Signup.UserId, UserId))
				return Unauthorized();

			var shift = await _shiftsService.GetShiftByIdAsync(model.Signup.ShiftId);

			if (shift == null || shift.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!model.Signup.IsActive())
			{
				SetMessage(_localizer["TradeNeedsApprovedSignup"], "warning");
				return RedirectToAction("YourShifts");
			}

			model.Signup.Shift = shift;
			model.ShiftDay = await _shiftsService.GetShiftDayForSignupAsync(shiftSignUpId);

			if (model.ShiftDay == null)
				return RedirectToAction("YourShifts");

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> RequestTrade(RequestTradeView model, IFormCollection form, CancellationToken cancellationToken)
		{
			if (model?.Signup == null)
				return RedirectToAction("YourShifts");

			// The posted Signup only carries its id, so the entity's [Required] members always fail binding validation;
			// everything is checked against the database copy below instead.
			ModelState.Clear();

			var shiftSignupId = model.Signup.ShiftSignupId;
			model.Signup = await _shiftsService.GetShiftSignupByIdAsync(shiftSignupId);

			if (model.Signup == null)
				return RedirectToAction("YourShifts");

			if (!SameUser(model.Signup.UserId, UserId))
				return Unauthorized();

			var shift = await _shiftsService.GetShiftByIdAsync(model.Signup.ShiftId);

			if (shift == null || shift.DepartmentId != DepartmentId)
				return Unauthorized();

			model.Signup.Shift = shift;
			model.ShiftDay = await _shiftsService.GetShiftDayForSignupAsync(shiftSignupId);

			var users = SplitValues(form["users"]);

			if (!users.Any())
			{
				ModelState.AddModelError("users", _localizer["TradeUsersRequired"]);
			}
			else
			{
				var result = await _shiftsService.RequestTradeForSignupAsync(shiftSignupId, UserId, users, model.Note, cancellationToken);

				if (result.Success)
				{
					SetMessage(_localizer["TradeRequested"]);
					return RedirectToAction("YourShifts");
				}

				ModelState.AddModelError("", ErrorText(result.Error));
			}

			if (model.ShiftDay == null)
				return RedirectToAction("YourShifts");

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ProcessTrade(int shiftSignupTradeId)
		{
			var trade = await _shiftsService.GetShiftTradeByIdAsync(shiftSignupTradeId);

			if (trade == null)
				return RedirectToAction("YourShifts");

			var denied = CheckTradeParticipant(trade);

			if (denied != null)
				return denied;

			// Once the requester has picked an offer (and no supervisor has denied it) there is nothing left to answer.
			if (trade.HasSelection() && !trade.Denied)
			{
				SetMessage(_localizer["TradeAlreadyFilled"], "info");
				return RedirectToAction("YourShifts");
			}

			return View(await BuildProcessTradeViewAsync(trade));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ProcessTrade(ProcessTradeView model, IFormCollection form, CancellationToken cancellationToken)
		{
			var tradeId = model?.Trade?.ShiftSignupTradeId ?? 0;

			if (tradeId <= 0)
				int.TryParse(form["shiftSignupTradeId"], out tradeId);

			var trade = await _shiftsService.GetShiftTradeByIdAsync(tradeId);

			if (trade == null)
				return RedirectToAction("YourShifts");

			var denied = CheckTradeParticipant(trade);

			if (denied != null)
				return denied;

			var offered = SplitValues(form["dates"])
				.Select(x => int.TryParse(x, out var id) ? id : 0)
				.Where(x => x > 0)
				.Distinct()
				.ToList();

			var result = await _shiftsService.RespondToTradeAsync(trade.ShiftSignupTradeId, UserId, true, form["note"], offered, cancellationToken);

			if (result.Success)
			{
				SetMessage(_localizer["TradeResponseSent"]);
				return RedirectToAction("YourShifts");
			}

			ModelState.Clear();
			ModelState.AddModelError("", ErrorText(result.Error));

			return View(await BuildProcessTradeViewAsync(trade));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> RejectTrade(int shiftSignupTradeId, string note, CancellationToken cancellationToken)
		{
			var trade = await _shiftsService.GetShiftTradeByIdAsync(shiftSignupTradeId);

			if (trade == null)
				return RedirectToAction("YourShifts");

			var denied = CheckTradeParticipant(trade);

			if (denied != null)
				return denied;

			var result = await _shiftsService.RespondToTradeAsync(shiftSignupTradeId, UserId, false, note, null, cancellationToken);

			if (result.Success)
				SetMessage(_localizer["TradeDeclinedMessage"]);
			else
				SetError(result.Error);

			return RedirectToAction("YourShifts");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> FinishTrade(int shiftSignupTradeId)
		{
			var trade = await _shiftsService.GetShiftTradeByIdAsync(shiftSignupTradeId);

			if (trade == null)
				return RedirectToAction("YourShifts");

			if (!await CanUserFinishTradeAsync(trade))
				return Unauthorized();

			return View(await BuildFinishTradeViewAsync(trade));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> FinishTrade(FinishTradeView model, string acceptedUserId, int? targetShiftSignupId, CancellationToken cancellationToken)
		{
			if (model?.Trade == null)
				return RedirectToAction("YourShifts");

			var trade = await _shiftsService.GetShiftTradeByIdAsync(model.Trade.ShiftSignupTradeId);

			if (trade == null)
				return RedirectToAction("YourShifts");

			// Authorize against the trade loaded from the database, never the one posted in the form.
			if (!await CanUserFinishTradeAsync(trade))
				return Unauthorized();

			if (String.IsNullOrWhiteSpace(acceptedUserId) && !(targetShiftSignupId > 0))
			{
				var view = await BuildFinishTradeViewAsync(trade);
				view.Message = _localizer["FinishTradeSelectOffer"];
				return View(view);
			}

			// The service checks the pick against the offers actually made on this trade.
			var result = await _shiftsService.FinishTradeAsync(trade.ShiftSignupTradeId, UserId,
				targetShiftSignupId > 0 ? null : acceptedUserId, targetShiftSignupId > 0 ? targetShiftSignupId : null, cancellationToken);

			if (!result.Success)
			{
				var view = await BuildFinishTradeViewAsync(trade);
				view.Message = ErrorText(result.Error);
				return View(view);
			}

			if (result.Item?.ApprovalPending == true)
				SetMessage(_localizer["TradeWaitingForApproval"], "info");
			else
				SetMessage(_localizer["TradeCompleted"]);

			return RedirectToAction("YourShifts");
		}

		#endregion Your shifts and trades

		#region Shift staffing

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ShiftStaffing()
		{
			var scope = await GetScopeAsync();

			if (!scope.IsSupervisor)
				return Unauthorized();

			var model = new ShiftStaffingView();
			await SetShiftStaffingModel(model, scope);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ShiftStaffing(ShiftStaffingView model, IFormCollection form, CancellationToken cancellationToken)
		{
			var scope = await GetScopeAsync();

			if (!scope.IsSupervisor)
				return Unauthorized();

			model = model ?? new ShiftStaffingView();

			var shift = model.ShiftId > 0 ? await _shiftsService.GetShiftByIdAsync(model.ShiftId) : null;

			if (shift != null && shift.DepartmentId != DepartmentId)
				return Unauthorized();

			if (shift != null && !scope.CanSuperviseShift(shift))
				return Unauthorized();

			if (shift == null)
				ModelState.AddModelError("ShiftId", _localizer["ShiftStaffingSelectShift"]);

			ShiftDay shiftDay = null;

			// The picker posts MM/dd/yyyy whatever the server culture is.
			if (!TryParseShiftDate(form["shiftDayPicker"], out var date))
			{
				ModelState.AddModelError("shiftDayPicker", _localizer["ShiftStaffingSelectDay"]);
			}
			else if (shift != null)
			{
				shiftDay = FindDay(shift, date);

				if (shiftDay == null)
					ModelState.AddModelError("shiftDayPicker", _localizer["ShiftStaffingSelectDay"]);
			}

			var selections = ReadPersonnel(form);

			// Every slot the form puts someone in has to be one the caller supervises; a slot with no group needs the
			// whole shift.
			if (shift != null && selections.Any(x => !scope.CanManageShiftGroup(shift, x.GroupId)))
				return Unauthorized();

			if (ModelState.IsValid && shift != null && shiftDay != null)
			{
				var names = await GetNamesAsync();
				var members = await _departmentService.GetAllMembersForDepartmentAsync(DepartmentId) ?? new List<DepartmentMember>();
				var memberIds = members
					.Where(x => x != null && !x.IsDeleted && !String.IsNullOrWhiteSpace(x.UserId))
					.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
					.ToDictionary(x => x.Key, x => x.First().UserId, StringComparer.OrdinalIgnoreCase);

				var staffing = new ShiftStaffing
				{
					ShiftId = shift.ShiftId,
					DepartmentId = DepartmentId,
					Note = model.Note,
					AddedByUserId = UserId,
					AddedOn = DateTime.UtcNow,
					ShiftDay = shiftDay.Day,
					Personnel = new Collection<ShiftStaffingPerson>()
				};

				var schedule = await _shiftsService.GetShiftDayScheduleAsync(shiftDay.ShiftDayId);
				var added = 0;
				var errors = new List<string>();

				foreach (var selection in selections)
				{
					if (!memberIds.TryGetValue(selection.UserId, out var userId))
						continue;

					var person = new ShiftStaffingPerson { UserId = userId, GroupId = selection.GroupId };
					staffing.Personnel.Add(person);

					// Already working the day: nothing to change.
					if (schedule != null && schedule.Roster.Any(x => SameUser(x.UserId, userId) && x.IsOnDuty()))
					{
						person.Assigned = true;
						continue;
					}

					var result = await _shiftsService.AssignUserToShiftDayAsync(shiftDay.ShiftDayId, userId, selection.GroupId, UserId, cancellationToken);

					if (result.Success)
					{
						person.Assigned = true;
						added++;
					}
					else
					{
						errors.Add($"{NameFor(names, userId)}: {ErrorText(result.Error)}");
					}
				}

				await _shiftsService.SaveShiftStaffingAsync(staffing, cancellationToken);

				if (errors.Any())
					SetMessage(String.Format(_localizer["ShiftStaffingSavedWithErrors"].Value, added, String.Join("; ", errors)), "warning");
				else
					SetMessage(String.Format(_localizer["ShiftStaffingSaved"].Value, added));

				return RedirectToAction("ViewShift", new { shiftDayId = shiftDay.ShiftDayId });
			}

			await SetShiftStaffingModel(model, scope);

			return View(model);
		}

		private async Task SetShiftStaffingModel(ShiftStaffingView model, ShiftManagementScope scope)
		{
			var shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId) ?? new List<Shift>();

			model.Shifts = shifts.Where(x => x != null && scope.CanSuperviseShift(x)).ToList();
			model.IsDepartmentAdmin = scope.AllGroups;
			model.ManageableGroupIds = scope.AllGroups ? new List<int>() : scope.GroupIds.ToList();
			model.GroupId = scope.AllGroups ? 0 : scope.GroupIds.FirstOrDefault();
			model.CurrentUnitRoles = new Dictionary<int, List<UnitStateRole>>();
		}

		#endregion Shift staffing

		#region Async Calls

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetShiftCalendarItems(string start = null, string end = null)
		{
			var lookups = await GetLookupsAsync(includeScope: false);
			GetCalendarRange(start, end, lookups.LocalNow.Date, out var startDate, out var endDate);

			// One load for the whole range instead of several queries per day of every shift.
			var schedules = await _shiftsService.GetShiftDaySchedulesForDateRangeAsync(DepartmentId, startDate, endDate) ?? new List<ShiftDaySchedule>();
			var calendarItems = schedules.Where(x => x?.Shift != null && x.Day != null).Select(x => ToCalendarItem(x, lookups)).ToList();

			var workshifts = await _workshiftsService.GetAllWorkshiftsByDepartmentAsync(DepartmentId);

			if (workshifts != null && workshifts.Any())
			{
				var department = lookups.Department ?? await _departmentService.GetDepartmentByIdAsync(DepartmentId);

				foreach (var workshift in workshifts)
				{
					if (workshift.DeletedOn == null)
					{
						if (workshift.Days != null && workshift.Days.Any())
						{
							foreach (var day in workshift.Days)
							{
								var item = new ShiftCalendarItemJson();
								item.Color = workshift.Color;
								item.Title = workshift.Name;
								item.Description = workshift.Name;
								item.SignupType = 2;
								item.WorkshiftId = workshift.WorkshiftId;
								item.WorkshiftDayId = day.WorkshiftDayId;
								item.Start = day.Day.TimeConverter(department).SetToMidnight();
								item.End = day.Day.TimeConverter(department).SetToEndOfDay();

								calendarItems.Add(item);
							}
						}
					}
				}
			}

			return Json(calendarItems);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetShiftCalendarItemsForShift(int shiftId, string start = null, string end = null)
		{
			if (shiftId <= 0)
				return Json(new List<ShiftCalendarItemJson>());

			var lookups = await GetLookupsAsync(includeScope: false);
			GetCalendarRange(start, end, lookups.LocalNow.Date, out var startDate, out var endDate);

			// Only this department's shifts are loaded, so another department's shift id simply returns nothing.
			var schedules = await _shiftsService.GetShiftDaySchedulesForDateRangeAsync(DepartmentId, startDate, endDate, shiftId) ?? new List<ShiftDaySchedule>();

			return Json(schedules.Where(x => x?.Shift != null && x.Day != null).Select(x => ToCalendarItem(x, lookups)).ToList());
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetShiftCalendarItemTypes()
		{
			var itemsJson = new List<CalendarItemTypeJson>();
			var shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId) ?? new List<Shift>();

			foreach (var shift in shifts.Where(x => x != null))
			{
				// A shift saved without a colour has none to show.
				if (!String.IsNullOrWhiteSpace(shift.Color) && !shift.Color.Equals("#FFFFFF", StringComparison.InvariantCultureIgnoreCase))
				{
					var item = new CalendarItemTypeJson();
					item.CalendarItemTypeId = shift.ShiftId.ToString();
					item.Name = shift.Name;
					item.Color = shift.Color;

					itemsJson.Add(item);
				}
			}

			return Json(itemsJson);
		}

		/// <summary>
		/// The people placed on a shift, for the personnel pickers: groupId &gt; 0 the people in that group, 0 the people
		/// with no group, none everyone. Without a day it is the standing roster; with a day (MM/dd/yyyy) it is who is on
		/// duty that day, so a supervisor's single-day changes are what the staffing page starts from.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetPersonnelForShift(int shiftId, int? groupId, string day = null)
		{
			var usersJson = new List<UserJson>();
			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);

			if (shift == null || shift.DepartmentId != DepartmentId)
				return Json(usersJson);

			var names = await GetNamesAsync();
			var placements = (shift.Personnel ?? new List<ShiftPerson>())
				.Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId))
				.Select(x => (x.UserId, x.GroupId))
				.ToList();

			if (TryParseShiftDate(day, out var date))
			{
				var shiftDay = FindDay(shift, date);
				var schedule = shiftDay != null ? await _shiftsService.GetShiftDayScheduleAsync(shiftDay.ShiftDayId) : null;

				if (schedule != null)
					placements = schedule.Roster.Where(x => x.IsOnDuty()).Select(x => (x.UserId, x.DepartmentGroupId)).ToList();
			}

			foreach (var (userId, placedGroupId) in placements)
			{
				var include = !groupId.HasValue ||
				              (groupId.Value > 0 && placedGroupId == groupId.Value) ||
				              (groupId.Value == 0 && !placedGroupId.HasValue);

				if (include && !usersJson.Any(x => SameUser(x.UserId, userId)))
					usersJson.Add(new UserJson { UserId = userId, Name = NameFor(names, userId) });
			}

			return Json(usersJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetShiftGroups(int shiftId)
		{
			var groups = new List<dynamic>();
			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);

			if (shift != null && shift.Groups != null)
			{
				if (shift.DepartmentId != DepartmentId)
					return Unauthorized();

				var scope = await GetScopeAsync();
				var departmentGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();

				foreach (var group in shift.Groups.Where(x => x != null))
				{
					// The group row can be missing (deleted group), so fall back to the department's list.
					var name = group.DepartmentGroup?.Name ?? departmentGroups.FirstOrDefault(x => x.DepartmentGroupId == group.DepartmentGroupId)?.Name ?? "";

					groups.Add(new
					{
						Id = group.DepartmentGroupId,
						Name = name,
						CanManage = scope.CanManageGroup(group.DepartmentGroupId)
					});
				}
			}

			return Json(groups);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetShiftDays(int shiftId)
		{
			var days = new List<dynamic>();
			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);

			if (shift != null && shift.Days != null)
			{
				if (shift.DepartmentId != DepartmentId)
					return Unauthorized();

				foreach (var day in shift.Days.Where(x => x != null))
				{
					days.Add(new
					{
						Processed = day.Processed.GetValueOrDefault(),
						// The date pickers read mm/dd/yyyy; the server culture's short date (24.09.2026) broke them.
						Day = day.Day.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)
					});
				}
			}

			return Json(days);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetShiftJson(int shiftId)
		{
			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);

			if (shift == null)
				return NotFound();

			// Returning null from an IActionResult action throws in MVC, so this branch used to
			// surface as a 500 rather than a denial.
			if (shift.DepartmentId != DepartmentId)
				return Unauthorized();

			shift = await _shiftsService.PopulateShiftData(shift, true, true, true, true, true);

			var shiftJson = JsonConvert.SerializeObject(shift);

			return Content(shiftJson, "application/json");
		}

		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetShiftsForDepartmentJson()
		{
			var shiftsJson = new List<dynamic>();
			var shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId);

			if (shifts != null && shifts.Any())
			{
				foreach (var shift in shifts)
				{
					shiftsJson.Add(new
					{
						Id = shift.ShiftId,
						Name = shift.Name
					});
				}
			}

			return Json(shiftsJson);
		}

		/// <summary>
		/// People the owner of a signup can ask to take it: not on that day's roster, and holding the roles of the slot's
		/// group that the owner holds.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetPersonnelNotOnShiftDay(int shiftSignupId, int shiftDayId)
		{
			var usersJson = new List<UserJson>();
			var signup = await _shiftsService.GetShiftSignupByIdAsync(shiftSignupId);

			if (signup == null)
				return Json(usersJson);

			var schedule = await _shiftsService.GetShiftDayScheduleAsync(shiftDayId);

			if (schedule?.Shift == null || schedule.Shift.DepartmentId != DepartmentId || schedule.Shift.ShiftId != signup.ShiftId)
				return Json(usersJson);

			// The resolved roster, so standing-roster people and anyone traded onto the day are left out too.
			var onDay = new HashSet<string>(schedule.Roster.Select(x => x.UserId), StringComparer.OrdinalIgnoreCase);
			var roles = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(DepartmentId) ?? new Dictionary<string, List<PersonnelRole>>();
			var rolesByUser = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

			foreach (var pair in roles.Where(x => x.Key != null))
				rolesByUser[pair.Key] = (pair.Value ?? new List<PersonnelRole>()).Where(x => x != null).Select(x => x.PersonnelRoleId).ToList();

			var requiredRoleIds = new List<int>();
			var signupGroup = schedule.Shift.Groups?.FirstOrDefault(x => x != null && x.DepartmentGroupId == signup.DepartmentGroupId);

			if (signupGroup?.Roles != null && rolesByUser.TryGetValue(signup.UserId, out var ownerRoles))
				requiredRoleIds = signupGroup.Roles.Where(x => x != null && ownerRoles.Contains(x.PersonnelRoleId)).Select(x => x.PersonnelRoleId).Distinct().ToList();

			var names = await GetNamesAsync();
			var selectable = new HashSet<string>((await _departmentService.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>()).Select(x => x.UserId), StringComparer.OrdinalIgnoreCase);
			var members = await _departmentService.GetAllMembersForDepartmentAsync(DepartmentId) ?? new List<DepartmentMember>();

			foreach (var member in members.Where(x => x != null && !x.IsDeleted && !String.IsNullOrWhiteSpace(x.UserId)))
			{
				if (!selectable.Contains(member.UserId) || onDay.Contains(member.UserId) || SameUser(member.UserId, signup.UserId) ||
				    usersJson.Any(x => SameUser(x.UserId, member.UserId)))
					continue;

				var memberRoles = rolesByUser.TryGetValue(member.UserId, out var held) ? held : new List<int>();

				if (requiredRoleIds.All(memberRoles.Contains))
					usersJson.Add(new UserJson { UserId = member.UserId, Name = NameFor(names, member.UserId) });
			}

			return Json(usersJson.OrderBy(x => x.Name).ToList());
		}

		/// <summary>
		/// The caller's own upcoming, approved signups they can offer back when answering a trade request, leaving out the
		/// day being traded.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetShiftDaysUserIsOn(int shiftTradeId)
		{
			var shiftDayJson = new List<ShiftDayJson>();
			var trade = await _shiftsService.GetShiftTradeByIdAsync(shiftTradeId);

			if (trade?.SourceShiftSignup == null || CheckTradeParticipant(trade) != null)
				return Json(shiftDayJson);

			var department = await _departmentService.GetDepartmentByIdAsync(DepartmentId, false);
			var localNow = department != null ? DateTime.UtcNow.TimeConverter(department) : DateTime.UtcNow;
			var tradeDay = trade.SourceShiftSignup.ShiftDay.Date;

			var signups = await _shiftsService.GetShiftSignupsForUserAsync(UserId) ?? new List<ShiftSignup>();

			// GetShiftSignupsForUserAsync also appends other people's days that were traded to the caller; those belong to
			// someone else and cannot be offered.
			foreach (var signup in signups.Where(x => x?.Shift != null && SameUser(x.UserId, UserId) && x.IsActive()).OrderBy(x => x.ShiftDay))
			{
				if (signup.Shift.DepartmentId != DepartmentId || signup.ShiftSignupId == trade.SourceShiftSignupId || signup.ShiftDay.Date == tradeDay)
					continue;

				// A day already traded away, or waiting for a supervisor to approve one, is not the caller's to give.
				if (IsTradeLocked(signup))
					continue;

				if (ShiftTimeWindow.GetWindow(signup.ShiftDay, signup.Shift.StartTime, signup.Shift.EndTime, signup.Shift.Hours).End <= localNow)
					continue;

				shiftDayJson.Add(new ShiftDayJson
				{
					ShiftSignupId = signup.ShiftSignupId,
					Title = String.Format(_localizer["ShiftOnDateFormat"].Value, signup.Shift.Name, signup.ShiftDay.ToShortDateString())
				});
			}

			return Json(shiftDayJson);
		}
		#endregion Async Calls

		#region Helpers

		private sealed class ShiftLookups
		{
			public Department Department { get; set; }
			public DateTime LocalNow { get; set; }
			public ShiftManagementScope Scope { get; set; }
			public string UnknownName { get; set; }
			public Dictionary<string, string> Names { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			public Dictionary<int, string> GroupNames { get; } = new Dictionary<int, string>();
			public Dictionary<int, string> RoleNames { get; } = new Dictionary<int, string>();
			public Dictionary<string, List<PersonnelRole>> RolesByUser { get; } = new Dictionary<string, List<PersonnelRole>>(StringComparer.OrdinalIgnoreCase);

			public string NameFor(string userId)
			{
				return userId != null && Names.TryGetValue(userId, out var name) && !String.IsNullOrWhiteSpace(name) ? name : UnknownName;
			}

			public string GroupNameFor(int? groupId)
			{
				return groupId.HasValue && GroupNames.TryGetValue(groupId.Value, out var name) ? name : "";
			}

			public string RoleNamesFor(string userId)
			{
				if (userId == null || !RolesByUser.TryGetValue(userId, out var roles) || roles == null)
					return "";

				return String.Join(", ", roles.Where(x => x != null && !String.IsNullOrWhiteSpace(x.Name)).Select(x => x.Name).Distinct());
			}
		}

		private async Task<ShiftLookups> GetLookupsAsync(bool includeScope = true)
		{
			var lookups = new ShiftLookups { UnknownName = _localizer["UnknownPerson"].Value };

			lookups.Department = await _departmentService.GetDepartmentByIdAsync(DepartmentId, false);
			lookups.LocalNow = lookups.Department != null ? DateTime.UtcNow.TimeConverter(lookups.Department) : DateTime.UtcNow;
			lookups.Scope = includeScope ? await GetScopeAsync() : ShiftManagementScope.None();

			foreach (var name in await _departmentService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
			{
				if (name != null)
					lookups.Names[name.UserId] = name.Name;
			}

			foreach (var group in await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>())
			{
				if (group != null)
					lookups.GroupNames[group.DepartmentGroupId] = group.Name;
			}

			foreach (var role in await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>())
			{
				if (role != null)
					lookups.RoleNames[role.PersonnelRoleId] = role.Name;
			}

			foreach (var pair in await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(DepartmentId) ?? new Dictionary<string, List<PersonnelRole>>())
			{
				if (pair.Key != null)
					lookups.RolesByUser[pair.Key] = pair.Value ?? new List<PersonnelRole>();
			}

			return lookups;
		}

		private async Task<Dictionary<string, string>> GetNamesAsync()
		{
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach (var name in await _departmentService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
			{
				if (name != null)
					names[name.UserId] = name.Name;
			}

			return names;
		}

		private string NameFor(Dictionary<string, string> names, string userId)
		{
			return userId != null && names.TryGetValue(userId, out var name) && !String.IsNullOrWhiteSpace(name) ? name : _localizer["UnknownPerson"].Value;
		}

		private async Task<ShiftManagementScope> GetScopeAsync()
		{
			return await _authorizationService.GetShiftManagementScopeAsync(UserId, DepartmentId) ?? ShiftManagementScope.None();
		}

		private async Task<ShiftDayView> BuildShiftDayViewAsync(ShiftDaySchedule schedule, ShiftLookups lookups)
		{
			var shift = schedule.Shift;
			var day = schedule.Day;
			var scope = lookups.Scope;
			var shiftGroups = (shift.Groups ?? new List<ShiftGroup>()).Where(x => x != null).ToList();

			var model = new ShiftDayView
			{
				ShiftDayId = day.ShiftDayId,
				ShiftId = shift.ShiftId,
				ShiftName = shift.Name,
				Color = shift.Color,
				AssignmentType = shift.AssignmentType,
				RequireApproval = shift.RequireApproval == true,
				Department = lookups.Department,
				Start = day.Start,
				End = day.End,
				IsActive = schedule.IsActive,
				IsOver = lookups.LocalNow >= day.End,
				IsFilled = schedule.IsFilled(),
				OpenSlots = schedule.OpenSlots(),
				CanSupervise = scope.CanSuperviseShift(shift)
			};

			var rows = schedule.Roster
				.OrderBy(x => lookups.NameFor(x.UserId))
				.Select(entry => new ShiftDayRosterRow
				{
					UserId = entry.UserId,
					Name = lookups.NameFor(entry.UserId),
					GroupId = entry.DepartmentGroupId,
					GroupName = lookups.GroupNameFor(entry.DepartmentGroupId),
					Roles = lookups.RoleNamesFor(entry.UserId),
					Source = entry.Source,
					TradedFromName = entry.TradedFromUserId != null ? lookups.NameFor(entry.TradedFromUserId) : null,
					ApprovalPending = entry.ApprovalPending,
					ShiftSignupId = entry.ShiftSignupId,
					IsYou = SameUser(entry.UserId, UserId),
					CanManage = scope.CanManageShiftGroup(shift, entry.DepartmentGroupId),
					// Your own sign-up, still ahead, that has not been traded (or put up for approval of a trade).
					CanWithdraw = SameUser(entry.UserId, UserId) && entry.Source == ShiftRosterSources.Signup && entry.ShiftSignupId.HasValue &&
					              !model.IsOver && !IsSignupTradeLocked(schedule, entry.ShiftSignupId.Value)
				})
				.ToList();

			var myRows = rows.Where(x => x.IsYou).ToList();
			var allowMultipleGroups = myRows.Any() && await _departmentSettingsService.GetAllowSignupsForMultipleShiftGroupsAsync(DepartmentId);

			foreach (var shiftGroup in shiftGroups.GroupBy(x => x.DepartmentGroupId).Select(x => x.First()))
			{
				var groupId = shiftGroup.DepartmentGroupId;
				var groupView = new ShiftDayGroupView
				{
					DepartmentGroupId = groupId,
					Name = lookups.GroupNames.TryGetValue(groupId, out var groupName) ? groupName : shiftGroup.DepartmentGroup?.Name,
					CanManage = scope.CanManageGroup(groupId),
					Roster = rows.Where(x => x.GroupId == groupId).ToList()
				};

				schedule.Needs.TryGetValue(groupId, out var groupNeeds);

				foreach (var role in (shiftGroup.Roles ?? new List<ShiftGroupRole>()).Where(x => x != null).GroupBy(x => x.PersonnelRoleId))
				{
					var remaining = 0;

					if (groupNeeds != null && groupNeeds.TryGetValue(role.Key, out var needed))
						remaining = Math.Max(needed, 0);

					groupView.Needs.Add(new ShiftDayRoleNeed
					{
						PersonnelRoleId = role.Key,
						RoleName = lookups.RoleNames.TryGetValue(role.Key, out var roleName) ? roleName : role.First().Role?.Name,
						Required = role.Sum(x => Math.Max(x.Required, 0)),
						Optional = role.Sum(x => Math.Max(x.Optional, 0)),
						Remaining = remaining
					});
				}

				groupView.CanSignup = !model.IsOver && !myRows.Any(x => x.GroupId == groupId) && (!myRows.Any() || allowMultipleGroups);
				model.Groups.Add(groupView);
			}

			var slotGroupIds = new HashSet<int>(shiftGroups.Select(x => x.DepartmentGroupId));
			model.OtherRoster = rows.Where(x => !x.GroupId.HasValue || !slotGroupIds.Contains(x.GroupId.Value)).ToList();

			model.CanSignupWithoutGroup = !shiftGroups.Any() && shift.AssignmentType == (int)ShiftAssignmentTypes.Signup && !model.IsOver && !myRows.Any();

			if (myRows.Any(x => !x.ApprovalPending))
			{
				model.MyStatus = ShiftDayUserStatus.OnDuty;
			}
			else if (myRows.Any())
			{
				model.MyStatus = ShiftDayUserStatus.Pending;
			}
			else
			{
				var removed = schedule.Signups.Where(x => SameUser(x.UserId, UserId) && x.Denied).OrderByDescending(x => x.ReviewedOn).FirstOrDefault();

				if (removed != null)
				{
					model.MyStatus = ShiftDayUserStatus.Removed;
					model.MyReviewNote = removed.ReviewNote;
				}
			}

			foreach (var trade in schedule.Trades.Where(x => x?.SourceShiftSignup != null))
			{
				var takerId = !String.IsNullOrWhiteSpace(trade.UserId) ? trade.UserId : trade.TargetShiftSignup?.UserId;
				var status = ShiftTradeStatus.Open;

				if (trade.Denied)
					status = ShiftTradeStatus.Denied;
				else if (trade.ApprovalPending)
					status = ShiftTradeStatus.PendingApproval;
				else if (trade.IsTradeComplete())
					status = ShiftTradeStatus.Complete;

				model.Trades.Add(new ShiftDayTradeRow
				{
					ShiftSignupTradeId = trade.ShiftSignupTradeId,
					FromName = lookups.NameFor(trade.SourceShiftSignup.UserId),
					ToName = takerId != null ? lookups.NameFor(takerId) : null,
					GroupName = lookups.GroupNameFor(trade.SourceShiftSignup.DepartmentGroupId),
					SwapBackDay = trade.TargetShiftSignup?.ShiftDay,
					Note = trade.Note,
					ReviewNote = trade.ReviewNote,
					Status = status,
					CanReview = status == ShiftTradeStatus.PendingApproval && trade.SourceShiftSignup.ShiftId == shift.ShiftId &&
					            scope.CanManageShiftGroup(shift, trade.SourceShiftSignup.DepartmentGroupId)
				});
			}

			if (model.CanSupervise && !model.IsOver)
			{
				foreach (var group in shiftGroups.Where(x => scope.CanManageGroup(x.DepartmentGroupId)).GroupBy(x => x.DepartmentGroupId).Select(x => x.First()))
					model.AddGroupOptions.Add(new ShiftDayOption { Value = group.DepartmentGroupId.ToString(), Text = lookups.GroupNameFor(group.DepartmentGroupId) });

				// A slot with no group belongs to the whole shift.
				if (scope.CanManageShift(shift))
					model.AddGroupOptions.Add(new ShiftDayOption { Value = "0", Text = _localizer["NoGroup"].Value });

				var onDay = new HashSet<string>(schedule.Roster.Select(x => x.UserId), StringComparer.OrdinalIgnoreCase);
				var selectable = new HashSet<string>((await _departmentService.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>()).Select(x => x.UserId), StringComparer.OrdinalIgnoreCase);
				var members = await _departmentService.GetAllMembersForDepartmentAsync(DepartmentId) ?? new List<DepartmentMember>();

				// Member rows carry the stored user id; the name list upper-cases it.
				model.AddPersonnelOptions = members
					.Where(x => x != null && !x.IsDeleted && !String.IsNullOrWhiteSpace(x.UserId) && selectable.Contains(x.UserId) && !onDay.Contains(x.UserId))
					.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
					.Select(x => new ShiftDayOption { Value = x.First().UserId, Text = lookups.NameFor(x.Key) })
					.OrderBy(x => x.Text)
					.ToList();
			}

			return model;
		}

		private static bool IsSignupTradeLocked(ShiftDaySchedule schedule, int shiftSignupId)
		{
			return schedule.Trades.Any(x => x != null && (x.SourceShiftSignupId == shiftSignupId || x.TargetShiftSignupId == shiftSignupId) &&
			                                (x.IsTradeComplete() || (x.ApprovalPending && !x.Denied)));
		}

		/// <summary>
		/// A signup whose trade took effect, or is waiting for a supervisor to approve one, now belongs to (or is about to
		/// belong to) someone else: deleting it would silently take the day from them.
		/// </summary>
		private static bool IsTradeLocked(ShiftSignup signup)
		{
			return signup?.Trade != null && (signup.Trade.IsTradeComplete() || (signup.Trade.ApprovalPending && !signup.Trade.Denied));
		}

		/// <summary>
		/// Loads a signup for a withdraw or decline and checks it is in the department and the caller may delete it. A
		/// missing signup comes back as (null, null) so the caller just returns to its page.
		/// </summary>
		private async Task<(ShiftSignup Signup, IActionResult Denied)> GetDeletableSignupAsync(int shiftSignupId)
		{
			var signup = await _shiftsService.GetShiftSignupByIdAsync(shiftSignupId);

			if (signup == null)
				return (null, null);

			var shift = await _shiftsService.GetShiftByIdAsync(signup.ShiftId);

			if (shift == null)
				return (null, null);

			if (shift.DepartmentId != DepartmentId)
				return (null, Unauthorized());

			if (!await _authorizationService.CanUserDeleteShiftSignupAsync(UserId, DepartmentId, shiftSignupId))
				return (null, Unauthorized());

			signup.Shift = shift;

			return (signup, null);
		}

		private async Task DeleteSignupUnlessTradedAsync(ShiftSignup signup, CancellationToken cancellationToken)
		{
			if (IsTradeLocked(signup))
			{
				SetMessage(_localizer["SignupTradeLocked"], "danger");
				return;
			}

			await _shiftsService.DeleteShiftSignupAsync(signup, cancellationToken);
			SetMessage(_localizer["SignupWithdrawn"]);
		}

		private async Task<(ShiftDay Day, Shift Shift)> GetShiftDayWithShiftAsync(int shiftDayId)
		{
			var day = await _shiftsService.GetShiftDayByIdAsync(shiftDayId);

			if (day == null)
				return (null, null);

			// The day's multi-mapped Shift can come back null and has no groups; load the full shift for the checks.
			var shift = await _shiftsService.GetShiftByIdAsync(day.ShiftId);

			return (day, shift);
		}

		/// <summary>The trade is in this department and the caller is one of the people it was offered to.</summary>
		private IActionResult CheckTradeParticipant(ShiftSignupTrade trade)
		{
			if (trade?.SourceShiftSignup?.Shift == null || trade.SourceShiftSignup.Shift.DepartmentId != DepartmentId)
				return Unauthorized();

			if (trade.Users == null || !trade.Users.Any(x => x != null && SameUser(x.UserId, UserId)))
				return Unauthorized();

			return null;
		}

		// Finishing a trade is the source signup owner picking which offer to accept. GetShiftTradeByIdAsync loads the
		// source signup and its shift; the fallbacks cover a trade loaded without them.
		private async Task<bool> CanUserFinishTradeAsync(ShiftSignupTrade trade)
		{
			if (trade == null)
				return false;

			var sourceSignup = trade.SourceShiftSignup ?? await _shiftsService.GetShiftSignupByIdAsync(trade.SourceShiftSignupId);

			if (sourceSignup == null || !SameUser(sourceSignup.UserId, UserId))
				return false;

			var sourceShift = sourceSignup.Shift ?? await _shiftsService.GetShiftByIdAsync(sourceSignup.ShiftId);

			return sourceShift != null && sourceShift.DepartmentId == DepartmentId;
		}

		private async Task<ProcessTradeView> BuildProcessTradeViewAsync(ShiftSignupTrade trade)
		{
			var lookups = await GetLookupsAsync(includeScope: false);
			var source = trade.SourceShiftSignup;
			var shift = source.Shift;
			var window = ShiftTimeWindow.GetWindow(source.ShiftDay, shift?.StartTime, shift?.EndTime, shift?.Hours);

			return new ProcessTradeView
			{
				Trade = trade,
				RequesterName = lookups.NameFor(source.UserId),
				GroupName = lookups.GroupNameFor(source.DepartmentGroupId),
				Start = window.Start,
				End = window.End,
				Department = lookups.Department
			};
		}

		private async Task<FinishTradeView> BuildFinishTradeViewAsync(ShiftSignupTrade trade)
		{
			var names = await GetNamesAsync();
			var shiftNames = new Dictionary<int, string>();
			var acceptedUserId = !String.IsNullOrWhiteSpace(trade.UserId) ? trade.UserId : trade.TargetShiftSignup?.UserId;

			var model = new FinishTradeView
			{
				Trade = trade,
				CanPick = !trade.HasSelection() || trade.Denied,
				AcceptedName = acceptedUserId != null ? NameFor(names, acceptedUserId) : null
			};

			foreach (var user in (trade.Users ?? new List<ShiftSignupTradeUser>()).Where(x => x != null))
			{
				var offer = new FinishTradeOffer
				{
					UserId = user.UserId,
					Name = NameFor(names, user.UserId),
					Offered = user.Offered,
					Declined = user.Declined,
					Reason = user.Reason
				};

				foreach (var offered in (user.Shifts ?? new List<ShiftSignupTradeUserShift>()).Where(x => x?.ShiftSignupId != null))
				{
					var signup = await _shiftsService.GetShiftSignupByIdAsync(offered.ShiftSignupId.Value);

					if (signup == null || !signup.IsActive())
						continue;

					if (!shiftNames.TryGetValue(signup.ShiftId, out var shiftName))
					{
						shiftName = (await _shiftsService.GetShiftByIdAsync(signup.ShiftId))?.Name ?? "";
						shiftNames[signup.ShiftId] = shiftName;
					}

					offer.Shifts.Add(new FinishTradeOfferedShift { ShiftSignupId = signup.ShiftSignupId, ShiftName = shiftName, Day = signup.ShiftDay });
				}

				model.Offers.Add(offer);
			}

			return model;
		}

		private async Task<(List<ShiftSignup> Signups, List<ShiftSignupTrade> Trades)> GetPendingApprovalsAsync(ShiftManagementScope scope, List<Shift> shifts)
		{
			// Full shifts (with groups) so a slot with no group can be checked against the whole shift.
			var shiftsById = (shifts ?? new List<Shift>()).Where(x => x != null).GroupBy(x => x.ShiftId).ToDictionary(x => x.Key, x => x.First());
			var signups = new List<ShiftSignup>();
			var trades = new List<ShiftSignupTrade>();

			foreach (var signup in await _shiftsService.GetPendingShiftSignupsAsync(DepartmentId) ?? new List<ShiftSignup>())
			{
				if (signup == null || !shiftsById.TryGetValue(signup.ShiftId, out var shift) || !scope.CanManageShiftGroup(shift, signup.DepartmentGroupId))
					continue;

				signup.Shift = shift;
				signups.Add(signup);
			}

			foreach (var trade in await _shiftsService.GetPendingTradesAsync(DepartmentId) ?? new List<ShiftSignupTrade>())
			{
				var source = trade?.SourceShiftSignup;

				if (source == null || !shiftsById.TryGetValue(source.ShiftId, out var shift) || !scope.CanManageShiftGroup(shift, source.DepartmentGroupId))
					continue;

				source.Shift = shift;
				trades.Add(trade);
			}

			return (signups, trades);
		}

		private IActionResult ReturnAfterReview(int? shiftDayId)
		{
			if (shiftDayId.HasValue && shiftDayId.Value > 0)
				return RedirectToAction("ViewShift", new { shiftDayId = shiftDayId.Value });

			return RedirectToAction("Approvals");
		}

		private ShiftCalendarItemJson ToCalendarItem(ShiftDaySchedule schedule, ShiftLookups lookups)
		{
			var shift = schedule.Shift;
			var day = schedule.Day;

			var item = new ShiftCalendarItemJson
			{
				CalendarItemId = day.ShiftDayId,
				Title = shift.Name,
				Description = shift.Name,
				Color = shift.Color,
				// The day's own window: a night shift ends the next morning.
				Start = DateTime.SpecifyKind(day.Start, DateTimeKind.Unspecified),
				End = DateTime.SpecifyKind(day.End, DateTimeKind.Unspecified),
				IsAllDay = ShiftTimeWindow.TryParseTimeOfDay(shift.StartTime) == null && ShiftTimeWindow.TryParseTimeOfDay(shift.EndTime) == null &&
				           !(shift.Hours > 0),
				ItemType = shift.ShiftId, // The Color in the calendar
				SignupType = shift.AssignmentType,
				ShiftId = shift.ShiftId,
				Filled = schedule.IsFilled(),
				// Pending counts, so the caller is not offered the day again.
				UserSignedUp = schedule.Roster.Any(x => SameUser(x.UserId, UserId))
			};

			foreach (var groupNeeds in schedule.Needs)
			{
				if (!lookups.GroupNames.TryGetValue(groupNeeds.Key, out var groupName))
					continue;

				var group = new ShiftGroupNeeds { ShiftGroupId = groupNeeds.Key, Name = groupName };

				foreach (var roleNeed in groupNeeds.Value)
				{
					if (lookups.RoleNames.TryGetValue(roleNeed.Key, out var roleName))
						group.Needs.Add(new ShiftGroupNeedRole { RoleId = roleNeed.Key, Name = roleName, Needed = Math.Max(roleNeed.Value, 0) });
				}

				item.Groups.Add(group);
			}

			// Who is actually working the day: standing roster, signups and trades, not pending approvals.
			foreach (var entry in schedule.Roster.Where(x => x.IsOnDuty()))
			{
				if (item.Users.Any(x => SameUser(x.UserId, entry.UserId)) || !lookups.Names.TryGetValue(entry.UserId, out var name))
					continue;

				item.Users.Add(new ShiftUser { UserId = entry.UserId, Name = name, IsYouOnShift = SameUser(entry.UserId, UserId) });
			}

			return item;
		}

		private static void GetCalendarRange(string start, string end, DateTime today, out DateTime startDate, out DateTime endDate)
		{
			var hasStart = TryParseCalendarDate(start, out var requestedStart);
			var hasEnd = TryParseCalendarDate(end, out var requestedEnd);

			if (hasStart && hasEnd && requestedEnd >= requestedStart)
			{
				startDate = requestedStart;
				endDate = requestedEnd;
			}
			else if (hasStart)
			{
				startDate = requestedStart;
				endDate = requestedStart.AddDays(CalendarDaysBack + CalendarDaysAhead);
			}
			else
			{
				startDate = today.AddDays(-CalendarDaysBack);
				endDate = today.AddDays(CalendarDaysAhead);
			}

			if ((endDate - startDate).TotalDays > MaxCalendarRangeDays)
				endDate = startDate.AddDays(MaxCalendarRangeDays);
		}

		private static bool TryParseCalendarDate(string value, out DateTime date)
		{
			date = default(DateTime);

			if (String.IsNullOrWhiteSpace(value))
				return false;

			var trimmed = value.Trim();

			// FullCalendar sends ISO 8601 with the browser's offset (2026-09-01T00:00:00-07:00); the date as the browser
			// sees it is the one that matters.
			if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var offset))
			{
				date = offset.DateTime.Date;
				return true;
			}

			// An unencoded '+' in the offset arrives as a space; the date part alone is enough.
			if (trimmed.Length >= 10 && DateTime.TryParseExact(trimmed.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var datePart))
			{
				date = datePart.Date;
				return true;
			}

			return false;
		}

		private static bool TryParseShiftDate(string value, out DateTime date)
		{
			date = default(DateTime);

			if (String.IsNullOrWhiteSpace(value))
				return false;

			if (!DateTime.TryParseExact(value.Trim(), ShiftDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
				return false;

			date = date.Date;
			return true;
		}

		private static ShiftDay FindDay(Shift shift, DateTime date)
		{
			var day = shift?.Days?.FirstOrDefault(x => x != null && x.Day.Date == date.Date);

			if (day != null)
				day.Shift = shift;

			return day;
		}

		/// <summary>
		/// The groups and role requirements of the shift-group editor. Each group row posts groupSelection_{n}, and each
		/// of its roles posts roleSelection_{n}_{suffix} with its count in groupRole_{n}_{suffix}.
		/// </summary>
		private static List<ShiftGroup> ReadShiftGroups(IFormCollection form, ICollection<ShiftGroup> existingGroups)
		{
			var groups = new List<ShiftGroup>();

			foreach (var key in form.Keys.Where(x => x.StartsWith("groupSelection_", StringComparison.OrdinalIgnoreCase)).ToList())
			{
				var index = key.Substring("groupSelection_".Length);

				if (!int.TryParse(form[key], out var departmentGroupId) || departmentGroupId <= 0)
					continue;

				var group = groups.FirstOrDefault(x => x.DepartmentGroupId == departmentGroupId);

				if (group == null)
				{
					group = new ShiftGroup { DepartmentGroupId = departmentGroupId, Roles = new Collection<ShiftGroupRole>() };

					var existing = existingGroups?.FirstOrDefault(x => x != null && x.DepartmentGroupId == departmentGroupId);

					if (existing != null)
						group.ShiftGroupId = existing.ShiftGroupId;

					groups.Add(group);
				}

				var rolePrefix = "roleSelection_" + index + "_";

				foreach (var roleKey in form.Keys.Where(x => x.StartsWith(rolePrefix, StringComparison.OrdinalIgnoreCase)).ToList())
				{
					if (!int.TryParse(form[roleKey], out var roleId) || roleId <= 0)
						continue;

					// Each role's count sits beside it under the same suffix. Reading the first count on the group gave
					// every role in it the same number.
					var suffix = roleKey.Substring(rolePrefix.Length);
					var count = int.TryParse(form["groupRole_" + index + "_" + suffix], out var parsed) ? parsed : 1;

					if (count <= 0)
						continue;

					var role = group.Roles.FirstOrDefault(x => x.PersonnelRoleId == roleId);

					if (role != null)
						role.Required += count;
					else
						group.Roles.Add(new ShiftGroupRole { PersonnelRoleId = roleId, Required = count });
				}
			}

			return groups;
		}

		/// <summary>
		/// The personnel pickers: shiftPersonnel holds people with no group, groupPersonnel_{departmentGroupId} the people
		/// for that group. Someone picked twice keeps their first placement.
		/// </summary>
		private static List<ShiftPerson> ReadPersonnel(IFormCollection form)
		{
			var personnel = new List<ShiftPerson>();

			foreach (var userId in SplitValues(form["shiftPersonnel"]))
			{
				if (!personnel.Any(x => SameUser(x.UserId, userId)))
					personnel.Add(new ShiftPerson { UserId = userId });
			}

			foreach (var key in form.Keys.Where(x => x.StartsWith("groupPersonnel_", StringComparison.OrdinalIgnoreCase)).ToList())
			{
				if (!int.TryParse(key.Substring("groupPersonnel_".Length), out var groupId) || groupId <= 0)
					continue;

				foreach (var userId in SplitValues(form[key]))
				{
					if (!personnel.Any(x => SameUser(x.UserId, userId)))
						personnel.Add(new ShiftPerson { UserId = userId, GroupId = groupId });
				}
			}

			return personnel;
		}

		private static List<string> SplitValues(StringValues values)
		{
			return values
				.SelectMany(x => (x ?? "").Split(','))
				.Select(x => x.Trim())
				.Where(x => x.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		private async Task SetNewShiftScopeAsync(NewShiftView model, ShiftManagementScope scope)
		{
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();

			model.CanManageAllGroups = scope.AllGroups;
			model.ManageableGroupIds = scope.AllGroups ? new List<int>() : scope.GroupIds.ToList();
			model.Groups = scope.AllGroups ? groups : groups.Where(x => x != null && scope.GroupIds.Contains(x.DepartmentGroupId)).ToList();
		}

		private async Task SetEditShiftScopeAsync(EditShiftView model, ShiftManagementScope scope)
		{
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();

			SetScope(model, scope);
			model.Groups = scope.AllGroups ? groups : groups.Where(x => x != null && scope.GroupIds.Contains(x.DepartmentGroupId)).ToList();
		}

		private static void SetScope(EditShiftView model, ShiftManagementScope scope)
		{
			model.CanManageAllGroups = scope.AllGroups;
			model.ManageableGroupIds = scope.AllGroups ? new List<int>() : scope.GroupIds.ToList();
		}

		private string ErrorText(ShiftActionErrors error)
		{
			return _localizer["ShiftError" + error].Value;
		}

		private void SetError(ShiftActionErrors error)
		{
			SetMessage(ErrorText(error), "danger");
		}

		private void SetMessage(string message, string type = "success")
		{
			TempData[MessageKey] = message;
			TempData[MessageTypeKey] = type;
		}

		private static bool SameUser(string a, string b)
		{
			return a != null && b != null && String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
		}

		#endregion Helpers
	}
}
