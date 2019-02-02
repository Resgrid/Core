using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
using RestSharp.Extensions.MonoHttp;
using Microsoft.AspNetCore.Authorization;
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

		public ShiftsController(IShiftsService shiftsService, IDepartmentGroupsService departmentGroupsService, IDepartmentsService departmentService,
			IPersonnelRolesService personnelRolesService, IUserProfileService userProfileService, IEventAggregator eventAggregator, IDepartmentSettingsService departmentSettingsService)
		{
			_shiftsService = shiftsService;
			_departmentGroupsService = departmentGroupsService;
			_departmentService = departmentService;
			_personnelRolesService = personnelRolesService;
			_userProfileService = userProfileService;
			_eventAggregator = eventAggregator;
			_departmentSettingsService = departmentSettingsService;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult Index()
		{
			var model = new ShiftsIndexModel();
			model.Shifts = _shiftsService.GetAllShiftsByDepartment(DepartmentId);

			var department = _departmentService.GetDepartmentById(DepartmentId);

			if (department.IsUserAnAdmin(UserId))
				model.IsUserAdminOrGroupAdmin = true;
			else
			{
				var groups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);

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
		public IActionResult NewShift()
		{
			var model = new NewShiftView();
			model.Shift = new Shift();
			model.Groups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);
			ViewBag.ShiftAssignmentTypes = model.AssignmentType.ToSelectList();

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public IActionResult EditShiftDetails(int shiftId)
		{
			var model = new EditShiftView();

			var shift = _shiftsService.GetShiftById(shiftId);

			if (shift.DepartmentId != DepartmentId)
				Unauthorized();

			model.Shift = shift;
			model.Groups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public IActionResult EditShiftDetails(EditShiftView model, IFormCollection form)
		{
			if (ModelState.IsValid)
			{
				var shift = _shiftsService.GetShiftById(model.Shift.ShiftId);
				shift.Name = model.Shift.Name;
				shift.Code = model.Shift.Code;
				shift.Color = model.Shift.Color;
				shift.StartTime = model.Shift.StartTime;
				shift.EndTime = model.Shift.EndTime;

				ViewBag.ShiftAssignmentTypes = model.AssignmentType.ToSelectList();

				shift = _shiftsService.SaveShift(shift);
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

				_shiftsService.UpdateShiftPersonnel(shift, personnel);

				var number = _departmentSettingsService.GetTextToCallNumberForDepartment(DepartmentId);
				_eventAggregator.SendMessage<ShiftUpdatedEvent>(new ShiftUpdatedEvent() { DepartmentId = DepartmentId, DepartmentNumber = number, Item = shift });

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_Create)]
		public IActionResult NewShift(NewShiftView model, IFormCollection form)
		{
			model.Shift.AssignmentType = (int)model.AssignmentType;
			//model.Shift.AssignmentType = (int)ShiftAssignmentTypes.Assigned;
			model.Shift.DepartmentId = DepartmentId;
			model.Shift.ScheduleType = (int)ShiftScheduleTypes.Manual;
			model.Shift.Name = form["Shift_Name"];
			model.Shift.Code = form["Shift_Code"];
			model.Shift.StartTime = form["Shift_StartTime"];
			model.Shift.EndTime = form["Shift_EndTime"];
			model.Groups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);

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
					model.Shift.StartDay = DateTime.UtcNow.TimeConverter(_departmentService.GetDepartmentById(DepartmentId, false));
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
				var newShift = _shiftsService.SaveShift(model.Shift);

				var number = _departmentSettingsService.GetTextToCallNumberForDepartment(DepartmentId);
				_eventAggregator.SendMessage<ShiftCreatedEvent>(new ShiftCreatedEvent() { DepartmentId = DepartmentId, DepartmentNumber = number, Item = newShift });

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult RequestTrade(int shiftSignUpId)
		{
			var model = new RequestTradeView();
			model.Signup = _shiftsService.GetShiftSignupById(shiftSignUpId);
			model.ShiftDay = _shiftsService.GetShiftDayForSignup(shiftSignUpId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult ShiftCalendar(int shiftId)
		{
			var model = new ShiftCalendarView();
			model.Shift = _shiftsService.GetShiftById(shiftId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult RequestTrade(RequestTradeView model, IFormCollection form)
		{
			string[] users = null;

			if (form.ContainsKey("users"))
				users = form["users"].ToString().Split(char.Parse(","));

			if (users == null || !users.Any())
				ModelState.AddModelError("users", "You must specify users to request a trade from. Only qualified users will populate the list.");

			model.Signup = _shiftsService.GetShiftSignupById(model.Signup.ShiftSignupId);
			model.ShiftDay = _shiftsService.GetShiftDayForSignup(model.Signup.ShiftSignupId);

			if (ModelState.IsValid && users != null)
			{
				var trade = new ShiftSignupTrade();
				trade.Users = new List<ShiftSignupTradeUser>();
				trade.SourceShiftSignupId = model.Signup.ShiftSignupId;

				foreach (var user in users)
				{
					trade.Users.Add(new ShiftSignupTradeUser() { UserId = user });
				}

				trade = _shiftsService.SaveTrade(trade);

				var tradeRequestedEvent = new ShiftTradeRequestedEvent();
				tradeRequestedEvent.DepartmentId = DepartmentId;
				tradeRequestedEvent.ShiftSignupTradeId = trade.ShiftSignupTradeId;

				var number = _departmentSettingsService.GetTextToCallNumberForDepartment(DepartmentId);
				tradeRequestedEvent.DepartmentNumber = number;

				_eventAggregator.SendMessage<ShiftTradeRequestedEvent>(tradeRequestedEvent);

				return RedirectToAction("YourShifts");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public IActionResult EditShiftDays(int shiftId)
		{
			var model = new EditShiftView();

			var shift = _shiftsService.GetShiftById(shiftId);

			if (shift.DepartmentId != DepartmentId)
				Unauthorized();

			model.Shift = shift;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public IActionResult EditShiftDays(EditShiftView model)
		{
			var shift = _shiftsService.GetShiftById(model.Shift.ShiftId);

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
				model.Shift.StartDay = DateTime.UtcNow.TimeConverter(_departmentService.GetDepartmentById(DepartmentId, false));
			}

			_shiftsService.UpdateShiftDates(shift, days);

			var number = _departmentSettingsService.GetTextToCallNumberForDepartment(DepartmentId);
			_eventAggregator.SendMessage<ShiftDaysAddedEvent>(new ShiftDaysAddedEvent() { DepartmentId = DepartmentId, DepartmentNumber = number, Item = shift });

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public IActionResult EditShiftGroups(int shiftId)
		{
			var model = new EditShiftView();

			var shift = _shiftsService.GetShiftById(shiftId);

			if (shift.DepartmentId != DepartmentId)
				Unauthorized();

			model.Shift = shift;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_Update)]
		public IActionResult EditShiftGroups(EditShiftView model, IFormCollection form)
		{
			var shift = _shiftsService.GetShiftById(model.Shift.ShiftId);

			if (shift.DepartmentId != DepartmentId)
				Unauthorized();
			model.Shift = shift;

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

				_shiftsService.UpdateShiftGroups(model.Shift, groups);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_Delete)]
		public IActionResult DeleteShift(int shiftId)
		{
			var shift = _shiftsService.GetShiftById(shiftId);

			if (shift.DepartmentId != DepartmentId)
				Unauthorized();

			_shiftsService.DeleteShift(shift);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult Signup(int shiftDayId)
		{
			var model = new ShiftSignupView();
			model.Day = _shiftsService.GetShiftDayById(shiftDayId);

			if (model.Day.Shift.DepartmentId != DepartmentId)
				Unauthorized();

			model.Roles = _personnelRolesService.GetRolesForUser(UserId, DepartmentId);
			model.Needs = _shiftsService.GetShiftDayNeeds(shiftDayId);
			model.Signups = _shiftsService.GetShiftSignpsForShiftDay(shiftDayId);
			model.UserSignedUp = _shiftsService.IsUserSignedUpForShiftDay(model.Day, UserId);
			model.PersonnelRoles = _personnelRolesService.GetAllRolesForUsersInDepartment(DepartmentId);
			model.UserProfiles = _userProfileService.GetAllProfilesForDepartment(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult ViewShift(int shiftDayId)
		{
			var model = new ShiftSignupView();
			model.Day = _shiftsService.GetShiftDayById(shiftDayId);

			if (model.Day.Shift.DepartmentId != DepartmentId)
				Unauthorized();

			model.Roles = _personnelRolesService.GetRolesForUser(UserId, DepartmentId);
			model.Needs = _shiftsService.GetShiftDayNeeds(shiftDayId);
			model.Signups = _shiftsService.GetShiftSignpsForShiftDay(shiftDayId);
			model.UserSignedUp = _shiftsService.IsUserSignedUpForShiftDay(model.Day, UserId);
			model.PersonnelRoles = _personnelRolesService.GetAllRolesForUsersInDepartment(DepartmentId);
			model.UserProfiles = _userProfileService.GetAllProfilesForDepartment(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult ShiftDaySignup(int shiftDayId, int groupId)
		{
			var day = _shiftsService.GetShiftDayById(shiftDayId);

			if (day.Shift.DepartmentId != DepartmentId)
				Unauthorized();

			var signup = _shiftsService.SignupForShiftDay(day.ShiftId, day.Day, groupId, UserId);

			return RedirectToAction("SignupSuccess", new { shiftSignupId = signup.ShiftSignupId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult SignupSuccess(int shiftSignupId)
		{
			var model = new ShiftSignupView();
			model.Signup = _shiftsService.GetShiftSignupById(shiftSignupId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult YourShifts()
		{
			var model = new YourShiftsView();
			model.Signups = _shiftsService.GetShiftSignupsForUser(UserId);
			model.Department = _departmentService.GetDepartmentById(DepartmentId, false);
			model.Trades = _shiftsService.GetOpenTradeRequestsForUser(UserId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult DeclineShiftDay(int shiftSignupId)
		{
			var signup = _shiftsService.GetShiftSignupById(shiftSignupId);

			if (signup.Shift.DepartmentId != DepartmentId)
				Unauthorized();

			_shiftsService.DeleteShiftSignup(signup);

			return RedirectToAction("YourShifts");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult ProcessTrade(int shiftSignupTradeId)
		{
			var model = new ProcessTradeView();
			model.Trade = _shiftsService.GetShiftTradeById(shiftSignupTradeId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult ProcessTrade(ProcessTradeView model, IFormCollection form)
		{
			string[] dates = null;
			string note = null;

			if (form.ContainsKey("dates"))
				dates = form["dates"].ToString().Split(char.Parse(","));

			if (form.ContainsKey("note"))
				note = form["note"];

			var tradeRequest = _shiftsService.GetShiftTradeById(model.Trade.ShiftSignupTradeId);

			if (dates != null && dates.Any())
				_shiftsService.ProposeShiftDaysForTrade(tradeRequest.ShiftSignupTradeId, UserId, note, dates.Select(x => int.Parse(x)).ToList());
			else
				_shiftsService.ProposeShiftDaysForTrade(tradeRequest.ShiftSignupTradeId, UserId, note, null);

			var shiftTradeProposed = new ShiftTradeProposedEvent();
			shiftTradeProposed.DepartmentId = DepartmentId;
			shiftTradeProposed.ShiftSignupTradeId = model.Trade.ShiftSignupTradeId;
			shiftTradeProposed.UserId = UserId;

			var number = _departmentSettingsService.GetTextToCallNumberForDepartment(DepartmentId);
			shiftTradeProposed.DepartmentNumber = number;

			_eventAggregator.SendMessage<ShiftTradeProposedEvent>(shiftTradeProposed);

			return RedirectToAction("YourShifts");


			model.Trade = tradeRequest;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult RejectTrade(int shiftTradeId, string reason)
		{
			var decodedReason = HttpUtility.UrlDecode(reason);

			_shiftsService.RejectTradeRequest(shiftTradeId, UserId, decodedReason);

			var shiftTradeRejected = new ShiftTradeRejectedEvent();
			shiftTradeRejected.DepartmentId = DepartmentId;
			shiftTradeRejected.ShiftSignupTradeId = shiftTradeId;
			shiftTradeRejected.UserId = UserId;

			var number = _departmentSettingsService.GetTextToCallNumberForDepartment(DepartmentId);
			shiftTradeRejected.DepartmentNumber = number;

			_eventAggregator.SendMessage<ShiftTradeRejectedEvent>(shiftTradeRejected);

			return RedirectToAction("YourShifts");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult FinishTrade(int shiftSignupTradeId)
		{
			var model = new FinishTradeView();
			model.Trade = _shiftsService.GetShiftTradeById(shiftSignupTradeId);
			model.Profiles = _userProfileService.GetSelectedUserProfiles(model.Trade.Users.Select(x => x.UserId).ToList());

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult FinishTrade(FinishTradeView model, IFormCollection form)
		{
			string selectedShift = null;
			if (form.ContainsKey("selectedShift"))
				selectedShift = form["selectedShift"];

			var tradeRequest = _shiftsService.GetShiftTradeById(model.Trade.ShiftSignupTradeId);

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
					var shiftSignup = _shiftsService.GetShiftSignupById(tradeRequest.TargetShiftSignupId.Value);
					userId = Guid.Parse(shiftSignup.UserId);
				}

				var shiftTradeFilled = new ShiftTradeFilledEvent();
				shiftTradeFilled.DepartmentId = DepartmentId;
				shiftTradeFilled.ShiftSignupTradeId = model.Trade.ShiftSignupTradeId;
				shiftTradeFilled.UserId = userId.ToString();

				var number = _departmentSettingsService.GetTextToCallNumberForDepartment(DepartmentId);
				shiftTradeFilled.DepartmentNumber = number;

				_shiftsService.SaveTrade(tradeRequest);

				_eventAggregator.SendMessage<ShiftTradeFilledEvent>(shiftTradeFilled);

				return RedirectToAction("YourShifts");
			}

			model.Trade = tradeRequest;
			model.Profiles = _userProfileService.GetSelectedUserProfiles(model.Trade.Users.Select(x => x.UserId).ToList());
			model.Message = "You must select a shift to trade for or accept an unbalanced trade";

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult ShiftStaffing()
		{
			var model = new ShiftStaffingView();

			SetShiftStaffingModel(model);

			return View(model);
		}

		private void SetShiftStaffingModel(ShiftStaffingView model)
		{
			model.Shifts = new List<Shift>();

			var department = _departmentService.GetDepartmentById(DepartmentId);
			var group = _departmentGroupsService.GetGroupForUser(UserId, DepartmentId);
			var shifts = _shiftsService.GetAllShiftsByDepartment(DepartmentId);

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
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult ShiftStaffing(ShiftStaffingView model, IFormCollection form)
		{
			if (String.IsNullOrWhiteSpace(form["shiftDayPicker"]))
			{
				SetShiftStaffingModel(model);
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

				var savedStaffing = _shiftsService.SaveShiftStaffing(staffing);

				return RedirectToAction("Index");
			}

			return View(model);
		}


		#region Async Calls
		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult GetShiftCalendarItems()
		{
			var calendarItems = new List<ShiftCalendarItemJson>();
			var shifts = _shiftsService.GetAllShiftsByDepartment(DepartmentId);
			var allGroups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);
			var allRoles = _personnelRolesService.GetRolesForDepartment(DepartmentId);
			var allUserNames = _departmentService.GetAllPersonnelNamesForDepartment(DepartmentId);

			foreach (var shift in shifts)
			{
				foreach (var day in shift.Days)
				{
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

					item.Title = shift.Name;
					item.Description = shift.Name;
					item.ItemType = day.ShiftId; // The Color in the calendar
					item.SignupType = day.Shift.AssignmentType;
					item.ShiftId = day.ShiftId;
					item.Filled = _shiftsService.IsShiftDayFilled(day.ShiftDayId);
					item.UserSignedUp = _shiftsService.IsUserSignedUpForShiftDay(day, UserId);

					var shiftGroups = _shiftsService.GetShiftDayNeeds(day.ShiftDayId);

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

			return Json(calendarItems);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult GetShiftCalendarItemsForShift(int shiftId)
		{
			var calendarItems = new List<ShiftCalendarItemJson>();
			var shift = _shiftsService.GetShiftById(shiftId);
			var allGroups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);
			var allRoles = _personnelRolesService.GetRolesForDepartment(DepartmentId);
			var allUserNames = _departmentService.GetAllPersonnelNamesForDepartment(DepartmentId);

			foreach (var day in shift.Days)
			{
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

				item.Title = shift.Name;
				item.Description = shift.Name;
				item.ItemType = day.ShiftId; // The Color in the calendar
				item.SignupType = day.Shift.AssignmentType;
				item.ShiftId = day.ShiftId;
				item.Filled = _shiftsService.IsShiftDayFilled(day.ShiftDayId);
				item.UserSignedUp = _shiftsService.IsUserSignedUpForShiftDay(day, UserId);

				var shiftGroups = _shiftsService.GetShiftDayNeeds(day.ShiftDayId);

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
		public IActionResult GetShiftCalendarItemTypes()
		{
			var itemsJson = new List<CalendarItemTypeJson>();
			var shifts = _shiftsService.GetAllShiftsByDepartment(DepartmentId);

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
		public IActionResult GetPersonnelForShift(int shiftId, int? groupId)
		{
			var usersJson = new List<UserJson>();
			var shift = _shiftsService.GetShiftById(shiftId);
			var group = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);

			if (shift.DepartmentId != DepartmentId)
				return null;

			foreach (var user in shift.Personnel)
			{
				var userJson = new UserJson();
				userJson.UserId = user.UserId;
				userJson.Name = UserHelper.GetFullNameForUser(user.UserId);

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
		public IActionResult GetShiftGroups(int shiftId)
		{
			var groups = new List<dynamic>();
			var shift = _shiftsService.GetShiftById(shiftId);

			foreach (var group in shift.Groups)
			{
				groups.Add(new
				{
					Id = group.DepartmentGroupId,
					Name = group.DepartmentGroup.Name
				});
			}

			return Json(groups);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult GetShiftDays(int shiftId)
		{
			var days = new List<dynamic>();
			var shift = _shiftsService.GetShiftById(shiftId);

			foreach (var day in shift.Days)
			{
				days.Add(new
				{
					Processed = day.Processed.GetValueOrDefault(),
					Day = day.Day.ToShortDateString()
				});
			}

			return Json(days);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult GetShiftJson(int shiftId)
		{
			var shift = _shiftsService.GetShiftById(shiftId);

			if (shift.DepartmentId != DepartmentId)
				return null;

			var shiftJson = JsonConvert.SerializeObject(shift);

			//return Json(shiftJson);
			return Content(shiftJson, "application/json");
		}

		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult GetShiftsForDepartmentJson()
		{
			var shiftsJson = new List<dynamic>();
			var shifts = _shiftsService.GetAllShiftsByDepartment(DepartmentId);

			foreach (var shift in shifts)
			{
				shiftsJson.Add(new
				{
					Id = shift.ShiftId,
					Name = shift.Name
				});
			}

			return Json(shiftsJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult GetPersonnelNotOnShiftDay(int shiftSignupId, int shiftDayId)
		{
			var usersJson = new List<UserJson>();
			var signup = _shiftsService.GetShiftSignupById(shiftSignupId);
			var signups = _shiftsService.GetShiftSignpsForShiftDay(shiftDayId);
			var personnel = _departmentService.GetAllUsersForDepartment(DepartmentId);
			var roles = _personnelRolesService.GetAllRolesForUsersInDepartment(DepartmentId);

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
					userJson.Name = UserHelper.GetFullNameForUser(user);

					usersJson.Add(userJson);
				}
			}

			return Json(usersJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public IActionResult GetShiftDaysUserIsOn(int shiftTradeId)
		{
			var shiftDayJson = new List<ShiftDayJson>();
			var trade = _shiftsService.GetShiftTradeById(shiftTradeId);
			var signups = _shiftsService.GetShiftSignupsForUser(UserId);

			var validShiftDays = from signup in signups
													 let shiftDaySignups = _shiftsService.GetShiftSignpsForShiftDay(signup.ShiftSignupId)
													 where !(from d in shiftDaySignups
																	 select d.UserId)
														 .Contains(trade.SourceShiftSignup.UserId)
													 select signup;

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
