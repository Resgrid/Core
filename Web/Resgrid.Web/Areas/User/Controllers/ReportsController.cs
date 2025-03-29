using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.Departments.ActionLogs;
using Resgrid.Web.Areas.User.Models.Reports.Activity;
using Resgrid.Web.Areas.User.Models.Reports.Calls;
using Resgrid.Web.Areas.User.Models.Reports.Certifications;
using Resgrid.Web.Areas.User.Models.Reports.Logs;
using Resgrid.Web.Areas.User.Models.Reports.Params;
using Resgrid.Web.Areas.User.Models.Reports.Personnel;
using Resgrid.Web.Areas.User.Models.Reports.Shifts;
using Resgrid.Web.Areas.User.Models.Reports.Units;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[ClaimsResource(ResgridClaimTypes.Resources.Reports)]
	public class ReportsController : SecureBaseController
	{
		#region Private Members and Constructors

		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IUserProfileService _userProfileService;
		private readonly IAddressService _addressService;
		private readonly IUserStateService _userStateService;
		private readonly IScheduledTasksService _scheduledTasksService;
		private readonly ICertificationService _certificationService;
		private readonly IWorkLogsService _logService;
		private readonly IShiftsService _shiftsService;
		private readonly ICallsService _callsService;
		private readonly IWorkLogsService _workLogsService;
		private readonly ICustomStateService _customStateService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IUnitsService _unitsService;
		private readonly IUnitStatesService _unitStatesService;

		public ReportsController(IDepartmentsService departmentsService, IUsersService usersService,
			IActionLogsService actionLogsService,
			IDepartmentGroupsService departmentGroupsService, IPersonnelRolesService personnelRolesService,
			IUserProfileService userProfileService,
			IAddressService addressService, IUserStateService userStateService,
			IScheduledTasksService scheduledTasksService,
			ICertificationService certificationService, IWorkLogsService logService, IShiftsService shiftsService,
			ICallsService callsService, IWorkLogsService workLogsService,
			ICustomStateService customStateService, IAuthorizationService authorizationService,
			IUnitsService unitsService, IUnitStatesService unitStatesService)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_userProfileService = userProfileService;
			_addressService = addressService;
			_userStateService = userStateService;
			_scheduledTasksService = scheduledTasksService;
			_certificationService = certificationService;
			_logService = logService;
			_shiftsService = shiftsService;
			_callsService = callsService;
			_workLogsService = workLogsService;
			_customStateService = customStateService;
			_authorizationService = authorizationService;
			_unitsService = unitsService;
			_unitStatesService = unitStatesService;
		}

		#endregion Private Members and Constructors

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> Index()
		{
			return View();
		}

		#region Action Logs

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> ActionLogs(bool groupSelect, int groupId, string userId, DateTime start,
			DateTime end)
		{
			//var model = new PersonnelStatusHistoryView();
			//model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			//model.User = _usersService.GetUserById(UserId);

			return View(
				await PersonnelStatusHistoryReportModel(DepartmentId, groupSelect, groupId, userId, start, end));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> GetActionLogs()
		{
			var actionLogs = new List<ActionLogForJson>();
			var dep = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var logs = await _actionLogsService.GetAllActionLogsForDepartmentAsync(DepartmentId);

			var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			foreach (var l in logs)
			{
				var actionLog = new ActionLogForJson();

				var name = names.FirstOrDefault(x => x.UserId == l.UserId);
				if (name != null)
					actionLog.Name = name.Name;
				else
					actionLog.Name = await UserHelper.GetFullNameForUser(l.UserId);

				actionLog.Timestamp = TimeConverterHelper.TimeConverterToString(l.Timestamp, dep);
				actionLog.ActionType = l.ActionTypeId;

				if (l.ActionTypeId <= 25)
				{
					if (l.ActionTypeId == 1)
					{
						actionLog.Action = "Not Responding";
					}
					else if (l.ActionTypeId == 2)
					{
						actionLog.Action = "Responding";
					}
					else if (l.ActionTypeId == 0)
					{
						actionLog.Action = "Available";
					}
					else if (l.ActionTypeId == 3)
					{
						actionLog.Action = "On Scene";
					}
					else if (l.ActionTypeId == 4)
					{
						if (l.DestinationId.HasValue)
						{
							var group = await _departmentGroupsService.GetGroupByIdAsync(l.DestinationId.Value);
							actionLog.Action = string.Format("Available at {0}", group.Name);
						}
						else
							actionLog.Action = "Available Station";
					}
					else if (l.ActionTypeId == 5)
					{
						if (l.DestinationId.HasValue)
						{
							var group = await _departmentGroupsService.GetGroupByIdAsync(l.DestinationId.Value);
							actionLog.Action = string.Format("Responding To Station {0}", group.Name);
						}
						else
							actionLog.Action = "Responding To Station";
					}
					else if (l.ActionTypeId == 6)
					{
						if (l.DestinationId.HasValue)
							actionLog.Action = string.Format("Responding To Scene {0}", l.DestinationId.Value);
						else
							actionLog.Action = "Responding To Scene ";
					}
				}
				else
				{
					var customStatus = await CustomStatesHelper.GetCustomState(DepartmentId, l.ActionTypeId);

					if (customStatus != null)
					{
						actionLog.Action = customStatus.ButtonText;
					}
					else
					{
						actionLog.Action = "Unknown";
					}
				}

				actionLogs.Add(actionLog);
			}

			return Json(actionLogs);
		}

		#endregion Action Logs

		#region Views

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> PersonnelReport()
		{
			return View(await CreatePersonnelReportModel(DepartmentId));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> StaffingReport()
		{
			return View(await CreateStaffingReportModel(DepartmentId));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> CertificationsReport()
		{
			return View(await CreateCertificationsReportModel(DepartmentId));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> LogReport(int logId)
		{
			if (!await _authorizationService.CanUserViewAndEditWorkLogAsync(UserId, logId))
				Unauthorized();

			return View(await CreateLogReportModel(logId));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> UpcomingShiftReadinessReport()
		{
			return View(await UpcomingShiftReadinessReportModel(DepartmentId));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> DepartmentActivityReport()
		{
			return View(await DepartmentActivityReportModel(DepartmentId));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> PersonnelHoursReport(DateTime start, DateTime end)
		{
			return View(await PersonnelHoursReportModel(DepartmentId, start, end));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> PersonnelHoursDetailReport(string userId, DateTime start, DateTime end)
		{
			return View(await PersonnelHoursDetailReportModel(DepartmentId, userId, start, end));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> PersonnelStaffingHistoryReport(bool groupSelect, int groupId, string userId,
			DateTime start, DateTime end)
		{
			return View(
				await PersonnelStaffingHistoryReportModel(DepartmentId, groupSelect, groupId, userId, start, end));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> CallSummaryReport(DateTime start, DateTime end)
		{
			return View(await CallSummaryReportModel(DepartmentId, start, end));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> PersonnelHoursReportParams()
		{
			var model = new PersonnelHoursReportParams();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			model.Start =
				TimeConverterHelper.TimeConverter(new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1), department);
			model.End = TimeConverterHelper.TimeConverter(new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59),
				department);

			var profiles = new List<UserProfile>();

			if (await _authorizationService.CanUserViewAllPeopleAsync(UserId, DepartmentId))
				profiles.Add(new UserProfile() { UserId = String.Empty, FirstName = "All", LastName = "Users" });

			var users = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId);
			foreach (var u in users)
			{
				if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(u.Key, UserId, DepartmentId))
					continue;

				profiles.Add(u.Value);
			}

			model.Users = new SelectList(profiles, "UserId", "FullName.AsFirstNameLastName", Guid.Empty);

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> PersonnelHoursReportParams(PersonnelHoursReportParams model)
		{
			if (string.IsNullOrWhiteSpace(model.UserId))
				return RedirectToAction("PersonnelHoursReport", new { start = model.Start, end = model.End });
			else
				return RedirectToAction("PersonnelHoursDetailReport",
					new { userId = model.UserId, start = model.Start, end = model.End });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> PersonnelStaffingHistoryReportParams()
		{
			var model = new PersonnelStaffingHistoryReportParams();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			model.Start =
				TimeConverterHelper.TimeConverter(new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1), department);
			model.End = TimeConverterHelper.TimeConverter(new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59),
				department);

			var profiles = new List<UserProfile>();

			if (await _authorizationService.CanUserViewAllPeopleAsync(UserId, DepartmentId))
				profiles.Add(new UserProfile() { UserId = String.Empty, FirstName = "All", LastName = "Users" });

			var users = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId);
			foreach (var u in users)
			{
				if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(u.Key, UserId, DepartmentId))
					continue;

				profiles.Add(u.Value);
			}

			model.Users = new SelectList(profiles, "UserId", "FullName.AsFirstNameLastName", Guid.Empty);

			if (await _authorizationService.CanUserViewAllPeopleAsync(UserId, DepartmentId))
			{
				var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
				model.Groups = new SelectList(groups, "DepartmentGroupId", "Name", 0);
			}
			else
			{
				model.Groups = new SelectList(new List<DepartmentGroup>(), "DepartmentGroupId", "Name", 0);
			}

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> PersonnelStaffingHistoryReportParams(
			PersonnelStaffingHistoryReportParams model)
		{
			return RedirectToAction("PersonnelStaffingHistoryReport",
				new
				{
					groupSelect = model.GroupSelect, groupId = model.GroupId, userId = model.UserId,
					start = model.Start, end = model.End
				});
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> UnitStateHistoryReportParams()
		{
			var model = new UnitStateHistoryReportParams();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			model.Start =
				TimeConverterHelper.TimeConverter(new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1), department);
			model.End = TimeConverterHelper.TimeConverter(new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59),
				department);

			var units = new List<Unit>();
			units.Add(new Unit() { UnitId = 0, Name = "All Units" });

			units.AddRange(await _unitsService.GetUnitsForDepartmentAsync(DepartmentId));
			model.Units = new SelectList(units, "UnitId", "Name", 0);

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			model.Groups = new SelectList(groups, "DepartmentGroupId", "Name", 0);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> UnitStateHistoryReportParams(UnitStateHistoryReportParams model)
		{
			return RedirectToAction("UnitStateHistoryReport",
				new
				{
					groupSelect = model.GroupSelect, groupId = model.GroupId, unitId = model.UnitId,
					start = model.Start, end = model.End
				});
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> UnitStateHistoryReport(bool groupSelect, int groupId, int unitId,
			DateTime start, DateTime end)
		{
			return View(await UnitStateHistoryReportModel(DepartmentId, groupSelect, groupId, unitId, start, end));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> ActionLogsParams()
		{
			var model = new PersonnelStaffingHistoryReportParams();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			model.Start =
				TimeConverterHelper.TimeConverter(new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1), department);
			model.End = TimeConverterHelper.TimeConverter(new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59),
				department);

			var profiles = new List<UserProfile>();
			if (await _authorizationService.CanUserViewAllPeopleAsync(UserId, DepartmentId))
				profiles.Add(new UserProfile() { UserId = String.Empty, FirstName = "All", LastName = "Users" });

			var users = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId);
			foreach (var u in users)
			{
				if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(u.Key, UserId, DepartmentId))
					continue;

				profiles.Add(u.Value);
			}

			model.Users = new SelectList(profiles, "UserId", "FullName.AsFirstNameLastName", Guid.Empty);

			if (await _authorizationService.CanUserViewAllPeopleAsync(UserId, DepartmentId))
			{
				var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
				model.Groups = new SelectList(groups, "DepartmentGroupId", "Name", 0);
			}
			else
			{
				model.Groups = new SelectList(new List<DepartmentGroup>(), "DepartmentGroupId", "Name", 0);
			}

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> ActionLogsParams(PersonnelStaffingHistoryReportParams model)
		{
			return RedirectToAction("ActionLogs",
				new
				{
					groupSelect = model.GroupSelect, groupId = model.GroupId, userId = model.UserId,
					start = model.Start, end = model.End
				});
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> CallSummaryReportParams()
		{
			var model = new PersonnelHoursReportParams();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			model.Start =
				TimeConverterHelper.TimeConverter(new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1), department);
			model.End = TimeConverterHelper.TimeConverter(new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59),
				department);

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> CallSummaryReportParams(PersonnelHoursReportParams model)
		{
			return RedirectToAction("CallSummaryReport", new { start = model.Start, end = model.End });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Reports_View)]
		public async Task<IActionResult> ActiveCallsResourcesReport()
		{
			return View(await ActiveCallsResourcesReportModel(DepartmentId));
		}
		#endregion Views

		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> InternalRunReport(int type, int departmentId)
		{
			if (((ReportTypes)type) == ReportTypes.Staffing)
			{
				return View("StaffingReport", await CreateStaffingReportModel(departmentId));
			}
			else if (((ReportTypes)type) == ReportTypes.Personnel)
			{
				return View("PersonnelReport", await CreatePersonnelReportModel(departmentId));
			}
			else if (((ReportTypes)type) == ReportTypes.Certifications)
			{
				return View("CertificationsReport", await CreateCertificationsReportModel(departmentId));
			}
			else if (((ReportTypes)type) == ReportTypes.ShiftReadiness)
			{
				return View("UpcomingShiftReadinessReport", await UpcomingShiftReadinessReportModel(departmentId));
			}

			return new EmptyResult();
		}

		private async Task<StaffingReportView> CreateStaffingReportModel(int departmentId)
		{
			var model = new StaffingReportView();
			model.Rows = new List<StaffingReportRow>();
			//model.StaffingBreakdown = new StaffingBreakdown();
			model.GroupBreakdowns = new List<GroupBreakdown>();

			var users = await _departmentsService.GetAllUsersForDepartmentAsync(departmentId, false);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			model.RunOn = DateTime.UtcNow.TimeConverter(model.Department);
			model.CustomStaffing = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(departmentId);

			foreach (var user in users)
			{
				if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(user.UserId, UserId, DepartmentId))
					continue;

				var person = new StaffingReportRow();
				var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, DepartmentId);
				var staffing = await _userStateService.GetLastUserStateByUserIdAsync(user.UserId);
				var staffingChanges =
					await _scheduledTasksService.GetUpcomingScheduledTasksByUserIdTaskTypeAsync(user.UserId,
						TaskTypes.UserStaffingLevel);

				person.Name = await UserHelper.GetFullNameForUser(user.UserId);

				if (group != null)
					person.Group = group.Name;

				var sb = new StringBuilder();
				var roles = await _personnelRolesService.GetRolesForUserAsync(user.UserId, DepartmentId);
				foreach (var role in roles)
				{
					if (sb.Length > 0)
						sb.Append(", ");

					sb.Append(role.Name);
				}

				person.Roles = sb.ToString();
				person.Staffing = staffing.State;

				if (staffing.AutoGenerated)
					person.StaffingTimestamp = "Never";
				else
					person.StaffingTimestamp = staffing.Timestamp.FormatForDepartment(model.Department);

				person.StaffingNote = staffing.Note;

				GroupBreakdown groupBreakdown = model.GroupBreakdowns.FirstOrDefault(x => x.Name == person.Group);
				bool isNew = false;
				if (groupBreakdown == null)
				{
					isNew = true;
					groupBreakdown = new GroupBreakdown();
					groupBreakdown.Name = person.Group;
				}

				groupBreakdown.AddStaffing(model.GetStaffingName(person.Staffing));
				model.AddStaffing(person.Staffing);

				//if (person.Staffing <= 25)
				//{
				//	switch ((UserStateTypes)person.Staffing)
				//	{
				//		case UserStateTypes.Available:
				//			model.StaffingBreakdown.Available++;
				//			groupBreakdown.Available++;
				//			break;
				//		case UserStateTypes.Delayed:
				//			model.StaffingBreakdown.Delayed++;
				//			groupBreakdown.Delayed++;
				//			break;
				//		case UserStateTypes.Unavailable:
				//			model.StaffingBreakdown.Unavailable++;
				//			groupBreakdown.Unavailable++;
				//			break;
				//		case UserStateTypes.Committed:
				//			model.StaffingBreakdown.Committed++;
				//			groupBreakdown.Committed++;
				//			break;
				//		case UserStateTypes.OnShift:
				//			model.StaffingBreakdown.OnShift++;
				//			groupBreakdown.OnShift++;
				//			break;
				//	}
				//}
				//else
				//{
				//	//var customState = CustomStatesHelper.GetCustomState(DepartmentId, person.Staffing);

				//	//if (customState != null)
				//	//{
				//	//	state = customState.ButtonText;
				//	//	stateCss = "label-default";
				//	//	stateStyle = string.Format("color:{0};background-color:{1};", customState.TextColor, customState.ButtonColor);
				//	//}
				//	//else
				//	//{
				//	//	state = "Unknown";
				//	//	stateCss = "label-default";
				//	//}
				//}

				if (isNew)
					model.GroupBreakdowns.Add(groupBreakdown);

				if (staffingChanges != null && staffingChanges.Count > 0)
				{
					person.NextStaffing = int.Parse(staffingChanges.First().Data);
					var currentLocalTime = DateTime.UtcNow.TimeConverter(model.Department);
					var timestamp = staffingChanges.First().WhenShouldJobBeRun(currentLocalTime);

					if (timestamp < currentLocalTime)
						timestamp = currentLocalTime.AddMinutes(15);

					if (timestamp != null)
						person.NextStaffingTimestamp = timestamp.Value.FormatForDepartment(model.Department);
				}
				else
				{
					person.NextStaffing = -1;
					person.NextStaffingTimestamp = "";
				}

				model.Rows.Add(person);
			}

			return model;
		}

		private async Task<PersonnelReportView> CreatePersonnelReportModel(int departmentId)
		{
			var model = new PersonnelReportView();
			model.Rows = new List<PersonnelReportRow>();
			var users = await _departmentsService.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(departmentId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			model.RunOn = DateTime.UtcNow.TimeConverter(model.Department);

			foreach (var user in users)
			{
				var departmentUser = await _departmentsService.GetDepartmentMemberAsync(user.UserId, DepartmentId, false);

				if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(user.UserId, UserId, DepartmentId))
					continue;

				if (departmentUser != null)
				{
					var person = new PersonnelReportRow();
					var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, DepartmentId);
					var savedProfile = await _userProfileService.GetProfileByUserIdAsync(user.UserId);

					if (departmentUser.IsAdmin.HasValue && departmentUser.IsAdmin.Value ||
						model.Department.ManagingUserId == user.UserId)
						person.DepartmentRole = "Admin";
					else
						person.DepartmentRole = "Available";

					if (group != null)
						person.Group = group.Name;

					person.Email = user.Email;
					person.Username = user.UserName;

					var sb = new StringBuilder();
					var roles = await _personnelRolesService.GetRolesForUserAsync(user.UserId, DepartmentId);
					foreach (var role in roles)
					{
						if (sb.Length > 0)
							sb.Append(", ");

						sb.Append(role.Name);
					}

					person.Roles = sb.ToString();

					if (savedProfile != null)
					{
						person.Name = savedProfile.FullName.AsFirstNameLastName;
						person.ID = savedProfile.IdentificationNumber;
						person.MobilePhoneNumber = savedProfile.MobileNumber;

						if (savedProfile.MailingAddressId.HasValue)
						{
							var mailingAddress =
								await _addressService.GetAddressByIdAsync(savedProfile.MailingAddressId.Value);

							StringBuilder address = new StringBuilder();
							address.Append("<address>");
							address.Append(mailingAddress.Address1);
							address.Append("&nbsp<br>");
							address.Append(mailingAddress.City);
							address.Append(mailingAddress.State);
							address.Append(mailingAddress.PostalCode);
							address.Append("&nbsp<br>");
							address.Append(mailingAddress.Country);
							address.Append("&nbsp<br>");
							address.Append("</address>");

							person.MailingAddress = address.ToString();
						}
					}
					else
					{
						var userProfile = await _userProfileService.GetProfileByUserIdAsync(user.UserId);
						person.Name = userProfile.FullName.AsFirstNameLastName;
					}

					model.Rows.Add(person);
				}
			}

			return model;
		}

		private async Task<CertificationsReportView> CreateCertificationsReportModel(int departmentId)
		{
			var model = new CertificationsReportView();
			model.Rows = new List<CertificationsReportRow>();

			var users = await _departmentsService.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			model.RunOn = DateTime.UtcNow.TimeConverter(department);

			foreach (var user in users)
			{
				if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(user.UserId, UserId, DepartmentId))
					continue;

				var person = new CertificationsReportRow();
				person.SubRows = new List<CertificationsReportSubRow>();

				var certifications = await _certificationService.GetCertificationsByUserIdAsync(user.UserId);

				if (certifications != null && certifications.Count > 0)
				{
					var savedProfile = await _userProfileService.GetProfileByUserIdAsync(user.UserId);

					if (savedProfile != null)
					{
						person.Name = savedProfile.FullName.AsFirstNameLastName;
						person.ID = savedProfile.IdentificationNumber;
					}
					else
					{
						var userProfile = await _userProfileService.GetProfileByUserIdAsync(user.UserId);
						person.Name = userProfile.FullName.AsFirstNameLastName;
					}

					foreach (var certification in certifications)
					{
						var subRow = new CertificationsReportSubRow();
						subRow.Name = certification.Name;
						subRow.Number = certification.Number;
						subRow.Type = certification.Type;
						subRow.IssuedBy = certification.IssuedBy;

						if (certification.ExpiresOn.HasValue)
							subRow.ExpiresOn = certification.ExpiresOn.Value.ToShortDateString();

						subRow.Area = certification.Area;

						person.SubRows.Add(subRow);
					}


					model.Rows.Add(person);
				}
			}

			return model;
		}

		private async Task<LogReportView> CreateLogReportModel(int logId)
		{
			var model = new LogReportView();
			model.Log = await _logService.GetWorkLogByIdAsync(logId);

			var department = await _departmentsService.GetDepartmentByIdAsync(model.Log.DepartmentId);
			model.RunOn = DateTime.UtcNow.TimeConverter(department);


			foreach (var user in model.Log.Users)
			{
				if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(user.UserId, UserId, DepartmentId))
					continue;

				var profile = await _userProfileService.GetProfileByUserIdAsync(user.UserId);
				var station = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, DepartmentId);

				var stationName = "";

				if (station != null)
					stationName = station.Name;

				if (!model.UserData.ContainsKey(user.UserId))
					model.UserData.Add(user.UserId, new Tuple<string, UserProfile>(stationName, profile));
			}

			if (model.Log.StationGroup != null)
			{
				var stationMembers = model.UserData.Count(x => x.Value.Item1 == model.Log.StationGroup.Name);
				model.Attendance = (stationMembers * 100f) / model.Log.StationGroup.Members.Count;
			}
			else
			{
				var members = await _departmentsService.GetAllMembersForDepartmentAsync(DepartmentId);
				model.Attendance = (model.Log.Users.Count * 100f) / members.Count;
			}

			return model;
		}

		private async Task<UpcomingShiftReadinessView> UpcomingShiftReadinessReportModel(int departmentId)
		{
			var model = new UpcomingShiftReadinessView();
			model.Rows = new List<UpcomingShiftReadinessReportRow>();

			var shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			model.RunOn = DateTime.UtcNow.TimeConverter(department);

			foreach (var s1 in shifts)
			{
				var shift = await _shiftsService.PopulateShiftData(s1, true, true, true, true, true);

				if (shift != null && shift.Days != null && shift.Days.Count > 0)
				{
					bool readyShift = true;
					var shiftRow = new UpcomingShiftReadinessReportRow();
					var nextShiftDay = (from s in shift.Days
										where s.Day > DateTime.UtcNow.TimeConverter(department)
										orderby s.Day.Day descending
										select s).FirstOrDefault();

					if (nextShiftDay != null)
					{
						var shiftRoleDeltas = await _shiftsService.GetShiftDayNeedsAsync(nextShiftDay.ShiftDayId);

						shiftRow.ShiftName = shift.Name;
						shiftRow.ShiftDate = nextShiftDay.Day.ToShortDateString();
						shiftRow.Type = ((ShiftAssignmentTypes)shift.AssignmentType).ToString();

						if (shift.Groups != null && shift.Groups.Count > 0)
						{
							foreach (var group in shift.Groups)
							{
								var shiftSubRow = new UpcomingShiftReadinessReportSubRow();
								shiftSubRow.GroupName = group.DepartmentGroup.Name;

								if (group.Roles != null && group.Roles.Count > 0)
								{
									foreach (var role in group.Roles)
									{
										var subRowRoles = new UpcomingShiftReadinessGroupRole();
										subRowRoles.Name = role.Role.Name;
										subRowRoles.Required = role.Required;
										subRowRoles.Optional = role.Optional;

										if (shiftRoleDeltas != null && shiftRoleDeltas.ContainsKey(group.DepartmentGroupId))
										{
											var roleDelta = shiftRoleDeltas[group.DepartmentGroupId];

											if (roleDelta.ContainsKey(role.PersonnelRoleId))
												subRowRoles.Delta = roleDelta[role.PersonnelRoleId];

											if (subRowRoles.Delta > 0)
												readyShift = false;
										}
										else
										{
											subRowRoles.Delta = 0;
										}

										shiftSubRow.Roles.Add(subRowRoles);
									}
								}

								if (shift.Personnel != null && shift.Personnel.Count > 0)
								{
									foreach (var person in shift.Personnel)
									{
										var subRowPerson = new UpcomingShiftReadinessPersonnel();
										subRowPerson.Name = await UserHelper.GetFullNameForUser(person.UserId);
										subRowPerson.Roles =
											(await _personnelRolesService.GetRolesForUserAsync(person.UserId, DepartmentId))
											.Select(x => x.Name).ToList();

										shiftSubRow.Personnel.Add(subRowPerson);
									}
								}

								var shiftSignupsForDay =
									await _shiftsService.GetShiftSignpsForShiftDayAsync(nextShiftDay.ShiftDayId);
								if (shiftSignupsForDay != null)
								{
									foreach (var signup in shiftSignupsForDay.Where(x =>
												 x.DepartmentGroupId.Value == group.DepartmentGroupId))
									{
										var subRowPerson = new UpcomingShiftReadinessPersonnel();
										subRowPerson.Name = await UserHelper.GetFullNameForUser(signup.UserId);
										subRowPerson.Roles =
											(await _personnelRolesService.GetRolesForUserAsync(signup.UserId, DepartmentId))
											.Select(x => x.Name).ToList();

										shiftSubRow.Personnel.Add(subRowPerson);
									}
								}

								shiftRow.SubRows.Add(shiftSubRow);
							}
						}

						if (shift.AssignmentType == (int)ShiftAssignmentTypes.Assigned)
							readyShift = true;

						shiftRow.Ready = readyShift;
						model.Rows.Add(shiftRow);
					}
				}
			}

			return model;
		}

		private async Task<DepartmentActivityView> DepartmentActivityReportModel(int departmentId)
		{
			var model = new DepartmentActivityView();

			model.Department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			model.RunOn = DateTime.UtcNow.TimeConverter(model.Department);

			var calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(DepartmentId,
				new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1),
				new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59));
			var logs =
				await _workLogsService.GetAllLogsByDepartmentDateRangeAsync(DepartmentId, LogTypes.Training,
					new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1),
					new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59));

			model.TotalCalls = calls.Count;
			model.CallTypeCount = new List<Tuple<string, int>>();
			model.TrainingMonthCount = new List<Tuple<int, int>>();
			model.Trainings = new List<PersonnelTraining>();

			var groupedCalls = calls.GroupBy(x => x.Type);
			foreach (var grouppedCall in groupedCalls)
			{
				string key = "No Type";
				if (!String.IsNullOrWhiteSpace(grouppedCall.Key))
					key = grouppedCall.Key;

				model.CallTypeCount.Add(new Tuple<string, int>(key, grouppedCall.ToList().Count));
			}

			model.CallTypeCount.Add(new Tuple<string, int>("Total", calls.Count));

			model.Responses = new List<PersonnelResponse>();
			var personnel = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId);

			CultureInfo culture = new CultureInfo("en-us");
			Calendar calendar = culture.Calendar;

			for (int i = 1; i <= 12; i++)
			{
				var monthLogs =
					logs.Where(x => calendar.GetMonth(x.LoggedOn) == i).GroupBy(x => x.Course);

				foreach (var monthLog in monthLogs)
				{
					model.TrainingMonthCount.Add(new Tuple<int, int>(i, monthLog.ToList().Count));
				}
			}

			foreach (var person in personnel)
			{
				if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(person.UserId, UserId, DepartmentId))
					continue;

				var response = new PersonnelResponse();
				var training = new PersonnelTraining();

				var profile = profiles[person.UserId];

				if (profile != null)
				{
					var group = await _departmentGroupsService.GetGroupForUserAsync(profile.UserId, DepartmentId);

					response.ID = profile.IdentificationNumber;
					training.ID = profile.IdentificationNumber;
					response.Name = profile.FullName.AsFirstNameLastName;
					training.Name = profile.FullName.AsFirstNameLastName;

					if (group != null)
					{
						response.Group = group.Name;
						training.Group = group.Name;

						var totalTrainings = logs.Where(x =>
							x.StationGroup == null || x.StationGroupId == group.DepartmentGroupId);
						training.Total = totalTrainings.Count();
					}
					else
					{
						var totalTrainings = logs.Where(x => x.StationGroup == null);
						training.Total = totalTrainings.Count();
					}

					var tonedCalls = calls.Where(x => x.Dispatches.Select(y => y.UserId).Contains(person.UserId));
					response.TotalCalls = tonedCalls.Count();

					var attendedTrainings = logs.Where(x =>
						x.Users != null && x.Users.Select(y => y.UserId).Contains(person.UserId));
					training.Attended = attendedTrainings.Count();

					var actionLogs = await _actionLogsService.GetAllActionLogsForUserInDateRangeAsync(person.UserId,
						new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1),
						new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59));

					response.CallTypeCount = new List<Tuple<string, int>>();
					foreach (var call in tonedCalls.GroupBy(x => x.Type))
					{
						var count = 0;

						foreach (var tonnedCall in call.ToList())
						{
							var callLog = await _logService.GetLogsForCallAsync(tonnedCall.CallId);
							if (callLog != null && callLog.Any())
							{
								var userLogs = callLog.Where(x =>
									x.Users != null && x.Users.Select(y => y.UserId).Contains(person.UserId));
								if (userLogs != null && userLogs.Any())
								{
									count += userLogs.Count();
								}
							}

							var callActionLogs = actionLogs.Where(x => x.DestinationId == tonnedCall.CallId);

							if (callActionLogs != null && callActionLogs.Any())
							{
								count += callActionLogs.Count();
							}
						}

						string key = "No Type";
						if (!String.IsNullOrWhiteSpace(call.Key))
							key = call.Key;

						response.CallTypeCount.Add(new Tuple<string, int>(key, count));
					}

					model.Responses.Add(response);
					model.Trainings.Add(training);
				}
			}

			return model;
		}

		private async Task<PersonnelHoursView> PersonnelHoursReportModel(int departmentId, DateTime? start,
			DateTime? end)
		{
			var model = new PersonnelHoursView();

			model.Department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			model.RunOn = DateTime.UtcNow.TimeConverter(model.Department);
			List<Call> calls = new List<Call>();

			if (start.HasValue && end.HasValue)
			{
				model.Start = start.Value;
				model.End = end.Value;

				calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, start.Value, end.Value);
			}
			else
			{
				model.Start = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1);
				model.End = new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59);

				calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, model.Start, model.End);
			}

			var workLogs =
				await _workLogsService.GetAllLogsByDepartmentDateRangeAsync(DepartmentId, LogTypes.Work, model.Start,
					model.End);
			var trainingLogs =
				await _workLogsService.GetAllLogsByDepartmentDateRangeAsync(DepartmentId, LogTypes.Training,
					model.Start, model.End);
			var callLogs =
				await _workLogsService.GetAllLogsByDepartmentDateRangeAsync(DepartmentId, LogTypes.Run, model.Start,
					model.End);

			model.CallsHours = new List<PersonnelCallHours>();
			model.WorkHours = new List<PersonnelWorkHours>();
			model.TrainingHours = new List<PersonnelTrainingHours>();

			var personnel = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId);

			foreach (var person in personnel)
			{
				if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(person.UserId, UserId, DepartmentId))
					continue;

				var callHours = new PersonnelCallHours();
				var workHours = new PersonnelWorkHours();
				var trainingHours = new PersonnelTrainingHours();

				var profile = profiles[person.UserId];

				if (profile != null)
				{
					var group = await _departmentGroupsService.GetGroupForUserAsync(profile.UserId, DepartmentId);

					callHours.ID = profile.IdentificationNumber;
					workHours.ID = profile.IdentificationNumber;
					trainingHours.ID = profile.IdentificationNumber;

					callHours.Name = profile.FullName.AsFirstNameLastName;
					workHours.Name = profile.FullName.AsFirstNameLastName;
					trainingHours.Name = profile.FullName.AsFirstNameLastName;

					if (group != null)
					{
						callHours.Group = group.Name;
						workHours.Group = group.Name;
						trainingHours.Group = group.Name;
					}

					var totalCallLogs = callLogs.Where(x => x.Users.Select(y => y.UserId).Contains(person.UserId));
					callHours.TotalCalls = totalCallLogs.Count();

					double callTotalHours = 0;
					foreach (var log in totalCallLogs)
					{
						var userLog = log.Users.FirstOrDefault(x => x.UserId == person.UserId);
						if (log.StartedOn.HasValue && log.EndedOn.HasValue)
						{
							callTotalHours += (log.EndedOn.Value - log.StartedOn.Value).TotalSeconds;
						}
						else if (userLog != null && userLog.UnitId.HasValue)
						{
							var unit = log.Units.FirstOrDefault(x => x.UnitId == userLog.UnitId.Value);

							if (unit != null && unit.Dispatched.HasValue && unit.InQuarters.HasValue)
								callTotalHours += (unit.InQuarters.Value - unit.Dispatched.Value).TotalSeconds;
						}
						else
						{
							var call = calls.FirstOrDefault(x => x.CallId == log.CallId);

							if (call != null && call.ClosedOn.HasValue)
							{
								callTotalHours += (call.ClosedOn.Value - call.LoggedOn).TotalSeconds;
							}
						}
					}

					callHours.TotalSeconds += callTotalHours;

					var totalWorkLogs = workLogs.Where(x => x.Users.Select(y => y.UserId).Contains(person.UserId));
					workHours.TotalWorkLogs = totalWorkLogs.Count();
					workHours.TotalSeconds = totalWorkLogs.Where(log => log.StartedOn.HasValue && log.EndedOn.HasValue)
						.Sum(log => (log.EndedOn.Value - log.StartedOn.Value).TotalSeconds);

					var totalTrainingLogs =
						trainingLogs.Where(x => x.Users.Select(y => y.UserId).Contains(person.UserId));
					trainingHours.TotalTrainings = totalTrainingLogs.Count();
					trainingHours.TotalSeconds = totalTrainingLogs
						.Where(log => log.StartedOn.HasValue && log.EndedOn.HasValue).Sum(log =>
							(log.EndedOn.Value - log.StartedOn.Value).TotalSeconds);

					model.CallsHours.Add(callHours);
					model.WorkHours.Add(workHours);
					model.TrainingHours.Add(trainingHours);
				}
			}

			return model;
		}

		private async Task<PersonnelHoursDetailView> PersonnelHoursDetailReportModel(int departmentId, string userId,
			DateTime? start, DateTime? end)
		{
			var model = new PersonnelHoursDetailView();

			model.Department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			model.RunOn = DateTime.UtcNow.TimeConverter(model.Department);
			List<Call> calls = new List<Call>();

			if (start.HasValue && end.HasValue)
			{
				model.Start = start.Value;
				model.End = end.Value;

				calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, start.Value, end.Value);
			}
			else
			{
				model.Start = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1);
				model.End = new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59);

				calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, model.Start, model.End);
			}

			var workLogs =
				await _workLogsService.GetAllLogsByDepartmentDateRangeAsync(DepartmentId, LogTypes.Work,
					model.Start, model.End);

			var trainingLogs =
				await _workLogsService.GetAllLogsByDepartmentDateRangeAsync(DepartmentId, LogTypes.Training,
					model.Start, model.End);

			var callLogs =
				await _workLogsService.GetAllLogsByDepartmentDateRangeAsync(DepartmentId, LogTypes.Run,
					model.Start, model.End);

			model.CallDetails = new List<CallDetail>();
			model.WorkDetails = new List<WorkDetail>();
			model.TrainingDetails = new List<TrainingDetail>();

			var profile = await _userProfileService.GetProfileByUserIdAsync(userId);
			model.ID = profile.IdentificationNumber;
			model.Name = profile.FullName.AsFirstNameLastName;

			var group = await _departmentGroupsService.GetGroupForUserAsync(profile.UserId, DepartmentId);

			if (group != null)
				model.Group = group.Name;

			var totalCallLogs = callLogs.Where(x => x.Users.Select(y => y.UserId).Contains(userId));
			foreach (var log in totalCallLogs)
			{
				var callDetail = new CallDetail();
				callDetail.CallNumber = log.Call.Number;
				callDetail.Name = log.Call.Name;

				bool valueSet = false;
				var userLog = log.Users.FirstOrDefault(x => x.UserId == userId);
				if (log.StartedOn.HasValue && log.EndedOn.HasValue)
				{
					callDetail.Start = log.StartedOn.Value;
					callDetail.End = log.EndedOn.Value;

					valueSet = true;
				}
				else if (userLog != null && userLog.UnitId.HasValue)
				{
					var unit = log.Units.FirstOrDefault(x => x.UnitId == userLog.UnitId.Value);

					if (unit != null && unit.Dispatched.HasValue && unit.InQuarters.HasValue)
					{
						callDetail.Start = unit.Dispatched.Value;
						callDetail.End = unit.InQuarters.Value;

						valueSet = true;
					}
				}
				else
				{
					var call = calls.FirstOrDefault(x => x.CallId == log.CallId);

					if (call != null && call.ClosedOn.HasValue)
					{
						callDetail.Start = call.LoggedOn;
						callDetail.End = call.ClosedOn.Value;

						valueSet = true;
					}
				}

				if (valueSet)
					model.CallDetails.Add(callDetail);
			}

			var totalWorkLogs = workLogs.Where(x => x.Users.Select(y => y.UserId).Contains(userId));
			foreach (var log in totalWorkLogs)
			{
				var workDetail = new WorkDetail();

				if (log.StartedOn.HasValue && log.EndedOn.HasValue)
				{
					var loggedByProfile = await _userProfileService.GetProfileByUserIdAsync(log.LoggedByUserId);

					workDetail.Name = string.Format("{0}-{1}", log.LoggedOn.ToString("G"),
						loggedByProfile.FullName.AsFirstNameLastName);
					workDetail.Start = log.StartedOn.Value;
					workDetail.End = log.EndedOn.Value;

					model.WorkDetails.Add(workDetail);
				}
			}

			var totalTrainingLogs = trainingLogs.Where(x => x.Users.Select(y => y.UserId).Contains(userId));
			foreach (var log in totalTrainingLogs)
			{
				var trainingDetail = new TrainingDetail();

				if (log.StartedOn.HasValue && log.EndedOn.HasValue)
				{
					trainingDetail.Name = log.Course;
					trainingDetail.Start = log.StartedOn.Value;
					trainingDetail.End = log.EndedOn.Value;

					model.TrainingDetails.Add(trainingDetail);
				}
			}

			return model;
		}

		private async Task<PersonnelStaffingHistoryView> PersonnelStaffingHistoryReportModel(int departmentId,
			bool groupSelect, int groupId, string userId, DateTime? start, DateTime? end)
		{
			var model = new PersonnelStaffingHistoryView();

			model.Department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			model.RunOn = DateTime.UtcNow.TimeConverter(model.Department);

			if (start.HasValue && end.HasValue)
			{
				model.Start = start.Value;
				model.End = end.Value;
			}
			else
			{
				model.Start = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1);
				model.End = new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59);
			}

			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(departmentId);
			var allStates =
				await _userStateService.GetAllStatesForDepartmentInDateRangeAsync(departmentId, model.Start, model.End);
			var groups = await _departmentGroupsService.GetAllDepartmentGroupsForDepartmentAsync(departmentId);

			var states = new List<UserState>();

			if (groupSelect && groupId > 0)
			{
				var group = await _departmentGroupsService.GetGroupByIdAsync(groupId);
				var usersInGroup = group.Members.Select(x => x.UserId);

				states.AddRange(allStates.Where(x => usersInGroup.Contains(x.UserId)));
			}
			else
			{
				if (!String.IsNullOrWhiteSpace(userId))
				{
					states.AddRange(allStates.Where(x => x.UserId == userId));
				}
				else
				{
					states.AddRange(allStates);
				}
			}

			var groupedStates = from s in states
				group s by s.UserId
				into g
				select new { UserId = g.Key, States = g.ToList() };

			foreach (var group in groupedStates)
			{
				if (profiles.ContainsKey(group.UserId))
				{
					if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(group.UserId, UserId, DepartmentId))
						continue;

					var summary = new PersonnelStaffingSummary();
					var profile = profiles[group.UserId];

					summary.ID = profile.IdentificationNumber;
					summary.Name = profile.FullName.AsFirstNameLastName;
					summary.TotalStaffingChanges = group.States.Count;

					var scheduledTasks =
						await _scheduledTasksService.GetScheduledTasksByUserTypeAsync(group.UserId,
							(int)TaskTypes.UserStaffingLevel);
					if (scheduledTasks != null)
						summary.TotalActiveScheduledChanges = scheduledTasks.Count;
					else
						summary.TotalActiveScheduledChanges = 0;

					if (groups.ContainsKey(group.UserId))
					{
						var dg = groups[group.UserId];
						summary.Group = dg.Name;
					}

					foreach (var state in group.States)
					{
						var detail = new PersonnelStaffingDetail();
						detail.Timestamp = state.Timestamp.TimeConverterToString(model.Department);
						detail.Note = state.Note;

						var customState = await CustomStatesHelper.GetCustomPersonnelStaffing(departmentId, state);
						detail.State = customState.ButtonText;
						detail.StateColor = customState.ButtonColor;

						summary.Details.Add(detail);
					}

					model.Personnel.Add(summary);
				}
			}

			return model;
		}

		private async Task<CallSummaryView> CallSummaryReportModel(int departmentId, DateTime? start, DateTime? end)
		{
			var model = new CallSummaryView();

			model.Department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			model.RunOn = DateTime.UtcNow.TimeConverter(model.Department);

			if (start.HasValue && end.HasValue)
			{
				model.Start = start.Value;
				model.End = end.Value;
			}
			else
			{
				model.Start = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1);
				model.End = new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59);
			}

			var calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, start.Value, end.Value);
			var logs = await _logService.GetAllLogsByDepartmentDateRangeAsync(departmentId, LogTypes.Run, model.Start,
				model.End);

			model.TotalCalls = calls.Count;
			model.CallTypeCount = new List<Tuple<string, int>>();
			model.CallCloseCount = new List<Tuple<string, int>>();
			model.CallSummaries = new List<CallSummary>();

			var groupedCalls = calls.GroupBy(x => x.Type);
			foreach (var grouppedCall in groupedCalls)
			{
				string key = "No Type";
				if (!String.IsNullOrWhiteSpace(grouppedCall.Key))
					key = grouppedCall.Key;

				model.CallTypeCount.Add(new Tuple<string, int>(key, grouppedCall.ToList().Count));
			}

			model.CallTypeCount.Add(new Tuple<string, int>("Total", calls.Count));

			var groupedCallStates = calls.GroupBy(x => x.State);
			foreach (var grouppedCall in groupedCallStates)
			{
				model.CallCloseCount.Add(new Tuple<string, int>(((CallStates)grouppedCall.Key).ToString(),
					grouppedCall.ToList().Count));
			}

			model.CallCloseCount.Add(new Tuple<string, int>("Total", calls.Count));

			foreach (var call in calls)
			{
				var summary = new CallSummary();
				summary.Number = call.Number;
				summary.Name = call.Name;
				summary.LoggedOn = call.LoggedOn;
				summary.ClosedOn = call.ClosedOn;
				summary.Type = call.Type;

				DateTime? onSceneTime = null;
				var callLogs = logs.Where(x => x.CallId == call.CallId);

				if (callLogs != null && callLogs.Any())
				{
					foreach (var callLog in callLogs)
					{
						if (callLog.Units != null && callLog.Units.Any())
						{
							summary.UnitsCount += callLog.Units.Count();

							foreach (var unit in callLog.Units)
							{
								if (unit.OnScene.HasValue)
								{
									if (onSceneTime.HasValue == false || onSceneTime.Value > unit.OnScene.Value)
										onSceneTime = unit.OnScene;
								}
							}
						}

						if (callLog.Users != null && callLog.Users.Any())
							summary.PersonnelCount += callLog.Users.Count();
					}

					summary.FirstOnSceneTime = onSceneTime;
				}
				else
				{
					summary.FirstOnSceneTime = null;
					summary.UnitsCount = 0;
					summary.PersonnelCount = 0;
				}

				model.CallSummaries.Add(summary);
			}

			return model;
		}

		private async Task<PersonnelStatusHistoryView> PersonnelStatusHistoryReportModel(int departmentId,
			bool groupSelect, int groupId, string userId, DateTime? start, DateTime? end)
		{
			var model = new PersonnelStatusHistoryView();

			model.Department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			model.RunOn = DateTime.UtcNow.TimeConverter(model.Department);

			if (start.HasValue && end.HasValue)
			{
				model.Start = start.Value;
				model.End = end.Value;
			}
			else
			{
				model.Start = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1);
				model.End = new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59);
			}

			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(departmentId);
			var groups = await _departmentGroupsService.GetAllDepartmentGroupsForDepartmentAsync(departmentId);

			var statuses = new List<ActionLog>();

			if (groupSelect && groupId > 0)
			{
				var group = await _departmentGroupsService.GetGroupByIdAsync(groupId);
				var usersInGroup = group.Members.Select(x => x.UserId);

				foreach (var user in usersInGroup)
				{
					statuses.AddRange(
						await _actionLogsService.GetAllActionLogsForUserInDateRangeAsync(user, model.Start, model.End));
				}
			}
			else
			{
				if (!String.IsNullOrWhiteSpace(userId))
				{
					statuses.AddRange(
						await _actionLogsService.GetAllActionLogsForUserInDateRangeAsync(userId, model.Start,
							model.End));
				}
				else
				{
					statuses.AddRange(
						await _actionLogsService.GetAllActionLogsInDateRangeAsync(DepartmentId, model.Start,
							model.End));
				}
			}

			var groupedStates = from s in statuses
				group s by s.UserId
				into g
				select new { UserId = g.Key, States = g.ToList() };

			foreach (var group in groupedStates)
			{
				if (profiles.ContainsKey(group.UserId))
				{
					var summary = new PersonnelStatusSummary();
					var profile = profiles[group.UserId];

					summary.ID = profile.IdentificationNumber;
					summary.Name = profile.FullName.AsFirstNameLastName;
					summary.TotalStaffingChanges = group.States.Count;

					if (groups.ContainsKey(group.UserId))
					{
						var dg = groups[group.UserId];
						summary.Group = dg.Name;
					}

					foreach (var state in group.States)
					{
						var detail = new PersonnelStatusDetail();
						detail.Timestamp = state.Timestamp.TimeConverterToString(model.Department);
						detail.Note = state.Note;

						var customState = await CustomStatesHelper.GetCustomPersonnelStatus(departmentId, state);

						if (customState != null)
						{
							detail.Status = customState.ButtonText;
							detail.StatusColor = customState.ButtonColor;
						}
						else
						{
							detail.Status = "Unknown";
							detail.StatusColor = "#FFF";
						}

						summary.Details.Add(detail);
					}

					model.Personnel.Add(summary);
				}
			}

			return model;
		}

		private async Task<UnitStateHistoryView> UnitStateHistoryReportModel(int departmentId, bool groupSelect,
			int groupId, int unitId, DateTime? start, DateTime? end)
		{
			var model = new UnitStateHistoryView();

			model.Department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			model.RunOn = DateTime.UtcNow.TimeConverter(model.Department);

			if (start.HasValue && end.HasValue)
			{
				model.Start = start.Value;
				model.End = end.Value;
			}
			else
			{
				model.Start = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1);
				model.End = new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59);
			}

			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(departmentId);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(departmentId);

			var statuses = new List<UnitState>();

			if (groupSelect && groupId > 0)
			{
				var group = await _departmentGroupsService.GetGroupByIdAsync(groupId);
				var unitsInGroup = await _unitsService.GetAllUnitsForGroupAsync(groupId);

				foreach (var unit in unitsInGroup)
				{
					var groupUnitStates = await _unitStatesService.GetAllStatesForUnitInDateRangeAsync(unit.UnitId, model.Start, model.End);

					if (groupUnitStates != null)
						statuses.AddRange(groupUnitStates);
				}
			}
			else
			{
				if (unitId > 0)
				{
					var perUnitStates = await _unitStatesService.GetAllStatesForUnitInDateRangeAsync(unitId, model.Start, model.End);

					if (perUnitStates != null)
						statuses.AddRange(perUnitStates);
				}
				else
				{
					foreach (var unit in units)
					{
						var unitStates = await _unitStatesService.GetAllStatesForUnitInDateRangeAsync(unit.UnitId, model.Start, model.End);

						if (unitStates != null)
							statuses.AddRange(unitStates);
					}
				}
			}

			var groupedStates = from s in statuses
				group s by s.UnitId
				into g
				select new { UnitId = g.Key, Unit = units.FirstOrDefault(x => x.UnitId == g.Key), States = g.ToList() };

			foreach (var group in groupedStates)
			{
				var summary = new UnitStateSummary();
				summary.Name = group.Unit?.Name;
				summary.TotalStatusChanges = group.States.Count;

				if (group.Unit?.StationGroupId.HasValue == true)
				{
					var groupInfo = groups.FirstOrDefault(x => x.DepartmentGroupId == group.Unit?.StationGroupId);
					if (groupInfo != null)
					{
						summary.Group = groupInfo.Name;
					}
				}

				foreach (var state in group.States)
				{
					var detail = new UnitStateDetail();
					detail.Timestamp = state.Timestamp.TimeConverterToString(model.Department);
					detail.Note = state.Note;

					var customState = await CustomStatesHelper.GetCustomUnitState(state);

					if (customState != null)
					{
						detail.State = customState.ButtonText;
						detail.StateColor = customState.ButtonColor;
					}
					else
					{
						detail.State = "Unknown";
						detail.StateColor = "#FFF";
					}

					summary.Details.Add(detail);
				}

				model.Units.Add(summary);
			}

			return model;
		}

		private async Task<OpenCallResourceView> ActiveCallsResourcesReportModel(int departmentId)
		{
			var model = new OpenCallResourceView();

			model.Department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			model.RunOn = DateTime.UtcNow.TimeConverter(model.Department);

			var calls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var personnel = await _usersService.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, false, false, false);
			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var activeRoles = await _unitsService.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId);
			
			foreach (var call in calls)
			{
				var summary = new OpenCallResource();
				summary.Number = call.Number;
				summary.Name = call.Name;
				summary.LoggedOn = call.LoggedOn.TimeConverter(model.Department);
				summary.Type = call.Type;

				var callData = await _callsService.PopulateCallData(call, true, false, false, true, true, true, false, false, false);

				if (callData != null)
				{
					if (callData.Dispatches != null && callData.Dispatches.Any())
					{
						foreach (var dispatch in callData.Dispatches)
						{
							var person = personnel.FirstOrDefault(x => x.UserId == dispatch.UserId);

							if (person != null)
							{
								var personResource = new OpenCallResourcePerson();
								personResource.Name = person.Name;
								personResource.Roles = person.RoleNames;
								personResource.GroupName = person.DepartmentGroupName;
								personResource.DispatchedOn = dispatch.DispatchedOn.TimeConverter(model.Department);

								summary.Personnel.Add(personResource);
							}
						}
					}

					if (callData.UnitDispatches != null && callData.UnitDispatches.Any())
					{
						foreach (var disaptch in callData.UnitDispatches)
						{
							var unit = units.FirstOrDefault(x => x.UnitId == disaptch.UnitId);

							if (unit != null)
							{
								var unitResource = new OpenCallResourceUnit();
								unitResource.UnitName = unit.Name;
								unitResource.DispatchedOn = disaptch.DispatchedOn.TimeConverter(model.Department);

								if (unit.Roles != null && unit.Roles.Any())
								{
									foreach (var role in unit.Roles)
									{
										var activeRole = activeRoles.FirstOrDefault(x => x.Role == role.Name);

										if (activeRole != null)
										{
											var activeRolePerson = personnel.FirstOrDefault(x => x.UserId == activeRole.UserId);

											if (activeRolePerson != null)
											{
												var roleUnitPerson = new OpenCallResourcePerson();
												roleUnitPerson.Name = activeRolePerson.Name;
												roleUnitPerson.Roles = activeRolePerson.RoleNames;
												roleUnitPerson.GroupName = activeRolePerson.DepartmentGroupName;

												unitResource.Roles.Add(role.Name, roleUnitPerson);
											}
										}
									}
								}

								summary.Units.Add(unitResource);
							}
						}
					}

					model.Calls.Add(summary);
				}
			}

			return model;
		}
	}
}
