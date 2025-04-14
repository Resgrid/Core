using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Web.Areas.User.Models.Calendar;
using Resgrid.Web.Areas.User.Models.Shifts;
using Resgrid.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Model.Providers;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class ShiftsController : SecureBaseController
	{
		private readonly IShiftsService _shiftsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IDepartmentsService _departmentService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IUserProfileService _userProfileService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IUnitsService _unitsService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly IWorkShiftsService _workshiftsService;

		public ShiftsController(IShiftsService shiftsService, IDepartmentGroupsService departmentGroupsService, IDepartmentsService departmentService,
			IPersonnelRolesService personnelRolesService, IUserProfileService userProfileService, IEventAggregator eventAggregator,
			IDepartmentSettingsService departmentSettingsService, IUnitsService unitsService, Model.Services.IAuthorizationService authorizationService,
			IWorkShiftsService workshiftsService)
		{
			_shiftsService = shiftsService;
			_departmentGroupsService = departmentGroupsService;
			_departmentService = departmentService;
			_personnelRolesService = personnelRolesService;
			_userProfileService = userProfileService;
			_eventAggregator = eventAggregator;
			_departmentSettingsService = departmentSettingsService;
			_unitsService = unitsService;
			_authorizationService = authorizationService;
			_workshiftsService = workshiftsService;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> Index()
		{
			var model = new ShiftsIndexModel();
			model.Shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId);

			var department = await _departmentService.GetDepartmentByIdAsync(DepartmentId);

			if (department.IsUserAnAdmin(UserId))
				model.IsUserAdminOrGroupAdmin = true;
			else
			{
				var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

				foreach (var group in groups)
				{
					if (group.IsUserGroupAdmin(UserId))
					{
						model.IsUserAdminOrGroupAdmin = true;
						break;
					}
				}
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Create)]
		public async Task<IActionResult> NewShift()
		{
			var model = new NewShiftView();
			model.Shift = new Shift();
			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			ViewBag.ShiftAssignmentTypes = model.AssignmentType.ToSelectList();

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public async Task<IActionResult> EditShiftDetails(int shiftId)
		{
			var model = new EditShiftView();

			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);

			if (shift.DepartmentId != DepartmentId)
				Unauthorized();

			shift = await _shiftsService.PopulateShiftData(shift, true, true, true, true, true);

			model.Shift = shift;
			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public async Task<IActionResult> EditShiftDetails(EditShiftView model, IFormCollection form, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				var shift = await _shiftsService.GetShiftByIdAsync(model.Shift.ShiftId);
				shift.Name = model.Shift.Name;
				shift.Code = model.Shift.Code;
				shift.Color = model.Shift.Color;
				shift.StartTime = model.Shift.StartTime;
				shift.EndTime = model.Shift.EndTime;

				ViewBag.ShiftAssignmentTypes = model.AssignmentType.ToSelectList();

				shift = await _shiftsService.SaveShiftAsync(shift, cancellationToken);
				var personnel = new List<ShiftPerson>();
				if (model.AssignmentType == ShiftAssignmentTypes.Assigned)
				{
					if (!string.IsNullOrWhiteSpace(form["shiftPersonnel"]))
					{
						var personnelIds = form["shiftPersonnel"].ToString().Split(char.Parse(","));

						foreach (var personnelId in personnelIds)
						{
							var assignment = new ShiftPerson();
							assignment.UserId = personnelId;
							personnel.Add(assignment);
						}
					}

					List<int> shiftFormGroupPersonnel = (from object key in form.Keys where key.ToString().StartsWith("groupPersonnel_") select int.Parse(key.ToString().Replace("groupPersonnel_", ""))).ToList();
					foreach (var i in shiftFormGroupPersonnel)
					{
						if (form.ContainsKey("groupPersonnel_" + i))
						{
							var personnelIds = form["groupPersonnel_" + i].ToString().Split(char.Parse(","));

							foreach (var personnelId in personnelIds)
							{
								var assignment = new ShiftPerson();
								assignment.UserId = personnelId;
								assignment.GroupId = i;

								if (personnel.All(x => x.UserId != assignment.UserId))
									personnel.Add(assignment);
							}

						}
					}
				}

				await _shiftsService.UpdateShiftPersonnel(shift, personnel, cancellationToken);

				var number = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
				_eventAggregator.SendMessage<ShiftUpdatedEvent>(new ShiftUpdatedEvent() { DepartmentId = DepartmentId, DepartmentNumber = number, Item = shift });

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_Create)]
		public async Task<IActionResult> NewShift(NewShiftView model, IFormCollection form, CancellationToken cancellationToken)
		{
			model.Shift.AssignmentType = (int)model.AssignmentType;
			//model.Shift.AssignmentType = (int)ShiftAssignmentTypes.Assigned;
			model.Shift.DepartmentId = DepartmentId;
			model.Shift.ScheduleType = (int)ShiftScheduleTypes.Manual;
			model.Shift.Name = form["Shift_Name"];
			model.Shift.Code = form["Shift_Code"];
			model.Shift.StartTime = form["Shift_StartTime"];
			model.Shift.EndTime = form["Shift_EndTime"];
			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			if (ModelState.IsValid)
			{
				List<int> shiftFormGroups = (from object key in form.Keys where key.ToString().StartsWith("groupSelection_") select int.Parse(key.ToString().Replace("groupSelection_", ""))).ToList();
				//var shiftGroups = new List<Tuple<ShiftGroup, List<ShiftGroupRole>>>();

				if (shiftFormGroups.Count > 0)
					model.Shift.Groups = new Collection<ShiftGroup>();

				foreach (var i in shiftFormGroups)
				{
					if (form.ContainsKey("groupSelection_" + i))
					{
						var groupId = form["groupSelection_" + i];
						var group = new ShiftGroup();

						group.DepartmentGroupId = int.Parse(groupId);

						var values = new Dictionary<int, int>();
						//var roles = form["groupSelection_" + i].Split(char.Parse(","));
						List<int> roleSelections = (from object key in form.Keys where key.ToString().StartsWith("roleSelection_" + i + "_") select int.Parse(key.ToString().Replace("roleSelection_" + i + "_", ""))).ToList();

						foreach (var groupRole in roleSelections)
						{
							var roleId = form["roleSelection_" + i + "_" + groupRole];
							int roleCountId = (from object key in form.Keys where key.ToString().StartsWith("groupRole_" + i + "_") select int.Parse(key.ToString().Replace("groupRole_" + i + "_", ""))).FirstOrDefault();
							var roleCount = form["groupRole_" + i + "_" + roleCountId];

							if (!string.IsNullOrWhiteSpace(roleId))
							{
								if (values.ContainsKey(int.Parse(roleId)))
									values[int.Parse(roleId)] += int.Parse(roleCount);
								else
									values.Add(int.Parse(roleId), int.Parse(roleCount));
							}
						}

						//var roles = new List<ShiftGroupRole>();
						group.Roles = new Collection<ShiftGroupRole>();
						foreach (var v in values)
						{
							var role = new ShiftGroupRole();
							role.Required = v.Value;
							role.PersonnelRoleId = v.Key;

							//roles.Add(role);
							group.Roles.Add(role);
						}

						//shiftGroups.Add(new Tuple<ShiftGroup, List<ShiftGroupRole>>(group, roles));
						model.Shift.Groups.Add(group);
					}
				}

				if (!String.IsNullOrWhiteSpace(model.Dates))
				{
					model.Shift.Days = new Collection<ShiftDay>();
					var dates = model.Dates.Split(char.Parse(","));

					foreach (var date in dates)
					{
						var shiftDate = DateTimeHelpers.ConvertKendoCalDate(date);

						if (model.Shift.Days.Count == 0)
							model.Shift.StartDay = shiftDate;

						var day = new ShiftDay();
						day.Day = shiftDate;

						model.Shift.Days.Add(day);
					}
				}
				else
				{
					model.Shift.StartDay = DateTime.UtcNow.TimeConverter(await _departmentService.GetDepartmentByIdAsync(DepartmentId, false));
				}

				if (model.AssignmentType == ShiftAssignmentTypes.Assigned)
				{
					model.Shift.Personnel = new Collection<ShiftPerson>();

					if (!string.IsNullOrWhiteSpace(form["shiftPersonnel"]))
					{
						var personnelIds = form["shiftPersonnel"].ToString().Split(char.Parse(","));

						foreach (var personnelId in personnelIds)
						{
							var assignment = new ShiftPerson();
							assignment.UserId = personnelId;
							model.Shift.Personnel.Add(assignment);
						}
					}

					List<int> shiftFormGroupPersonnel = (from object key in form.Keys where key.ToString().StartsWith("groupPersonnel_") select int.Parse(key.ToString().Replace("groupPersonnel_", ""))).ToList();
					foreach (var i in shiftFormGroupPersonnel)
					{
						if (form.ContainsKey("groupPersonnel_" + i))
						{
							var personnelIds = form["groupPersonnel_" + i].ToString().Split(char.Parse(","));

							foreach (var personnelId in personnelIds)
							{
								var assignment = new ShiftPerson();
								assignment.UserId = personnelId;
								assignment.GroupId = i;

								if (model.Shift.Personnel.All(x => x.UserId != assignment.UserId))
									model.Shift.Personnel.Add(assignment);
							}

						}
					}
				}

				//_shiftsService.CreateNewShift(model.Shift, shiftGroups, shiftDays, assignments);
				var newShift = await _shiftsService.SaveShiftAsync(model.Shift, cancellationToken);

				var number = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
				_eventAggregator.SendMessage<ShiftCreatedEvent>(new ShiftCreatedEvent() { DepartmentId = DepartmentId, DepartmentNumber = number, Item = newShift });

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> RequestTrade(int shiftSignUpId)
		{
			var model = new RequestTradeView();
			model.Signup = await _shiftsService.GetShiftSignupByIdAsync(shiftSignUpId);
			model.ShiftDay = await _shiftsService.GetShiftDayForSignupAsync(shiftSignUpId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ShiftCalendar(int shiftId)
		{
			var model = new ShiftCalendarView();
			model.Shift = await _shiftsService.GetShiftByIdAsync(shiftId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> RequestTrade(RequestTradeView model, IFormCollection form, CancellationToken cancellationToken)
		{
			string[] users = null;

			if (form.ContainsKey("users"))
				users = form["users"].ToString().Split(char.Parse(","));

			if (users == null || !users.Any())
				ModelState.AddModelError("users", "You must specify users to request a trade from. Only qualified users will populate the list.");

			model.Signup = await _shiftsService.GetShiftSignupByIdAsync(model.Signup.ShiftSignupId);
			model.ShiftDay = await _shiftsService.GetShiftDayForSignupAsync(model.Signup.ShiftSignupId);

			if (ModelState.IsValid && users != null)
			{
				var trade = new ShiftSignupTrade();
				trade.Users = new List<ShiftSignupTradeUser>();
				trade.SourceShiftSignupId = model.Signup.ShiftSignupId;

				foreach (var user in users)
				{
					trade.Users.Add(new ShiftSignupTradeUser() { UserId = user });
				}

				trade = await _shiftsService.SaveTradeAsync(trade, cancellationToken);

				var tradeRequestedEvent = new ShiftTradeRequestedEvent();
				tradeRequestedEvent.DepartmentId = DepartmentId;
				tradeRequestedEvent.ShiftSignupTradeId = trade.ShiftSignupTradeId;

				var number = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
				tradeRequestedEvent.DepartmentNumber = number;

				_eventAggregator.SendMessage<ShiftTradeRequestedEvent>(tradeRequestedEvent);

				return RedirectToAction("YourShifts");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public async Task<IActionResult> EditShiftDays(int shiftId)
		{
			var model = new EditShiftView();

			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);

			if (shift.DepartmentId != DepartmentId)
				Unauthorized();

			model.Shift = shift;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public async Task<IActionResult> EditShiftDays(EditShiftView model, CancellationToken cancellationToken)
		{
			var shift = await _shiftsService.GetShiftByIdAsync(model.Shift.ShiftId);

			if (shift.DepartmentId != DepartmentId)
				Unauthorized();

			var days = new List<ShiftDay>();
			if (!String.IsNullOrWhiteSpace(model.Dates))
			{
				model.Shift.Days = new Collection<ShiftDay>();
				var dates = model.Dates.Split(char.Parse(","));

				for (int i = 0; i < dates.Length; i++)
				{
					var date = DateTimeHelpers.ConvertKendoCalDate(dates[i]);

					if (shift.Days.Count == 0 && i == 0)
						model.Shift.StartDay = date;

					var day = new ShiftDay();
					day.Day = date;

					days.Add(day);
				}
			}
			else
			{
				model.Shift.StartDay = DateTime.UtcNow.TimeConverter(await _departmentService.GetDepartmentByIdAsync(DepartmentId, false));
			}

			await _shiftsService.UpdateShiftDatesAsync(shift, days, cancellationToken);

			var number = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
			_eventAggregator.SendMessage<ShiftDaysAddedEvent>(new ShiftDaysAddedEvent() { DepartmentId = DepartmentId, DepartmentNumber = number, Item = shift });

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public async Task<IActionResult> EditShiftGroups(int shiftId)
		{
			var model = new EditShiftView();

			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);

			if (shift.DepartmentId != DepartmentId)
				Unauthorized();

			shift = await _shiftsService.PopulateShiftData(shift, true, true, true, true, true);

			model.Shift = shift;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public async Task<IActionResult> EditShiftGroups(EditShiftView model, IFormCollection form, CancellationToken cancellationToken)
		{
			var shift = await _shiftsService.GetShiftByIdAsync(model.Shift.ShiftId);

			if (shift.DepartmentId != DepartmentId)
				Unauthorized();

			model.Shift = shift;
			var shiftGroups = await _shiftsService.GetShiftGroupsByGroupIdAsync(model.Shift.ShiftId);

			if (ModelState.IsValid)
			{
				List<int> shiftFormGroups = (from object key in form.Keys where key.ToString().StartsWith("groupSelection_") select int.Parse(key.ToString().Replace("groupSelection_", ""))).ToList();
				var groups = new List<ShiftGroup>();

				foreach (var i in shiftFormGroups)
				{
					if (form.ContainsKey("groupSelection_" + i))
					{
						var groupId = form["groupSelection_" + i];
						var group = new ShiftGroup();

						group.DepartmentGroupId = int.Parse(groupId);

						if (shiftGroups != null && shiftGroups.Any())
						{
							var shiftGroup = shiftGroups.FirstOrDefault(x => x.DepartmentGroupId == group.DepartmentGroupId);

							if (shiftGroup != null)
								group.ShiftGroupId = shiftGroup.ShiftGroupId;
						}

						var values = new Dictionary<int, int>();
						List<int> roleSelections = (from object key in form.Keys where key.ToString().StartsWith("roleSelection_" + i + "_") select int.Parse(key.ToString().Replace("roleSelection_" + i + "_", ""))).ToList();

						foreach (var groupRole in roleSelections)
						{
							var roleId = form["roleSelection_" + i + "_" + groupRole];
							//int roleCountId = (from object key in form.Keys where key.ToString().StartsWith("groupRole_" + i + "_") select int.Parse(key.ToString().Replace("groupRole_" + i + "_", ""))).FirstOrDefault();
							var roleCount = form["groupRole_" + i + "_" + groupRole];

							if (!string.IsNullOrWhiteSpace(roleId))
							{
								if (values.ContainsKey(int.Parse(roleId)))
									values[int.Parse(roleId)] += int.Parse(roleCount);
								else
									values.Add(int.Parse(roleId), int.Parse(roleCount));
							}
						}

						group.Roles = new Collection<ShiftGroupRole>();
						foreach (var v in values)
						{
							var role = new ShiftGroupRole();
							role.Required = v.Value;
							role.PersonnelRoleId = v.Key;

							//roles.Add(role);
							group.Roles.Add(role);
						}

						groups.Add(group);
					}
				}

				await _shiftsService.UpdateShiftGroupsAsync(model.Shift, groups, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Delete)]
		public async Task<IActionResult> DeleteShift(int shiftId, CancellationToken cancellationToken)
		{
			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);

			if (shift.DepartmentId != DepartmentId)
				Unauthorized();

			await _shiftsService.DeleteShift(shift, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> Signup(int shiftDayId)
		{
			var model = new ShiftSignupView();
			model.Day = await _shiftsService.GetShiftDayByIdAsync(shiftDayId);

			if (model.Day.Shift.DepartmentId != DepartmentId)
				Unauthorized();

			model.Day.Shift = await _shiftsService.GetShiftByIdAsync(model.Day.ShiftId);
			model.Roles = await _personnelRolesService.GetRolesForUserAsync(UserId, DepartmentId);
			model.Needs = await _shiftsService.GetShiftDayNeedsAsync(shiftDayId);
			model.Signups = await _shiftsService.GetShiftSignpsForShiftDayAsync(shiftDayId);

			var allowMulitpleSignups = await _departmentSettingsService.GetAllowSignupsForMultipleShiftGroupsAsync(DepartmentId);
			model.UserSignedUp = await _shiftsService.IsUserSignedUpForShiftDayAsync(model.Day, UserId, null);

			if (model.UserSignedUp)
			{
				if (allowMulitpleSignups)
					model.UserSignedUp = false;
			}

			foreach (var shiftGroup in model.Day.Shift.Groups)
			{
				model.ShiftGroupSignups.Add(shiftGroup.DepartmentGroupId, await _shiftsService.IsUserSignedUpForShiftDayAsync(model.Day, UserId, shiftGroup.DepartmentGroupId));
			}

			model.PersonnelRoles = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(DepartmentId);
			model.UserProfiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ViewShift(int shiftDayId)
		{
			var model = new ShiftSignupView();
			model.Day = await _shiftsService.GetShiftDayByIdAsync(shiftDayId);

			if (model.Day.Shift.DepartmentId != DepartmentId)
				Unauthorized();

			model.Day.Shift = await _shiftsService.GetShiftByIdAsync(model.Day.ShiftId); //await _shiftsService.PopulateShiftData(model.Day.Shift, true, true, true, true, true);

			model.Roles = await _personnelRolesService.GetRolesForUserAsync(UserId, DepartmentId);
			model.Needs = await _shiftsService.GetShiftDayNeedsAsync(shiftDayId);
			model.Signups = await _shiftsService.GetShiftSignpsForShiftDayAsync(shiftDayId);
			model.UserSignedUp = await _shiftsService.IsUserSignedUpForShiftDayAsync(model.Day, UserId, null);
			model.PersonnelRoles = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(DepartmentId);
			model.UserProfiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ShiftDaySignup(int shiftDayId, int groupId, CancellationToken cancellationToken)
		{
			var day = await _shiftsService.GetShiftDayByIdAsync(shiftDayId);

			if (day.Shift.DepartmentId != DepartmentId)
				Unauthorized();

			var signup = await _shiftsService.SignupForShiftDayAsync(day.ShiftId, day.Day, groupId, UserId, cancellationToken);

			return RedirectToAction("SignupSuccess", new { shiftSignupId = signup.ShiftSignupId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> DeleteShiftDaySignup(int shiftSignupId, int shiftDayId, CancellationToken cancellationToken)
		{
			var signup = await _shiftsService.GetShiftSignupByIdAsync(shiftSignupId);

			if (signup == null)
				return RedirectToAction("Signup", new { shiftDayId = shiftDayId });

			var shift = await _shiftsService.GetShiftByIdAsync(signup.ShiftId);

			if (shift == null)
				return RedirectToAction("Signup", new { shiftDayId = shiftDayId });

			if (shift.DepartmentId != DepartmentId)
				Unauthorized();

			if (!(await _authorizationService.CanUserDeleteShiftSignupAsync(UserId, DepartmentId, shiftSignupId)))
				Unauthorized();

			await _shiftsService.DeleteShiftSignupAsync(signup, cancellationToken);

			return RedirectToAction("Signup", new { shiftDayId = shiftDayId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> SignupSuccess(int shiftSignupId)
		{
			var model = new ShiftSignupView();
			model.Signup = await _shiftsService.GetShiftSignupByIdAsync(shiftSignupId);
			model.Signup.Shift = await _shiftsService.GetShiftByIdAsync(model.Signup.ShiftId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> YourShifts()
		{
			var model = new YourShiftsView();
			model.Signups = await _shiftsService.GetShiftSignupsForUserAsync(UserId);
			model.Department = await _departmentService.GetDepartmentByIdAsync(DepartmentId, false);
			model.Trades = await _shiftsService.GetOpenTradeRequestsForUserAsync(UserId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> DeclineShiftDay(int shiftSignupId, CancellationToken cancellationToken)
		{
			var signup = await _shiftsService.GetShiftSignupByIdAsync(shiftSignupId);

			if (signup.Shift.DepartmentId != DepartmentId)
				Unauthorized();

			await _shiftsService.DeleteShiftSignupAsync(signup, cancellationToken);

			return RedirectToAction("YourShifts");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ProcessTrade(int shiftSignupTradeId)
		{
			var model = new ProcessTradeView();
			model.Trade = await _shiftsService.GetShiftTradeByIdAsync(shiftSignupTradeId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ProcessTrade(ProcessTradeView model, IFormCollection form, CancellationToken cancellationToken)
		{
			string[] dates = null;
			string note = null;

			if (form.ContainsKey("dates"))
				dates = form["dates"].ToString().Split(char.Parse(","));

			if (form.ContainsKey("note"))
				note = form["note"];

			var tradeRequest = await _shiftsService.GetShiftTradeByIdAsync(model.Trade.ShiftSignupTradeId);

			if (dates != null && dates.Any())
				await _shiftsService.ProposeShiftDaysForTradeAsync(tradeRequest.ShiftSignupTradeId, UserId, note, dates.Select(x => int.Parse(x)).ToList(), cancellationToken);
			else
				await _shiftsService.ProposeShiftDaysForTradeAsync(tradeRequest.ShiftSignupTradeId, UserId, note, null, cancellationToken);

			var shiftTradeProposed = new ShiftTradeProposedEvent();
			shiftTradeProposed.DepartmentId = DepartmentId;
			shiftTradeProposed.ShiftSignupTradeId = model.Trade.ShiftSignupTradeId;
			shiftTradeProposed.UserId = UserId;

			var number = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
			shiftTradeProposed.DepartmentNumber = number;

			_eventAggregator.SendMessage<ShiftTradeProposedEvent>(shiftTradeProposed);

			return RedirectToAction("YourShifts");


			model.Trade = tradeRequest;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> RejectTrade(int shiftTradeId, string reason, CancellationToken cancellationToken)
		{
			var decodedReason = HttpUtility.UrlDecode(reason);

			await _shiftsService.RejectTradeRequestAsync(shiftTradeId, UserId, decodedReason, cancellationToken);

			var shiftTradeRejected = new ShiftTradeRejectedEvent();
			shiftTradeRejected.DepartmentId = DepartmentId;
			shiftTradeRejected.ShiftSignupTradeId = shiftTradeId;
			shiftTradeRejected.UserId = UserId;

			var number = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
			shiftTradeRejected.DepartmentNumber = number;

			_eventAggregator.SendMessage<ShiftTradeRejectedEvent>(shiftTradeRejected);

			return RedirectToAction("YourShifts");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> FinishTrade(int shiftSignupTradeId)
		{
			var model = new FinishTradeView();
			model.Trade = await _shiftsService.GetShiftTradeByIdAsync(shiftSignupTradeId);
			model.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(model.Trade.Users.Select(x => x.UserId).ToList());

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> FinishTrade(FinishTradeView model, IFormCollection form, CancellationToken cancellationToken)
		{
			string selectedShift = null;
			if (form.ContainsKey("selectedShift"))
				selectedShift = form["selectedShift"];

			var tradeRequest = await _shiftsService.GetShiftTradeByIdAsync(model.Trade.ShiftSignupTradeId);

			if (selectedShift != null)
			{
				Guid userId;

				if (Guid.TryParse(selectedShift, out userId))
				{
					tradeRequest.UserId = userId.ToString();
				}
				else
				{
					tradeRequest.TargetShiftSignupId = int.Parse(selectedShift);
					var shiftSignup = await _shiftsService.GetShiftSignupByIdAsync(tradeRequest.TargetShiftSignupId.Value);
					userId = Guid.Parse(shiftSignup.UserId);
				}

				var shiftTradeFilled = new ShiftTradeFilledEvent();
				shiftTradeFilled.DepartmentId = DepartmentId;
				shiftTradeFilled.ShiftSignupTradeId = model.Trade.ShiftSignupTradeId;
				shiftTradeFilled.UserId = userId.ToString();

				var number = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);
				shiftTradeFilled.DepartmentNumber = number;

				await _shiftsService.SaveTradeAsync(tradeRequest, cancellationToken);

				_eventAggregator.SendMessage<ShiftTradeFilledEvent>(shiftTradeFilled);

				return RedirectToAction("YourShifts");
			}

			model.Trade = tradeRequest;
			model.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(model.Trade.Users.Select(x => x.UserId).ToList());
			model.Message = "You must select a shift to trade for or accept an unbalanced trade";

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ShiftStaffing()
		{
			var model = new ShiftStaffingView();

			await SetShiftStaffingModel(model);

			return View(model);
		}

		private async Task<ShiftStaffingView> SetShiftStaffingModel(ShiftStaffingView model)
		{
			model.Shifts = new List<Shift>();
			model.CurrentUnitRoles = new Dictionary<int, List<UnitStateRole>>();

			var department = await _departmentService.GetDepartmentByIdAsync(DepartmentId);
			var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
			var shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);

			if (department.IsUserAnAdmin(UserId))
			{
				model.IsDepartmentAdmin = true;
				model.Shifts = shifts;
			}
			else
			{
				model.GroupId = group.DepartmentGroupId;

				foreach (var shift in shifts)
				{
					if (shift.Groups.Any(x => x.DepartmentGroupId == group.DepartmentGroupId))
						model.Shifts.Add(shift);
				}
			}

			foreach (var unit in units)
			{
				model.CurrentUnitRoles.Add(unit.UnitId, await _unitsService.GetCurrentRolesForUnitAsync(unit.UnitId));
			}

			return model;
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ShiftStaffing(ShiftStaffingView model, IFormCollection form, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(form["shiftDayPicker"]))
			{
				await SetShiftStaffingModel(model);
				ModelState.AddModelError("shiftDayPicker", "You must select a valid shift day");
			}

			if (ModelState.IsValid)
			{
				var staffing = new ShiftStaffing();
				staffing.Personnel = new Collection<ShiftStaffingPerson>();

				staffing.ShiftId = model.ShiftId;
				staffing.DepartmentId = DepartmentId;
				staffing.Note = model.Note;
				staffing.AddedByUserId = UserId;
				staffing.AddedOn = DateTime.UtcNow;
				staffing.ShiftDay = DateTime.Parse(form["shiftDayPicker"]);

				if (!string.IsNullOrWhiteSpace(form["shiftPersonnel"]))
				{
					var personnelIds = form["shiftPersonnel"].ToString().Split(char.Parse(","));

					foreach (var personnelId in personnelIds)
					{
						var assignment = new ShiftStaffingPerson();
						assignment.UserId = personnelId;
						staffing.Personnel.Add(assignment);
					}
				}

				List<int> shiftFormGroupPersonnel = (from object key in form.Keys where key.ToString().StartsWith("groupPersonnel_") select int.Parse(key.ToString().Replace("groupPersonnel_", ""))).ToList();
				foreach (var i in shiftFormGroupPersonnel)
				{
					if (form.ContainsKey("groupPersonnel_" + i))
					{
						var personnelIds = form["groupPersonnel_" + i].ToString().Split(char.Parse(","));

						foreach (var personnelId in personnelIds)
						{
							var assignment = new ShiftStaffingPerson();
							assignment.UserId = personnelId;
							assignment.GroupId = i;

							if (staffing.Personnel.All(x => x.UserId != assignment.UserId))
								staffing.Personnel.Add(assignment);
						}

					}
				}

				//List<string> unitRoles = (from object key in form.Keys where key.ToString().StartsWith("unitRole_") select key.ToString()).ToList();
				//foreach (var i in unitRoles)
				//{
				//	var iTrimmed = i.Replace("unitRole_", "");
				//	var middleUnderscrore = iTrimmed.IndexOf(char.Parse("_"));
				//	var unitId = int.Parse(iTrimmed.Substring(0, middleUnderscrore));
				//	var roleId = int.Parse(iTrimmed.Substring(middleUnderscrore, iTrimmed.Length));

				//	if (form.ContainsKey(i))
				//	{
				//		var personId = form[i].ToString();



				//	}
				//}

				var savedStaffing = await _shiftsService.SaveShiftStaffingAsync(staffing, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}


		#region Async Calls
		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetShiftCalendarItems()
		{
			var calendarItems = new List<ShiftCalendarItemJson>();
			var shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId);
			var allGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var allRoles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);
			var allUserNames = await _departmentService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			foreach (var shift in shifts)
			{
				if (shift.Days != null && shift.Days.Any())
				{
					foreach (var day in shift.Days)
					{
						day.Shift = shift;

						var item = new ShiftCalendarItemJson();
						item.CalendarItemId = day.ShiftDayId;

						if (String.IsNullOrWhiteSpace(shift.EndTime))
							item.IsAllDay = true;
						else
							item.IsAllDay = false;

						DateTime startResult;
						if (!String.IsNullOrWhiteSpace(shift.StartTime) && DateTime.TryParse(shift.StartTime, out startResult))
						{
							item.Start = new DateTime(day.Day.Year, day.Day.Month, day.Day.Day, startResult.Hour, startResult.Minute, 0, DateTimeKind.Local);
						}
						else
						{
							item.Start = new DateTime(day.Day.Year, day.Day.Month, day.Day.Day, 0, 0, 0, DateTimeKind.Local);
						}

						DateTime endResult;
						if (!String.IsNullOrWhiteSpace(shift.EndTime) && DateTime.TryParse(shift.EndTime, out endResult))
						{
							item.End = new DateTime(day.Day.Year, day.Day.Month, day.Day.Day, endResult.Hour, endResult.Minute, 0, DateTimeKind.Local);
						}
						else
						{
							item.End = new DateTime(day.Day.Year, day.Day.Month, day.Day.Day, 23, 59, 59, DateTimeKind.Local);
						}

						item.Color = shift.Color;
						item.Title = shift.Name;
						item.Description = shift.Name;
						item.ItemType = day.ShiftId; // The Color in the calendar
						item.SignupType = shift.AssignmentType;
						item.ShiftId = day.ShiftId;
						item.Filled = await _shiftsService.IsShiftDayFilledWithObjAsync(shift, day);
						item.UserSignedUp = await _shiftsService.IsUserSignedUpForShiftDayAsync(day, UserId, null);

						var shiftGroups = await _shiftsService.GetShiftDayNeedsObjAsync(shift, day);

						if (shiftGroups != null)
						{
							foreach (var shiftGroup in shiftGroups)
							{
								var group = new ShiftGroupNeeds();

								var departmentGroup = allGroups.FirstOrDefault(x => x.DepartmentGroupId == shiftGroup.Key);
								if (departmentGroup != null)
								{
									group.ShiftGroupId = departmentGroup.DepartmentGroupId;
									group.Name = departmentGroup.Name;

									foreach (var shiftGroupNeed in shiftGroup.Value)
									{
										var role = allRoles.FirstOrDefault(x => x.PersonnelRoleId == shiftGroupNeed.Key);

										if (role != null)
										{
											var shiftGroupNeedRole = new ShiftGroupNeedRole();
											shiftGroupNeedRole.RoleId = shiftGroupNeed.Key;
											shiftGroupNeedRole.Name = role.Name;
											shiftGroupNeedRole.Needed = shiftGroupNeed.Value;

											group.Needs.Add(shiftGroupNeedRole);
										}
									}

									item.Groups.Add(group);
								}

							}
						}

						if (shift.Personnel != null)
						{
							foreach (var person in shift.Personnel)
							{
								var name = allUserNames.FirstOrDefault(x => x.UserId == person.UserId);
								if (name != null)
								{
									var shiftUser = new ShiftUser();
									shiftUser.UserId = person.UserId;
									shiftUser.Name = name.Name;

									if (person.UserId == UserId)
										shiftUser.IsYouOnShift = true;
									else
										shiftUser.IsYouOnShift = false;

									item.Users.Add(shiftUser);
								}
							}
						}

						calendarItems.Add(item);
					}
				}
			}

			var workshifts = await _workshiftsService.GetAllWorkshiftsByDepartmentAsync(DepartmentId);

			if (workshifts != null && workshifts.Any())
			{
				var department = await _departmentService.GetDepartmentByIdAsync(DepartmentId);

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
		public async Task<IActionResult> GetShiftCalendarItemsForShift(int shiftId)
		{
			var calendarItems = new List<ShiftCalendarItemJson>();
			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);
			var allGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var allRoles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);
			var allUserNames = await _departmentService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			foreach (var day in shift.Days)
			{
				var item = new ShiftCalendarItemJson();
				item.CalendarItemId = day.ShiftDayId;
				day.Shift = shift;

				if (String.IsNullOrWhiteSpace(shift.EndTime))
					item.IsAllDay = true;
				else
					item.IsAllDay = false;

				DateTime startResult;
				if (!String.IsNullOrWhiteSpace(shift.StartTime) && DateTime.TryParse(shift.StartTime, out startResult))
				{
					item.Start = new DateTime(day.Day.Year, day.Day.Month, day.Day.Day, startResult.Hour, startResult.Minute, 0, DateTimeKind.Local);
				}
				else
				{
					item.Start = new DateTime(day.Day.Year, day.Day.Month, day.Day.Day, 0, 0, 0, DateTimeKind.Local);
				}

				DateTime endResult;
				if (!String.IsNullOrWhiteSpace(shift.EndTime) && DateTime.TryParse(shift.EndTime, out endResult))
				{
					item.End = new DateTime(day.Day.Year, day.Day.Month, day.Day.Day, endResult.Hour, endResult.Minute, 0, DateTimeKind.Local);
				}
				else
				{
					item.End = new DateTime(day.Day.Year, day.Day.Month, day.Day.Day, 23, 59, 59, DateTimeKind.Local);
				}

				item.Title = shift.Name;
				item.Description = shift.Name;
				item.ItemType = day.ShiftId; // The Color in the calendar
				item.SignupType = shift.AssignmentType;
				item.ShiftId = day.ShiftId;
				item.Filled = await _shiftsService.IsShiftDayFilledAsync(day.ShiftDayId);
				item.UserSignedUp = await _shiftsService.IsUserSignedUpForShiftDayAsync(day, UserId, null);

				var shiftGroups = await _shiftsService.GetShiftDayNeedsAsync(day.ShiftDayId);

				if (shiftGroups != null)
				{
					foreach (var shiftGroup in shiftGroups)
					{
						var group = new ShiftGroupNeeds();

						var departmentGroup = allGroups.FirstOrDefault(x => x.DepartmentGroupId == shiftGroup.Key);
						if (departmentGroup != null)
						{
							group.ShiftGroupId = departmentGroup.DepartmentGroupId;
							group.Name = departmentGroup.Name;

							foreach (var shiftGroupNeed in shiftGroup.Value)
							{
								var role = allRoles.FirstOrDefault(x => x.PersonnelRoleId == shiftGroupNeed.Key);

								if (role != null)
								{
									var shiftGroupNeedRole = new ShiftGroupNeedRole();
									shiftGroupNeedRole.RoleId = shiftGroupNeed.Key;
									shiftGroupNeedRole.Name = role.Name;
									shiftGroupNeedRole.Needed = shiftGroupNeed.Value;

									group.Needs.Add(shiftGroupNeedRole);
								}
							}

							item.Groups.Add(group);
						}

					}
				}

				if (shift.Personnel != null)
				{
					foreach (var person in shift.Personnel)
					{
						var name = allUserNames.FirstOrDefault(x => x.UserId == person.UserId);
						if (name != null)
						{
							var shiftUser = new ShiftUser();
							shiftUser.UserId = person.UserId;
							shiftUser.Name = name.Name;

							if (person.UserId == UserId)
								shiftUser.IsYouOnShift = true;
							else
								shiftUser.IsYouOnShift = false;

							item.Users.Add(shiftUser);
						}
					}
				}

				calendarItems.Add(item);
			}

			return Json(calendarItems);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetShiftCalendarItemTypes()
		{
			var itemsJson = new List<CalendarItemTypeJson>();
			var shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId);

			foreach (var shift in shifts)
			{
				if (!shift.Color.Equals("#FFFFFF", StringComparison.InvariantCultureIgnoreCase))
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

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetPersonnelForShift(int shiftId, int? groupId)
		{
			var usersJson = new List<UserJson>();
			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);
			var group = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			if (shift == null || shift.DepartmentId != DepartmentId)
				return Json(usersJson);

			shift = await _shiftsService.PopulateShiftData(shift, true, true, true, true, true);

			foreach (var user in shift.Personnel)
			{
				var userJson = new UserJson();
				userJson.UserId = user.UserId;
				userJson.Name = await UserHelper.GetFullNameForUser(user.UserId);

				if (groupId.HasValue && groupId.Value > 0 && user.GroupId.HasValue)
				{
					var selectedGroup = group.FirstOrDefault(x => x.DepartmentGroupId == groupId.Value);

					if (selectedGroup != null && selectedGroup.Members.Any(y => y.UserId == user.UserId))
						usersJson.Add(userJson);
				}
				else if (groupId.HasValue && groupId.Value == 0 && !user.GroupId.HasValue)
					usersJson.Add(userJson);
				else if (!groupId.HasValue)
					usersJson.Add(userJson);
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
					Unauthorized();

				foreach (var group in shift.Groups)
				{
					groups.Add(new
					{
						Id = group.DepartmentGroupId,
						Name = group.DepartmentGroup.Name
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
					Unauthorized();

				foreach (var day in shift.Days)
				{
					days.Add(new
					{
						Processed = day.Processed.GetValueOrDefault(),
						Day = day.Day.ToShortDateString()
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

			if (shift.DepartmentId != DepartmentId)
				return null;

			shift = await _shiftsService.PopulateShiftData(shift, true, true, true, true, true);

			var shiftJson = JsonConvert.SerializeObject(shift);

			//return Json(shiftJson);
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

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetPersonnelNotOnShiftDay(int shiftSignupId, int shiftDayId)
		{
			var usersJson = new List<UserJson>();
			var signup = await _shiftsService.GetShiftSignupByIdAsync(shiftSignupId);
			var signups = await _shiftsService.GetShiftSignpsForShiftDayAsync(shiftDayId);
			var personnel = await _departmentService.GetAllUsersForDepartmentAsync(DepartmentId);
			var roles = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(DepartmentId);

			var rolesForSignup = new List<ShiftGroupRole>();

			if (roles.ContainsKey(signup.UserId))
				rolesForSignup.AddRange(signup.Shift.Groups.First(x => x.DepartmentGroupId == signup.DepartmentGroupId).Roles.Where(x => roles[signup.UserId].Select(z => z.PersonnelRoleId).Contains(x.PersonnelRoleId)));

			foreach (var user in personnel.Select(x => x.UserId).Except(signups.Select(y => y.UserId)))
			{
				int matchRequirements = 0;

				if (roles.ContainsKey(user))
					matchRequirements = rolesForSignup.Count(role => roles[user].Select(x => x.PersonnelRoleId).Any(y => y == role.PersonnelRoleId));

				if (rolesForSignup.Count() == matchRequirements)
				{
					var profile = personnel.First(x => x.UserId == user);

					var userJson = new UserJson();
					userJson.UserId = user;
					userJson.Name = await UserHelper.GetFullNameForUser(user);

					usersJson.Add(userJson);
				}
			}

			return Json(usersJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> GetShiftDaysUserIsOn(int shiftTradeId)
		{
			var shiftDayJson = new List<ShiftDayJson>();
			var trade = await _shiftsService.GetShiftTradeByIdAsync(shiftTradeId);
			var signups = await _shiftsService.GetShiftSignupsForUserAsync(UserId);

			//var validShiftDays = from signup in signups
			//										 let shiftDaySignups = await _shiftsService.GetShiftSignpsForShiftDayAsync(signup.ShiftSignupId)
			//										 where !(from d in shiftDaySignups
			//														 select d.UserId)
			//											 .Contains(trade.SourceShiftSignup.UserId)
			//										 select signup;

			var validShiftDays = new List<ShiftSignup>();
			foreach (var signup in signups)
			{
				var shiftDaySignups = await _shiftsService.GetShiftSignpsForShiftDayAsync(signup.ShiftSignupId);

				if (!(from d in shiftDaySignups select d.UserId).Contains(trade.SourceShiftSignup.UserId))
					validShiftDays.Add(signup);
			}

			foreach (var day in validShiftDays)
			{
				var shiftDay = new ShiftDayJson();
				shiftDay.ShiftSignupId = day.ShiftSignupId;
				shiftDay.Title = String.Format("{0} on {1}", day.Shift.Name, day.ShiftDay.ToShortDateString());

				shiftDayJson.Add(shiftDay);
			}

			return Json(shiftDayJson);
		}
		#endregion Async Calls
	}
}
