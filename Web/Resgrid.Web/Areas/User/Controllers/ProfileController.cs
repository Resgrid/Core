using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Framework;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.Profile;
using Resgrid.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Threading;
using Microsoft.Extensions.Options;
using Resgrid.Web.Options;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Resgrid.Web.Areas.User.Models.Personnel;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using SixLabors.ImageSharp.Formats;
using Microsoft.Extensions.Localization;
using Resgrid.Model.Security;
using Newtonsoft.Json;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Web.Attributes;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class ProfileController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly IUserProfileService _userProfileService;
		private readonly IScheduledTasksService _scheduledTasksService;
		private readonly ICertificationService _certificationService;
		private readonly ICustomStateService _customStateService;
		private readonly IImageService _imageService;
		private readonly IOptions<AppOptions> _appOptionsAccessor;
		private readonly IEmailService _emailService;
		private readonly UserManager<IdentityUser> _userManager;
		private readonly SignInManager<IdentityUser> _signInManager;
		private readonly IDepartmentSsoService _departmentSsoService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security> _secLocalizer;
		private readonly IDeleteService _deleteService;
		private readonly IExternalIdentityLinkService _externalIdentityLinkService;
		private readonly IUserSessionService _userSessionService;
		private readonly ISystemAuditsService _systemAuditsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IPasswordRecoveryService _passwordRecoveryService;
		private readonly IEventAggregator _eventAggregator;

		public ProfileController(IDepartmentsService departmentsService, IUsersService usersService, Model.Services.IAuthorizationService authorizationService,
			IUserProfileService userProfileService, IScheduledTasksService scheduledTasksService, ICertificationService certificationService,
			ICustomStateService customStateService, IImageService imageService, IOptions<AppOptions> appOptionsAccessor,
			IEmailService emailService, UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager,
			IDepartmentSsoService departmentSsoService,
			IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security> secLocalizer, IDeleteService deleteService,
			IExternalIdentityLinkService externalIdentityLinkService, IUserSessionService userSessionService,
			ISystemAuditsService systemAuditsService, IDepartmentGroupsService departmentGroupsService,
			IDepartmentSettingsService departmentSettingsService, IPasswordRecoveryService passwordRecoveryService,
			IEventAggregator eventAggregator)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_authorizationService = authorizationService;
			_userProfileService = userProfileService;
			_scheduledTasksService = scheduledTasksService;
			_certificationService = certificationService;
			_customStateService = customStateService;
			_imageService = imageService;
			_appOptionsAccessor = appOptionsAccessor;
			_emailService = emailService;
			_userManager = userManager;
			_signInManager = signInManager;
			_departmentSsoService = departmentSsoService;
			_secLocalizer = secLocalizer;
			_deleteService = deleteService;
			_externalIdentityLinkService = externalIdentityLinkService;
			_userSessionService = userSessionService;
			_systemAuditsService = systemAuditsService;
			_departmentGroupsService = departmentGroupsService;
			_departmentSettingsService = departmentSettingsService;
			_passwordRecoveryService = passwordRecoveryService;
			_eventAggregator = eventAggregator;
		}
		#endregion Private Members and Constructors

		/// <summary>Resolves a password-error key returned by ValidatePasswordAgainstPolicyAsync into a localised message.</summary>
		private string ResolvePwdError(string key)
		{
			if (string.IsNullOrEmpty(key)) return key;
			// PwdErrorTooShort carries the min-length as "PwdErrorTooShort:N"
			if (key.StartsWith("PwdErrorTooShort:", StringComparison.Ordinal))
			{
				var minLen = key.Substring("PwdErrorTooShort:".Length);
				return string.Format(_secLocalizer["PwdErrorTooShort"], minLen);
			}
			return _secLocalizer[key];
		}

		#region Reporting
		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_View)]
		public async Task<IActionResult>  Reporting()
		{
			var model = new ReportingView();
			model.Department= await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			model.User= _usersService.GetUserById(UserId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_View)]
		public async Task<IActionResult>  GetScheduledReportingForGrid()
		{
			var scheduleJson = new List<ScheduledTasksForJson>();

			var dep= await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			var tasks= await _scheduledTasksService.GetScheduledReportingTasksForUserAsync(UserId);

			foreach (var task in tasks)
			{
				ScheduledTasksForJson st = new ScheduledTasksForJson();
				st.ScheduleId = task.ScheduledTaskId;
				if (task.ScheduleType == (int)ScheduleTypes.Weekly)
					st.ScheduleType = "Weekly";
				else
					st.ScheduleType = "Specific Date";

				if (task.SpecifcDate.HasValue)
					st.SpecifcDate = TimeConverterHelper.TimeConverter(task.SpecifcDate.Value, dep);

				st.IsActive = task.Active;

				StringBuilder days = new StringBuilder();

				if (task.Monday)
					days.Append("M");

				if (task.Tuesday)
					days.Append("T");

				if (task.Wednesday)
					days.Append("W");

				if (task.Thursday)
					days.Append("Th");

				if (task.Friday)
					days.Append("F");

				if (task.Saturday)
					days.Append("S");

				if (task.Sunday)
					days.Append("Su");

				days.Append(" @ " + task.Time);

				if (task.ScheduleType == (int)ScheduleTypes.Weekly)
					st.DaysOfWeek = days.ToString();

				st.Data = ((ReportTypes)int.Parse(task.Data)).ToString();

				scheduleJson.Add(st);
			}

			return Json(scheduleJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_View)]
		public async Task<IActionResult>  AddNewScheduledReport()
		{
			var model = new NewScheduledReportView();
			model.ReportTypes = model.ReportType.ToSelectList();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult>  AddNewScheduledReport(NewScheduledReportView model, CancellationToken cancellationToken)
		{
			model.ReportTypes = model.ReportType.ToSelectList();

			if (!model.SpecificDatetime)
			{
				int daysCount = 0;

				if (model.Sunday)
					daysCount++;

				if (model.Monday)
					daysCount++;

				if (model.Tuesday)
					daysCount++;

				if (model.Wednesday)
					daysCount++;

				if (model.Thursday)
					daysCount++;

				if (model.Friday)
					daysCount++;

				if (model.Saturday)
					daysCount++;

				if (daysCount == 0)
					ModelState.AddModelError("", "You need to select at least one day for a days of the week schedule.");

				if (String.IsNullOrWhiteSpace(model.Time))
					ModelState.AddModelError("", "You need to supply a time of day that the report will run.");
			}
			else
			{
				if (String.IsNullOrWhiteSpace(model.SpecifcDate))
					ModelState.AddModelError("", "You need to supply a date and time that the report will run.");
			}

			if (ModelState.IsValid)
			{
				ScheduledTask task = new ScheduledTask();
				task.UserId = UserId;

				if (model.SpecificDatetime)
				{
					task.ScheduleType = (int)ScheduleTypes.SpecifcDateTime;
					task.SpecifcDate = DateTime.Parse(model.SpecifcDate).ToUniversalTime();
				}
				else
				{
					task.ScheduleType = (int)ScheduleTypes.Weekly;
					task.Time = model.Time;
				}

				task.Sunday = model.Sunday;
				task.Monday = model.Monday;
				task.Tuesday = model.Tuesday;
				task.Wednesday = model.Wednesday;
				task.Thursday = model.Thursday;
				task.Friday = model.Friday;
				task.Saturday = model.Saturday;
				task.AddedOn = DateTime.UtcNow;
				task.Active = true;
				task.Data = ((int)model.ReportType).ToString();
				task.TaskType = (int)TaskTypes.ReportDelivery;
				task.DepartmentId = DepartmentId;

				await _scheduledTasksService.SaveScheduledTaskAsync(task, cancellationToken);

				ViewBag.EditProfile_TabProfileActiveCSS = "";
				ViewBag.EditProfile_TabScheduleActiveCSS = "active";

				return RedirectToAction("Reporting", "Profile", new { area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		public async Task<IActionResult>  EditScheduledReport(int scheduleId)
		{
			var model = new EditScheduledReportView();
			model.ReportTypes = model.ReportType.ToSelectList();

			var schedule= await _scheduledTasksService.GetScheduledTaskByIdAsync(scheduleId);
			if (schedule.ScheduleType == (int)ScheduleTypes.SpecifcDateTime)
			{
				model.SpecificDatetime = true;
				model.SpecifcDate = schedule.SpecifcDate.Value.ToString("g");
			}
			else
			{
				model.Time = schedule.Time;
			}

			model.ScheduleId = scheduleId;
			model.Sunday = schedule.Sunday;
			model.Monday = schedule.Monday;
			model.Tuesday = schedule.Tuesday;
			model.Wednesday = schedule.Wednesday;
			model.Thursday = schedule.Thursday;
			model.Friday = schedule.Friday;
			model.Saturday = schedule.Saturday;
			model.ReportType = (ReportTypes)int.Parse(schedule.Data);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		public async Task<IActionResult>  EditScheduledReport(EditScheduledReportView model, CancellationToken cancellationToken)
		{
			model.ReportTypes = model.ReportType.ToSelectList();


			if (!model.SpecificDatetime)
			{
				int daysCount = 0;

				if (model.Sunday)
					daysCount++;

				if (model.Monday)
					daysCount++;

				if (model.Tuesday)
					daysCount++;

				if (model.Wednesday)
					daysCount++;

				if (model.Thursday)
					daysCount++;

				if (model.Friday)
					daysCount++;

				if (model.Saturday)
					daysCount++;

				if (daysCount == 0)
					ModelState.AddModelError("", "You need to select at least one day for a days of the week schedule.");

				if (String.IsNullOrWhiteSpace(model.Time))
					ModelState.AddModelError("", "You need to supply a time of day that the report will run.");
			}
			else
			{
				if (String.IsNullOrWhiteSpace(model.SpecifcDate))
					ModelState.AddModelError("", "You need to supply a date and time that the report will run.");
			}

			if (ModelState.IsValid)
			{
				ScheduledTask task= await _scheduledTasksService.GetScheduledTaskByIdAsync(model.ScheduleId);
				task.UserId = UserId;

				if (model.SpecificDatetime)
				{
					task.ScheduleType = (int)ScheduleTypes.SpecifcDateTime;
					task.SpecifcDate = DateTime.Parse(model.SpecifcDate);

				}
				else
				{
					task.ScheduleType = (int)ScheduleTypes.Weekly;
					task.Time = model.Time;
				}

				task.Sunday = model.Sunday;
				task.Monday = model.Monday;
				task.Tuesday = model.Tuesday;
				task.Wednesday = model.Wednesday;
				task.Thursday = model.Thursday;
				task.Friday = model.Friday;
				task.Saturday = model.Saturday;
				task.Active = true;
				task.Data = ((int)model.ReportType).ToString();
				task.TaskType = (int)TaskTypes.ReportDelivery;
				task.DepartmentId = DepartmentId;

				await _scheduledTasksService.SaveScheduledTaskAsync(task, cancellationToken);

				ViewBag.EditProfile_TabProfileActiveCSS = "";
				ViewBag.EditProfile_TabScheduleActiveCSS = "active";

				return RedirectToAction("Reporting", "Profile", new { area = "User" });
			}

			return View(model);
		}
		#endregion Reporting

		#region Staffing Schedules
		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_View)]
		public async Task<IActionResult> AddNewStaffingSchedule(string userId)
		{
			var model = new NewStaffingLevelView();

			var staffingLevels= await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);
			if (staffingLevels == null)
			{
				model.StaffingLevels = model.StaffingLevelEnum.ToSelectListInt();
			}
			else
			{
				model.StaffingLevels = new SelectList(staffingLevels.GetActiveDetails(), "CustomStateDetailId", "ButtonText");
			}

			model.UserId = !String.IsNullOrWhiteSpace(userId) ? userId : UserId;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_View)]
		public async Task<IActionResult> ViewSchedules(string userId)
		{
			string userToGet = !String.IsNullOrWhiteSpace(userId) ? userId : UserId;

			var model = new EditProfileModel();
			model.Department= await _departmentsService.GetDepartmentByUserIdAsync(userToGet);
			model.User = _usersService.GetUserById(userToGet);

			if (userToGet == UserId)
				model.Self = true;
			else
				model.Name = await UserHelper.GetFullNameForUser(userToGet);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		public async Task<IActionResult>  AddNewStaffingSchedule(NewStaffingLevelView model, CancellationToken cancellationToken)
		{
			var staffingLevels= await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);
			if (staffingLevels == null)
			{
				model.StaffingLevels = model.StaffingLevelEnum.ToSelectListInt();
			}
			else
			{
				model.StaffingLevels = new SelectList(staffingLevels.GetActiveDetails(), "CustomStateDetailId", "ButtonText");
			}

			if (!model.SpecificDatetime)
			{
				int daysCount = 0;

				if (model.Sunday)
					daysCount++;

				if (model.Monday)
					daysCount++;

				if (model.Tuesday)
					daysCount++;

				if (model.Wednesday)
					daysCount++;

				if (model.Thursday)
					daysCount++;

				if (model.Friday)
					daysCount++;

				if (model.Saturday)
					daysCount++;

				if (daysCount == 0)
					ModelState.AddModelError("", "You need to select at least one day for a days of the week schedule.");

				if (String.IsNullOrWhiteSpace(model.Time))
					ModelState.AddModelError("", "You need to supply a time of day that the staffing level will change.");
			}
			else
			{
				if (String.IsNullOrWhiteSpace(model.SpecifcDate))
					ModelState.AddModelError("", "You need to supply a date and time that the staffing level will change.");
			}

			if (ModelState.IsValid)
			{
				var task = new ScheduledTask();
				task.UserId = model.UserId;

				if (model.SpecificDatetime)
				{
					task.ScheduleType = (int)ScheduleTypes.SpecifcDateTime;
					task.SpecifcDate = DateTime.Parse(model.SpecifcDate).ToUniversalTime();
				}
				else
				{
					task.ScheduleType = (int)ScheduleTypes.Weekly;
					task.Time = model.Time;
				}

				task.Sunday = model.Sunday;
				task.Monday = model.Monday;
				task.Tuesday = model.Tuesday;
				task.Wednesday = model.Wednesday;
				task.Thursday = model.Thursday;
				task.Friday = model.Friday;
				task.Saturday = model.Saturday;
				task.AddedOn = DateTime.UtcNow;
				task.Active = true;
				task.Data = model.StaffingLevel.ToString();
				task.TaskType = (int)TaskTypes.UserStaffingLevel;
				task.DepartmentId = DepartmentId;

				await _scheduledTasksService.SaveScheduledTaskAsync(task, cancellationToken);

				ViewBag.EditProfile_TabProfileActiveCSS = "";
				ViewBag.EditProfile_TabScheduleActiveCSS = "active";

				if (model.UserId == UserId)
					return RedirectToAction("ViewSchedules", "Profile", new { area = "User" });
				else
					return RedirectToAction("ViewSchedules", "Profile", new { area = "User", userId = model.UserId });
			}

			return View(model);
		}


		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_View)]
		public async Task<IActionResult>  GetScheduledStaffingTasksForGrid(string userId)
		{
			string userToGet = !String.IsNullOrWhiteSpace(userId) ? userId : UserId;
			var scheduleJson = new List<ScheduledTasksForJson>();

			var dep= await _departmentsService.GetDepartmentByUserIdAsync(userToGet);
			var tasks= await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(userToGet);

			foreach (var task in tasks)
			{
				var st = new ScheduledTasksForJson();
				st.ScheduleId = task.ScheduledTaskId;
				if (task.ScheduleType == (int)ScheduleTypes.Weekly)
					st.ScheduleType = "Weekly";
				else
					st.ScheduleType = "Specific Date";

				if (task.SpecifcDate.HasValue)
					st.SpecifcDate = TimeConverterHelper.TimeConverter(task.SpecifcDate.Value, dep);

				st.IsActive = task.Active;

				var days = new StringBuilder();

				if (task.Monday)
					days.Append("M");

				if (task.Tuesday)
					days.Append("T");

				if (task.Wednesday)
					days.Append("W");

				if (task.Thursday)
					days.Append("Th");

				if (task.Friday)
					days.Append("F");

				if (task.Saturday)
					days.Append("S");

				if (task.Sunday)
					days.Append("Su");

				days.Append(" @ " + task.Time);

				if (task.ScheduleType == (int)ScheduleTypes.Weekly)
					st.DaysOfWeek = days.ToString();

				var state = int.Parse(task.Data);
				if (state <= 25)
				{
					st.Data = ((UserStateTypes)int.Parse(task.Data)).ToString();
				}
				else
				{
					var stateDetail= await _customStateService.GetCustomDetailForDepartmentAsync(DepartmentId, state);

					st.Data = stateDetail.ButtonText;
				}

				scheduleJson.Add(st);
			}

			return Json(scheduleJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		public async Task<IActionResult>  EditStaffingSchedule(int scheduleId)
		{
			var model = new EditStaffingLevelView();

			var staffingLevels= await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);
			if (staffingLevels == null)
			{
				model.StaffingLevels = model.StaffingLevelEnum.ToSelectListInt();
			}
			else
			{
				model.StaffingLevels = new SelectList(staffingLevels.GetActiveDetails(), "CustomStateDetailId", "ButtonText");
			}

			var schedule= await _scheduledTasksService.GetScheduledTaskByIdAsync(scheduleId);
			if (schedule.ScheduleType == (int)ScheduleTypes.SpecifcDateTime)
			{
				model.SpecificDatetime = true;
				model.SpecifcDate = schedule.SpecifcDate.Value.ToString("g");
			}
			else
			{
				model.Time = schedule.Time;
			}

			model.ScheduleId = scheduleId;
			model.Sunday = schedule.Sunday;
			model.Monday = schedule.Monday;
			model.Tuesday = schedule.Tuesday;
			model.Wednesday = schedule.Wednesday;
			model.Thursday = schedule.Thursday;
			model.Friday = schedule.Friday;
			model.Saturday = schedule.Saturday;
			model.StaffingLevel = int.Parse(schedule.Data);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		public async Task<IActionResult>  EditStaffingSchedule(EditStaffingLevelView model, CancellationToken cancellationToken)
		{
			var staffingLevels= await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);
			if (staffingLevels == null)
			{
				model.StaffingLevels = model.StaffingLevelEnum.ToSelectListInt();
			}
			else
			{
				model.StaffingLevels = new SelectList(staffingLevels.GetActiveDetails(), "CustomStateDetailId", "ButtonText");
			}

			if (!model.SpecificDatetime)
			{
				int daysCount = 0;

				if (model.Sunday)
					daysCount++;

				if (model.Monday)
					daysCount++;

				if (model.Tuesday)
					daysCount++;

				if (model.Wednesday)
					daysCount++;

				if (model.Thursday)
					daysCount++;

				if (model.Friday)
					daysCount++;

				if (model.Saturday)
					daysCount++;

				if (daysCount == 0)
					ModelState.AddModelError("", "You need to select at least one day for a days of the week schedule.");

				if (String.IsNullOrWhiteSpace(model.Time))
					ModelState.AddModelError("", "You need to supply a time of day that the staffing level will change.");
			}
			else
			{
				if (String.IsNullOrWhiteSpace(model.SpecifcDate))
					ModelState.AddModelError("", "You need to supply a date and time that the staffing level will change.");
			}

			if (ModelState.IsValid)
			{
				ScheduledTask task= await _scheduledTasksService.GetScheduledTaskByIdAsync(model.ScheduleId);

				if (model.SpecificDatetime)
				{
					task.ScheduleType = (int)ScheduleTypes.SpecifcDateTime;
					task.SpecifcDate = DateTime.Parse(model.SpecifcDate);
				}
				else
				{
					task.ScheduleType = (int)ScheduleTypes.Weekly;
					task.Time = model.Time;
				}

				task.Sunday = model.Sunday;
				task.Monday = model.Monday;
				task.Tuesday = model.Tuesday;
				task.Wednesday = model.Wednesday;
				task.Thursday = model.Thursday;
				task.Friday = model.Friday;
				task.Saturday = model.Saturday;
				task.Active = true;
				task.Data = model.StaffingLevel.ToString();
				task.TaskType = (int)TaskTypes.UserStaffingLevel;
				task.DepartmentId = DepartmentId;

				await _scheduledTasksService.SaveScheduledTaskAsync(task, cancellationToken);

				ViewBag.EditProfile_TabProfileActiveCSS = "";
				ViewBag.EditProfile_TabScheduleActiveCSS = "active";

				if (task.UserId == UserId)
					return RedirectToAction("ViewSchedules", "Profile", new { area = "User" });
				else
					return RedirectToAction("ViewSchedules", "Profile", new { area = "User", userId = task.UserId });
			}

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		public async Task<IActionResult>  DeactivateSchedule(int scheduleId, CancellationToken cancellationToken)
		{
			await _scheduledTasksService.DisabledScheduledTaskByIdAsync(scheduleId, cancellationToken);
			return new EmptyResult();
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		public async Task<IActionResult>  ActivateSchedule(int scheduleId, CancellationToken cancellationToken)
		{
			await _scheduledTasksService.EnableScheduledTaskByIdAsync(scheduleId, cancellationToken);
			return new EmptyResult();
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		public async Task<IActionResult> DeleteSchedule(int scheduleId, CancellationToken cancellationToken)
		{
			await _scheduledTasksService.DeleteScheduledTask(scheduleId, cancellationToken);
			return new EmptyResult();
		}
		#endregion Staffing Schedules

		#region Certifications
		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_View)]
		public async Task<IActionResult> Certifications(string userId)
		{
			string userToGet = !String.IsNullOrWhiteSpace(userId) ? userId : UserId;

			var model = new CertificationsView();
			model.Certifications= await _certificationService.GetCertificationsByUserIdAsync(userToGet);
			model.Department= await _departmentsService.GetDepartmentByUserIdAsync(userToGet);
			model.UserId = userToGet;

			var user= _usersService.GetUserById(userToGet);
			if (userToGet == UserId)
				model.Self = true;
			else
				model.Name = await UserHelper.GetFullNameForUser(userToGet);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_View)]
		public async Task<IActionResult> AddCertification(string userId)
		{
			string userToGet = !String.IsNullOrWhiteSpace(userId) ? userId : UserId;
			var model = new AddCertificationView();
			model.UserId = userToGet;
			var types = await _certificationService.GetAllCertificationTypesByDepartmentAsync(DepartmentId);
			model.CertificationTypes = new SelectList(types, "Type", "Type");

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Profile_View)]
		public async Task<IActionResult> AddCertification(AddCertificationView model, IFormFile fileToUpload, CancellationToken cancellationToken)
		{
			if (fileToUpload != null && fileToUpload.Length > 0)
			{
				var extenion = FileHelper.GetFileExtensionWithoutDot(fileToUpload.FileName);

				if (extenion != "jpg" && extenion != "jpeg" && extenion != "png" && extenion != "gif" && extenion != "gif" && extenion != "pdf" && extenion != "doc"
					&& extenion != "docx" && extenion != "ppt" && extenion != "pptx" && extenion != "pps" && extenion != "ppsx" && extenion != "odt"
					&& extenion != "xls" && extenion != "xlsx" && extenion != "txt")
					ModelState.AddModelError("fileToUpload", string.Format("File type ({0}) is not importable.", extenion));

				if (fileToUpload.Length > 10000000)
					ModelState.AddModelError("fileToUpload", "Document is too large, must be smaller then 10MB.");
			}

			var types = await _certificationService.GetAllCertificationTypesByDepartmentAsync(DepartmentId);
			model.CertificationTypes = new SelectList(types, "Type", "Type");

			if (ModelState.IsValid)
			{
				var cert = new PersonnelCertification();
				cert.DepartmentId = DepartmentId;
				cert.UserId = model.UserId;
				cert.Name = model.Name;
				cert.Number = model.Number;
				cert.Area = model.Area;
				cert.IssuedBy = model.IssuedBy;
				cert.ExpiresOn = model.ExpiresOn;
				cert.RecievedOn = model.RecievedOn;
				cert.Type = model.Type;

				if (fileToUpload != null && fileToUpload.Length > 0)
				{
					cert.Filetype = String.IsNullOrWhiteSpace(fileToUpload.ContentType) ? "application/octet-stream" : fileToUpload.ContentType;
					cert.Filename = FileHelper.GetSafeFileName(fileToUpload.FileName);

					using (var uploadStream = fileToUpload.OpenReadStream())
					{
						cert.Data = await FileHelper.ReadAllBytesAsync(uploadStream, cancellationToken);
					}
				}

				await _certificationService.SaveCertificationAsync(cert, cancellationToken);

				if (model.UserId == UserId)
					return RedirectToAction("Certifications", "Profile", new { area = "User" });
				else
					return RedirectToAction("Certifications", "Profile", new { area = "User", userId = model.UserId });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		public async Task<IActionResult> DeleteCertification(int certId, CancellationToken cancellationToken)
		{
			var cert= await _certificationService.GetCertificationByIdAsync(certId);

			if (cert.DepartmentId != DepartmentId)
				return Unauthorized();

			string userId = cert.UserId;

			await _certificationService.DeleteCertification(cert, cancellationToken);

			if (userId == UserId)
				return RedirectToAction("Certifications", "Profile", new { area = "User" });
			else
				return RedirectToAction("Certifications", "Profile", new { area = "User", userId = userId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		public async Task<IActionResult> EditCertification(EditCertificationView model, IFormFile fileToUpload, CancellationToken cancellationToken)
		{
			var cert= await _certificationService.GetCertificationByIdAsync(model.CertificationId);

			if (cert.DepartmentId != DepartmentId)
				return Unauthorized();

			if (fileToUpload != null && fileToUpload.Length > 0)
			{
				var extenion = FileHelper.GetFileExtensionWithoutDot(fileToUpload.FileName);

				if (extenion != "jpg" && extenion != "jpeg" && extenion != "png" && extenion != "gif" && extenion != "gif" && extenion != "pdf" && extenion != "doc"
					&& extenion != "docx" && extenion != "ppt" && extenion != "pptx" && extenion != "pps" && extenion != "ppsx" && extenion != "odt"
					&& extenion != "xls" && extenion != "xlsx" && extenion != "txt")
					ModelState.AddModelError("fileToUpload", string.Format("File type ({0}) is not importable.", extenion));

				if (fileToUpload.Length > 10000000)
					ModelState.AddModelError("fileToUpload", "Document is too large, must be smaller then 10MB.");
			}

			if (ModelState.IsValid)
			{
				cert.DepartmentId = DepartmentId;
				cert.Name = model.Name;
				cert.Number = model.Number;
				cert.Area = model.Area;
				cert.IssuedBy = model.IssuedBy;
				cert.ExpiresOn = model.ExpiresOn;
				cert.RecievedOn = model.RecievedOn;
				cert.Type = model.Type;

				if (fileToUpload != null && fileToUpload.Length > 0)
				{
					cert.Filetype = String.IsNullOrWhiteSpace(fileToUpload.ContentType) ? "application/octet-stream" : fileToUpload.ContentType;
					cert.Filename = FileHelper.GetSafeFileName(fileToUpload.FileName);

					using (var uploadStream = fileToUpload.OpenReadStream())
					{
						cert.Data = await FileHelper.ReadAllBytesAsync(uploadStream, cancellationToken);
					}
				}

				await _certificationService.SaveCertificationAsync(cert, cancellationToken);

				if (cert.UserId == UserId)
					return RedirectToAction("Certifications", "Profile", new { area = "User" });
				else
					return RedirectToAction("Certifications", "Profile", new { area = "User", userId = cert.UserId });
			}

			var certificationTypes = await _certificationService.GetAllCertificationTypesByDepartmentAsync(DepartmentId);
			model.CertificationTypes = new SelectList(certificationTypes, "Type", "Type");
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		public async Task<IActionResult> EditCertification(int certId)
		{
			var cert= await _certificationService.GetCertificationByIdAsync(certId);

			if (cert.DepartmentId != DepartmentId)
				return Unauthorized();

			var model = new EditCertificationView();
			model.CertificationId = cert.PersonnelCertificationId;
			model.Name = cert.Name;
			model.Number = cert.Number;
			model.Area = cert.Area;
			model.IssuedBy = cert.IssuedBy;
			model.ExpiresOn = cert.ExpiresOn;
			model.RecievedOn = cert.RecievedOn;
			model.Type = cert.Type;

			var types = await _certificationService.GetAllCertificationTypesByDepartmentAsync(DepartmentId);
			model.CertificationTypes = new SelectList(types, "Type", "Type");

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_View)]

		public async Task<IActionResult> GetCertificationData(int certId)
		{
			var cert= await _certificationService.GetCertificationByIdAsync(certId);

			if (cert.DepartmentId != DepartmentId)
				return Unauthorized();

			return new FileContentResult(cert.Data, cert.Filetype)
			{
				FileDownloadName = cert.Filename
			};
		}

		[Authorize(Policy = ResgridResources.Profile_View)]
		[HttpGet]
		public async Task<IActionResult> GetDepartmentCertificationTypes()
		{
			return Json(_certificationService.GetDepartmentCertificationTypesAsync(DepartmentId));
		}
		#endregion Certifications

		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_View)]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5)]
		public async Task<IActionResult> ResetPasswordForUser(string userId)
		{
			var member = await _departmentsService.GetDepartmentMemberAsync(userId, DepartmentId);
			if (member == null)
				return NotFound();
			if (!await CanAdministratorResetPasswordAsync(userId, member))
				return Unauthorized();

			var user = await _userManager.FindByIdAsync(userId);
			if (user == null)
				return NotFound();

			var model = new ResetPasswordForUserView
			{
				UserId = userId,
				MustChangePasswordOnLogin = true
			};
			await PopulatePasswordResetModelAsync(model, user, member);
			model.Message = TempData["PasswordResetMessage"] as string;

			if (model.IsSsoManaged)
				return Forbid();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Profile_View)]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 5)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ResetPasswordForUser(ResetPasswordForUserView model, CancellationToken cancellationToken)
		{
			var user = await _userManager.FindByIdAsync(model.UserId);
			if (user == null)
				return NotFound();

			var member = await _departmentsService.GetDepartmentMemberAsync(model.UserId, DepartmentId);
			if (member == null)
				return NotFound();
			if (!await CanAdministratorResetPasswordAsync(model.UserId, member))
				return Unauthorized();

			await PopulatePasswordResetModelAsync(model, user, member);
			if (model.IsSsoManaged)
				return Forbid();
			if (!TryGetMfaVerifiedAtUtc(out var mfaVerifiedAtUtc))
				return Forbid();

			if (model.UseEmailResetLink)
			{
				// The mode is derived from durable department policy. Ignore any password fields or
				// mode value supplied by the browser so a direct POST cannot bypass the setting.
				ModelState.Remove(nameof(model.Password));
				ModelState.Remove(nameof(model.ConfirmPassword));
				model.Password = null;
				model.ConfirmPassword = null;

				if (string.IsNullOrWhiteSpace(user.Email) || !await _userManager.IsEmailConfirmedAsync(user))
				{
					ModelState.AddModelError(string.Empty,
						"A reset link cannot be sent until this user has a confirmed email address.");
					return View(model);
				}

				return await SendAdministratorPasswordResetLinkAsync(model, user, mfaVerifiedAtUtc, cancellationToken);
			}

			if (!ModelState.IsValid)
				return View(model);

			// Validate new password against system-enforced complexity and department min-length policy
			var policyError = await _departmentSsoService.ValidatePasswordAgainstPolicyAsync(DepartmentId, model.Password);
			if (policyError != null)
			{
				ModelState.AddModelError("Password", ResolvePwdError(policyError));
				return View(model);
			}

			var now = DateTime.UtcNow;
			user.AuthenticationGeneration++;
			user.CredentialsValidAfterUtc = now;
			user.AuthenticationStateChangedOn = now;
			var token = await _userManager.GeneratePasswordResetTokenAsync(user);
			var result = await _userManager.ResetPasswordAsync(user, token, model.Password);
			var auditData = BuildPasswordResetAuditData(model.UserId, "direct", mfaVerifiedAtUtc,
				result.Succeeded, result.Succeeded, model.MustChangePasswordOnLogin);

			if (result.Succeeded)
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
				await _departmentSsoService.RecordPasswordChangedAsync(DepartmentId, model.UserId);

				member.MustChangePassword = model.MustChangePasswordOnLogin;
				await _departmentsService.SaveDepartmentMemberAsync(member, cancellationToken);

				await _userSessionService.RevokeAllAfterCredentialChangeAsync(UserId, model.UserId,
					UserSessionRevocationReason.PasswordReset, now, cancellationToken);

				await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)SystemAuditSystems.Website,
					Type = (int)SystemAuditTypes.PasswordResetByAdministrator,
					DepartmentId = DepartmentId,
					UserId = UserId,
					TargetUserId = model.UserId,
					Username = User.Identity?.Name,
					IpAddress = IpAddressHelper.GetRequestIP(Request, true),
					CorrelationId = HttpContext.TraceIdentifier,
					ServerName = Environment.MachineName,
					Successful = true,
					Data = auditData
				}, cancellationToken);
				PublishPasswordResetAudit(auditData, true);

				if (model.EmailUser)
					await _emailService.SendPasswordChangedByAdministratorEmail(model.Email, model.Name,
						user.UserName, department?.Name ?? "Resgrid");

				return RedirectToAction("Index", "Personnel", new { Area = "User" });
			}

			foreach (var error in result.Errors)
			{
				model.Message += error.Description + " ";
			}

			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.PasswordResetByAdministrator,
				DepartmentId = DepartmentId,
				UserId = UserId,
				TargetUserId = model.UserId,
				Username = User.Identity?.Name,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				CorrelationId = HttpContext.TraceIdentifier,
				ServerName = Environment.MachineName,
				Successful = false,
				Data = auditData
			}, cancellationToken);
			PublishPasswordResetAudit(auditData, false);

			return View(model);
		}

		private async Task PopulatePasswordResetModelAsync(ResetPasswordForUserView model, IdentityUser user,
			DepartmentMember member)
		{
			model.Name = await UserHelper.GetFullNameForUser(user.Id);
			model.Email = user.Email;
			model.Username = user.UserName;
			model.MinPasswordLength = await _departmentSsoService.GetEffectiveMinPasswordLengthAsync(DepartmentId);
			model.IsSsoManaged = await IsSsoManagedAsync(user.Id, member);
			model.UseEmailResetLink = await _departmentSettingsService.GetRequirePasswordResetViaEmailAsync(DepartmentId);
		}

		private async Task<bool> CanAdministratorResetPasswordAsync(string targetUserId, DepartmentMember targetMember)
		{
			if (string.IsNullOrWhiteSpace(targetUserId) || targetUserId == UserId || targetMember == null)
				return false;

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			if (department == null || targetUserId == department.ManagingUserId)
				return false;

			if (department.IsUserAnAdmin(UserId))
				return true;

			// Group administrators may manage only non-department-admin users in the group they
			// administer. Target group membership is resolved server-side on every request.
			if (department.IsUserAnAdmin(targetUserId))
				return false;

			var targetGroup = await _departmentGroupsService.GetGroupForUserAsync(targetUserId, DepartmentId);
			return targetGroup != null && targetGroup.IsUserGroupAdmin(UserId);
		}

		private async Task<IActionResult> SendAdministratorPasswordResetLinkAsync(ResetPasswordForUserView model,
			IdentityUser user, DateTime mfaVerifiedAtUtc, CancellationToken cancellationToken)
		{
			var requestedOn = DateTime.UtcNow;
			var ipAddress = IpAddressHelper.GetRequestIP(Request, true);
			var issued = false;
			var emailSent = false;
			string issuedToken = null;

			try
			{
				var issue = await _passwordRecoveryService.IssueAsync(user.Id, user.Email, ipAddress,
					user.AuthenticationGeneration, user.SecurityStamp, cancellationToken);
				issued = issue.Issued && !issue.RateLimited;
				issuedToken = issue.Token;

				if (issued)
				{
					var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
					var profile = await _userProfileService.GetProfileByUserIdAsync(user.Id);
					var resetPageUrl = Url.Action("ResetPassword", "Account", new { area = "" }, Request.Scheme);
					if (string.IsNullOrWhiteSpace(resetPageUrl))
						throw new InvalidOperationException("The password reset URL could not be generated.");

					var resetUrl = $"{resetPageUrl}#token={Uri.EscapeDataString(issue.Token)}";
					emailSent = await _emailService.SendPasswordRecoveryEmail(user.Email,
						profile?.FullName.AsFirstNameLastName ?? model.Name ?? "Resgrid user",
						department?.Name ?? "Resgrid", resetUrl, ipAddress,
						BoundSecurityContext(Request.Headers.UserAgent.ToString(), 512), requestedOn, false);

					if (!emailSent)
						await TryRemovePasswordRecoveryTokenAsync(issue.Token, cancellationToken);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Administrator password reset link processing failed.");
				if (!string.IsNullOrWhiteSpace(issuedToken))
					await TryRemovePasswordRecoveryTokenAsync(issuedToken, cancellationToken);
			}

			var successful = issued && emailSent;
			var auditData = BuildPasswordResetAuditData(user.Id, "email_link", mfaVerifiedAtUtc,
				successful, false, null);
			await _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)SystemAuditTypes.PasswordResetLinkSentByAdministrator,
				DepartmentId = DepartmentId,
				UserId = UserId,
				TargetUserId = user.Id,
				Username = User.Identity?.Name,
				IpAddress = ipAddress,
				CorrelationId = HttpContext.TraceIdentifier,
				ServerName = Environment.MachineName,
				Successful = successful,
				Data = auditData
			}, cancellationToken);
			PublishPasswordResetAudit(auditData, successful);

			if (!issued || !emailSent)
			{
				ModelState.AddModelError(string.Empty,
					"The password reset email could not be sent. Wait before trying again or verify the user's email configuration.");
				return View(model);
			}

			TempData["PasswordResetMessage"] =
				"A short-lived, single-use password reset link was sent to the user's confirmed email address.";
			return RedirectToAction(nameof(ResetPasswordForUser), new { userId = user.Id });
		}

		private bool TryGetMfaVerifiedAtUtc(out DateTime verifiedAtUtc)
		{
			verifiedAtUtc = default;
			if (!HttpContext.Items.TryGetValue(RequiresRecentTwoFactorAttribute.MfaVerifiedAtHttpContextItemKey,
				out var rawValue) || rawValue is not DateTime value)
				return false;

			verifiedAtUtc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
			var age = DateTime.UtcNow - verifiedAtUtc;
			return age >= TimeSpan.Zero && age <= TimeSpan.FromMinutes(5);
		}

		private static string BuildPasswordResetAuditData(string targetUserId, string resetMode,
			DateTime mfaVerifiedAtUtc, bool successful, bool sessionsRevoked, bool? mustChangePasswordOnLogin)
		{
			return JsonConvert.SerializeObject(new
			{
				target_user_id = targetUserId,
				reset_mode = resetMode,
				mfa_verified_at = mfaVerifiedAtUtc.ToString("O"),
				successful,
				sessions_revoked = sessionsRevoked,
				must_change_password_on_login = mustChangePasswordOnLogin
			});
		}

		private void PublishPasswordResetAudit(string auditData, bool successful)
		{
			_eventAggregator.SendMessage(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = AuditLogTypes.PasswordResetByAdministrator,
				After = auditData,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				UserAgent = BoundSecurityContext(Request.Headers.UserAgent.ToString(), 512),
				ServerName = Environment.MachineName,
				Successful = successful
			});
		}

		private async Task TryRemovePasswordRecoveryTokenAsync(string token, CancellationToken cancellationToken)
		{
			try
			{
				await _passwordRecoveryService.RemoveAsync(token, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Unable to remove an undelivered administrator password recovery token.");
			}
		}

		private static string BoundSecurityContext(string value, int maximumLength)
		{
			if (string.IsNullOrWhiteSpace(value))
				return "unknown";

			var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
			return normalized.Length <= maximumLength ? normalized : normalized.Substring(0, maximumLength);
		}

		private async Task<bool> IsSsoManagedAsync(string userId, DepartmentMember member = null)
		{
			var state = await _externalIdentityLinkService.GetSsoManagementStateAsync(userId);
			if (state == null || state.IsSsoManaged)
				return true;

			member ??= await _departmentsService.GetDepartmentMemberAsync(userId, DepartmentId);
			return member != null && (!string.IsNullOrWhiteSpace(member.ExternalSsoId) || member.SsoLinkedOn.HasValue);
		}

		#region Your Departments
		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<IActionResult>  YourDepartments()
		{
			YourDepartmentsView model = new YourDepartmentsView();
			model.Members = await _departmentsService.GetAllDepartmentsForUserAsync(UserId);
			model.UserId = UserId;

			var user = await _userManager.FindByIdAsync(UserId);
			_departmentsService.InvalidateDepartmentUserInCache(UserId, user);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<string> JoinDepartment(int id, string code)
		{
			if (id == 0 || String.IsNullOrWhiteSpace(code))
				return "Department Id and Code are required.";

			var department= await _departmentsService.GetDepartmentByIdAsync(id);

			if (department == null)
				return "Unknown department id or department code. Please reach out to an administrator of the department you want to join and ensure the values are correct.";

			if (department.Code != code)
				return "Unknown department id or department code. Please reach out to an administrator of the department you want to join and ensure the values are correct.";

			if (await _departmentsService.IsMemberOfDepartmentAsync(id, UserId))
				return "You are already a member of this department and cannot join it again.";

			return null;
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult>  JoinDepartment(IFormCollection form)
		{
			var departmentId = form["deparmentId"];
			var departmentCode = form["departmentCode"];

			var department= await _departmentsService.GetDepartmentByIdAsync(int.Parse(departmentId));

			if (department == null)
				return RedirectToAction("YourDepartments");

			if (department.Code != departmentCode)
				return RedirectToAction("YourDepartments");

			if (!await _departmentsService.IsMemberOfDepartmentAsync(int.Parse(departmentId), UserId))
				await _departmentsService.JoinDepartmentAsync(int.Parse(departmentId), UserId);

			return RedirectToAction("YourDepartments");
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult>  SetActiveDepartment([FromBody]ChangeActiveDepartmentModel model,
			CancellationToken cancellationToken)
		{
			if (await _departmentsService.IsMemberOfDepartmentAsync(model.DepartmentId, UserId))
			{
				if (!await CanContinueCurrentAuthenticationInDepartmentAsync(model.DepartmentId, cancellationToken))
					return Forbid();

				var user = await _userManager.FindByIdAsync(UserId);
				await _departmentsService.SetActiveDepartmentForUserAsync(UserId, model.DepartmentId, user,
					cancellationToken);
				if (!await RenewPrincipalForDepartmentAsync(user, model.DepartmentId, cancellationToken))
					return Unauthorized();

				return RedirectToAction("Dashboard", "Home", new { Area = "User" });
			}

			return RedirectToAction("YourDepartments");
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult>  SetDefaultDepartment(int departmentId, CancellationToken cancellationToken)
		{
			if (await _departmentsService.IsMemberOfDepartmentAsync(departmentId, UserId))
			{
				if (!await CanContinueCurrentAuthenticationInDepartmentAsync(departmentId, cancellationToken))
					return Forbid();

				var user = await _userManager.FindByIdAsync(UserId);

				await _departmentsService.SetActiveDepartmentForUserAsync(UserId, departmentId, user, cancellationToken);
				await _departmentsService.SetDefaultDepartmentForUserAsync(UserId, departmentId, user, cancellationToken);

				if (!await RenewPrincipalForDepartmentAsync(user, departmentId, cancellationToken))
					return Unauthorized();

				return RedirectToAction("Dashboard", "Home", new { Area = "User" });
			}

			return RedirectToAction("YourDepartments");
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteDepartmentLink(int departmentId, CancellationToken cancellationToken)
		{
			if (!await _departmentsService.IsMemberOfDepartmentAsync(departmentId, UserId))
				return RedirectToAction("YourDepartments");

			var departmentLinks = await _departmentsService.GetAllDepartmentsForUserAsync(UserId);
			var departmentToRemove = departmentLinks.FirstOrDefault(x => x.DepartmentId == departmentId);
			if (departmentToRemove == null || departmentLinks.Count <= 1 ||
				departmentToRemove.Department?.ManagingUserId == UserId)
				return RedirectToAction("YourDepartments");

			var user = await _userManager.FindByIdAsync(UserId);
			var remainingDepartment = departmentLinks
				.Where(x => x.DepartmentId != departmentToRemove.DepartmentId)
				.OrderByDescending(x => x.IsDefault)
				.FirstOrDefault();
			var switchesDepartment = departmentToRemove.IsActive;
			var canKeepCurrentSession = !switchesDepartment ||
				await CanContinueCurrentAuthenticationInDepartmentAsync(remainingDepartment.DepartmentId,
					cancellationToken);

			if (switchesDepartment)
			{
				await _departmentsService.SetActiveDepartmentForUserAsync(UserId, remainingDepartment.DepartmentId,
					user, cancellationToken);
				if (canKeepCurrentSession &&
					!await RenewPrincipalForDepartmentAsync(user, remainingDepartment.DepartmentId, cancellationToken))
				{
					// The active department has already moved but this principal still describes the old
					// one. Sign out rather than leave the two disagreeing; the next sign-in rebuilds both.
					await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
					return Unauthorized();
				}
			}

			if (departmentToRemove.IsDefault)
				await _departmentsService.SetDefaultDepartmentForUserAsync(UserId, remainingDepartment.DepartmentId,
					user, cancellationToken);

			var revoked = await _deleteService.RevokeDepartmentAccessAsync(UserId,
				departmentToRemove.DepartmentId, UserId, cancellationToken);
			if (!revoked)
			{
				Resgrid.Framework.Logging.LogError($"DeleteDepartmentLink: failed to revoke department {departmentToRemove.DepartmentId} access for user {UserId}");
				return RedirectToAction("YourDepartments");
			}

			if (switchesDepartment && !canKeepCurrentSession)
			{
				await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
				return RedirectToAction("LogOn", "Account", new {Area = ""});
			}

			return switchesDepartment
				? RedirectToAction("Dashboard", "Home", new {Area = "User"})
				: RedirectToAction("YourDepartments");
		}

		private async Task<bool> CanContinueCurrentAuthenticationInDepartmentAsync(int departmentId,
			CancellationToken cancellationToken)
		{
			var requiresSso = await _departmentSsoService.IsRequireSsoPolicyActiveAsync(departmentId,
				cancellationToken) && await _departmentSsoService.IsSsoEnabledForDepartmentAsync(departmentId,
				cancellationToken);
			var localLoginAllowed = await _externalIdentityLinkService.IsLocalLoginAllowedAsync(UserId,
				departmentId, cancellationToken);
			if (!requiresSso && localLoginAllowed)
				return true;

			// An authentication established for another department cannot be used to enter a
			// department that requires its own SSO policy. The user must authenticate at that IdP.
			var currentSessionId = User.FindFirstValue(SessionClaimTypes.SessionId);
			var current = (await _userSessionService.GetActiveForUserAsync(UserId, cancellationToken))
				.FirstOrDefault(session => session.UserSessionId == currentSessionId);
			return current?.DepartmentId == departmentId &&
				(current.AuthenticationMethod == UserSessionAuthenticationMethod.OidcSso ||
				 current.AuthenticationMethod == UserSessionAuthenticationMethod.SamlSso);
		}

		private async Task<bool> RenewPrincipalForDepartmentAsync(IdentityUser user, int departmentId,
			CancellationToken cancellationToken)
		{
			var authentication = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
			var currentSessionId = User.FindFirstValue(SessionClaimTypes.SessionId);
			if (!string.IsNullOrWhiteSpace(currentSessionId) &&
				!await _userSessionService.MoveSessionToDepartmentAsync(UserId, currentSessionId, departmentId,
					cancellationToken))
				return false;

			var principal = await _signInManager.CreateUserPrincipalAsync(user);
			if (!string.IsNullOrWhiteSpace(currentSessionId) && principal.Identity is ClaimsIdentity identity)
				identity.AddClaim(new Claim(SessionClaimTypes.SessionId, currentSessionId));

			var properties = authentication.Properties ?? new AuthenticationProperties
			{
				IssuedUtc = DateTimeOffset.UtcNow,
				ExpiresUtc = DateTimeOffset.UtcNow.AddHours(4),
				IsPersistent = false,
				AllowRefresh = false
			};
			await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
			HttpContext.User = principal;
			return true;
		}

		#endregion Your Departments


		#region Avatar
		[HttpGet]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		public async Task<IActionResult> GetAvatar(string id, int? type)
		{
			if (type == null)
			{ // User profile images (will have a null type) are by Guid
				Guid newId;
				if (!Guid.TryParse(id, out newId))
					return StatusCode(404);
			}
			else if (type != null && type == 1)
			{ // User profile images (will have a 1 type) are by Guid
				Guid newId;
				if (!Guid.TryParse(id, out newId))
					return StatusCode(404);
			}
			else if (type != null && type == 2)
			{ // Department Profiles are by Int
				int newId;
				if (!int.TryParse(id, out newId))
					return StatusCode(404);
			}


			byte[] data = null;
			if (type == null)
				data= await _imageService.GetImageAsync(ImageTypes.Avatar, id);
			else
				data= await _imageService.GetImageAsync((ImageTypes)type.Value, id);

			if (data == null || data.Length <= 0)
				return StatusCode(404);

			return File(data, "image/jpeg");
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		public async Task<IActionResult> Upload(string id, int? type, CancellationToken cancellationToken)
		{
			if (Request.Form.Files == null || !Request.Form.Files.Any()) { return BadRequest(); }

			var file = Request.Form.Files[0];

			// check for a valid mediatype
			if (!file.ContentType.StartsWith("image/"))
				return StatusCode(400);

			var extension = Path.GetExtension(file.FileName);
			byte[] imgArray = null;
			int width = 0;
			int height = 0;

			using (Image image = Image.Load(file.OpenReadStream()))
			{
				using (MemoryStream stream = new MemoryStream())
				{
					IImageFormat imageFormat;

					if (Configuration.Default.ImageFormatsManager.TryFindFormatByFileExtension("png", out imageFormat))
					{
						image.Save(stream, imageFormat);

						width = image.Width;
						height = image.Height;

						stream.Position = 0;
						imgArray = stream.ToArray();
					}
				}
			}

			if (type == null)
				await _imageService.SaveImageAsync(ImageTypes.Avatar, id, imgArray, cancellationToken);
			else
				await _imageService.SaveImageAsync((ImageTypes)type.Value, id, imgArray, cancellationToken);

			string url;

			if (type == null)
				url = $"{Request.Scheme}://{Request.Host}/User/Profile/GetAvatar?id=" + id;
			else
				url = $"{Request.Scheme}://{Request.Host}/User/Profile/GetAvatar?id=" + id + "?type=" + type.Value;

			var obj = new
			{
				status = CroppicStatuses.Success,
				url = url,
				width = width,
				height = height
			};

			return Json(obj);
		}

		[HttpPut]
		[Authorize(Policy = ResgridResources.Profile_Update)]
		public async Task<IActionResult> Crop([FromBody]CropRequest model, CancellationToken cancellationToken)
		{
			// extract original image ID and generate a new filename for the cropped result
			var originalUri = new Uri(model.imgUrl);
			var originalId = originalUri.Query.Replace("?id=", "");
			byte[] imgArray = null;

			try
			{
				var ms = new MemoryStream(await _imageService.GetImageAsync(ImageTypes.Avatar, originalId));

				using (Image image = Image.Load(ms))
				{
					using (MemoryStream stream = new MemoryStream())
					{
						IImageFormat imageFormat;

						if (Configuration.Default.ImageFormatsManager.TryFindFormatByFileExtension("png", out imageFormat))
						{
							image.Mutate(x => x
							 .Resize((int)model.imgW, (int)model.imgH));

							Rectangle rectangle = new Rectangle(model.imgX1, model.imgY1, model.cropW, model.cropH);
							image.Mutate(ctx => ctx.Crop(rectangle));

							image.Save(stream, imageFormat);

							stream.Position = 0;
							imgArray = stream.ToArray();
						}
					}
				}

				await _imageService.SaveImageAsync(ImageTypes.Avatar, originalId, imgArray, cancellationToken);
			}
			catch (Exception e)
			{
				return StatusCode(500);
			}

			var obj = new
			{
				status = CroppicStatuses.Success,
				url = model.imgUrl
			};

			return Json(obj);
		}

		internal static class CroppicStatuses
		{
			public const string Success = "success";
			public const string Error = "error";
		}

		private const int AvatarStoredWidth = 100;  // ToDo - Change the size of the stored avatar image
		private const int AvatarStoredHeight = 100; // ToDo - Change the size of the stored avatar image
		private const int AvatarScreenWidth = 400;  // ToDo - Change the value of the width of the image on the screen
		private readonly string[] _imageFileExtensions = { ".jpg", ".png", ".gif", ".jpeg" };

		[ValidateAntiForgeryToken]
		public async Task<IActionResult> _Upload(ICollection<IFormFile> files)
		{
			if (files == null || !files.Any()) return Json(new { success = false, errorMessage = "No file uploaded." });

			var file = files.FirstOrDefault();  // get ONE only

			if (file == null || !IsImage(file)) return Json(new { success = false, errorMessage = "File is of wrong format." });

			if (file.Length <= 0) return Json(new { success = false, errorMessage = "File cannot be zero length." });

			return Json(new { success = true }); // success
		}

		[HttpPost]
		public async Task<IActionResult> SaveAvatar(string t, string l, string h, string w, IFormFile file, CancellationToken cancellationToken)
		{
			try
			{
				//var img = new WebImage(file.InputStream);
				//var ratio = img.Height / (double)img.Width;
				//img.Resize(AvatarScreenWidth, (int)(AvatarScreenWidth * ratio));

				// Calculate dimensions
				var top = Convert.ToInt32(t.Replace("-", "").Replace("px", ""));
				var left = Convert.ToInt32(l.Replace("-", "").Replace("px", ""));
				var height = Convert.ToInt32(h.Replace("-", "").Replace("px", ""));
				var width = Convert.ToInt32(w.Replace("-", "").Replace("px", ""));

				//var img = new WebImage(file.InputStream);

				//img.Resize(width, height);
				// ... crop the part the user selected, ...
				//img.Crop(top, left, img.Height - top - AvatarStoredHeight, img.Width - left - AvatarStoredWidth);

				var profile= await _userProfileService.GetProfileByUserIdAsync(UserId);

				if (profile != null)
				{
					//profile.Image = img.GetBytes();
				}
				else
				{
					profile = new UserProfile();

					var user= _usersService.GetUserById(UserId);

					profile.UserId = UserId;
					//profile.Image = img.GetBytes();
					profile.FirstName = await UserHelper.GetFullNameForUser(UserId);
					profile.MobileCarrier = (int)MobileCarriers.None;
					profile.SendEmail = true;
					profile.SendPush = true;
					profile.SendMessageEmail = true;
					profile.SendMessagePush = true;
					profile.SendNotificationEmail = true;
					profile.SendNotificationPush = true;


				}

				profile.LastUpdated = DateTime.Now;
				await _userProfileService.SaveProfileAsync(DepartmentId, profile, cancellationToken);

				return Json(new { success = true });
			}
			catch (Exception ex)
			{
				return Json(new { success = false, errorMessage = "Unable to upload file.\nERRORINFO: " + ex.Message });
			}
		}

		private bool IsImage(IFormFile file)
		{
			if (file == null) return false;
			return file.ContentType.Contains("image") ||
					_imageFileExtensions.Any(item => file.FileName.EndsWith(item, StringComparison.OrdinalIgnoreCase));
		}
		#endregion Avatar
	}
}
