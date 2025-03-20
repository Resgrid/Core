using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Logs;
using Resgrid.Web.Areas.User.Models.Personnel;
using Resgrid.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Model.Providers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class LogsController : SecureBaseController
	{

		#region Private Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly ICallsService _callsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly ICommunicationService _communicationService;
		private readonly IQueueService _queueService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly IWorkLogsService _workLogsService;
		private readonly IEventAggregator _eventAggregator;

		public LogsController(IDepartmentsService departmentsService, IUsersService usersService, ICallsService callsService,
			IDepartmentGroupsService departmentGroupsService, ICommunicationService communicationService, IQueueService queueService,
			Model.Services.IAuthorizationService authorizationService, IWorkLogsService workLogsService, IEventAggregator eventAggregator)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_callsService = callsService;
			_departmentGroupsService = departmentGroupsService;
			_communicationService = communicationService;
			_queueService = queueService;
			_authorizationService = authorizationService;
			_workLogsService = workLogsService;
			_eventAggregator = eventAggregator;
		}
		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Log_View)]
		public async Task<IActionResult> Index()
		{
			LogsIndexView model = new LogsIndexView();
			model.CallLogs = await _workLogsService.GetAllCallLogsForUserAsync(UserId);
			model.WorkLogs = await _workLogsService.GetAllLogsForUserAsync(UserId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			model.Years = new List<SelectListItem>();

			var years = await _workLogsService.GetLogYearsByDeptartmentAsync(DepartmentId);

			if (years != null && years.Any())
			{
				foreach (var year in years)
				{
					model.Years.Add(new SelectListItem(year, year));
				}
				model.Year = years[0];
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Log_Create)]
		public async Task<IActionResult> NewLog()
		{
			var model = new NewLogView();
			await PopulateLogViewModel(model);
			model.Log = new Log();

			if (model.Call == null)
				model.Call = new Call();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Log_Create)]
		public async Task<IActionResult> NewLog(NewLogView model, IFormCollection form, ICollection<IFormFile> files, CancellationToken cancellationToken)
		{
			await PopulateLogViewModel(model);

			if (model.LogType == LogTypes.Work && String.IsNullOrWhiteSpace(form["nonUnitPersonnel"]))
			{
				model.ErrorMessage = "You need to specify at least 1 person to be part of the work log.";
				return View(model);
			}

			try
			{
				try
				{
					if (files != null && files.Any())
					{
						foreach (var file in files)
						{
							if (file != null && !String.IsNullOrWhiteSpace(file.FileName))
							{
								string extension = Path.GetExtension(file.FileName);

								if (!String.IsNullOrWhiteSpace(extension))
									extension = extension.ToLower();

								if (extension != ".jpg" && extension != ".jpeg" && extension != ".png" && extension != ".gif" && extension != ".pdf" &&
										extension != ".doc" && extension != ".docx" && extension != ".ppt" && extension != ".pptx" && extension != ".pps" &&
										extension != ".ppsx" && extension != ".odt" && extension != ".xls" && extension != ".xlsx" && extension != ".txt" && extension != ".rtf")
									model.ErrorMessage = string.Format("File type ({0}) is not importable.", extension);

								if (file.Length > 10000000)
									model.ErrorMessage = "Document is too large, must be smaller then 10MB.";
							}
						}
					}
				}
				catch { }

				if (!String.IsNullOrWhiteSpace(model.ErrorMessage))
					return View(model);

				// Get all unit blocks in the report
				List<int> unitsInReport = (from object key in form.Keys where key.ToString().StartsWith("unit_personnel_") select int.Parse(key.ToString().Replace("unit_personnel_", ""))).ToList();

				model.Log.LoggedByUserId = UserId;
				model.Log.DepartmentId = model.Department.DepartmentId;
				model.Log.Narrative = System.Net.WebUtility.HtmlDecode(model.Log.Narrative);
				model.Log.Cause = System.Net.WebUtility.HtmlDecode(model.Log.Cause);
				model.Log.InitialReport = System.Net.WebUtility.HtmlDecode(model.Log.InitialReport);
				model.Log.LogType = (int)model.LogType;

				if (model.Log.StationGroupId == 0)
					model.Log.StationGroupId = null;

				model.Log.Units = new Collection<LogUnit>();
				model.Log.Users = new Collection<LogUser>();

				if (String.IsNullOrWhiteSpace(model.Log.InvestigatedByUserId))
					model.Log.InvestigatedByUserId = null;

				if (model.Log.StationGroupId.HasValue && model.Log.StationGroupId.Value == 0)
					model.Log.StationGroupId = null;

				if (model.LogType == LogTypes.Run)
				{
					if (model.CallId == 0)
					{
						model.Call.DepartmentId = DepartmentId;
						model.Call.ReportingUserId = UserId;
						model.Call.Priority = (int)model.CallPriority;

						if (model.Call.Type == "No Type")
							model.Call.Type = null;

						model.Call = await _callsService.SaveCallAsync(model.Call, cancellationToken);
						model.Log.CallId = model.Call.CallId;
						model.Log.StartedOn = model.Call.LoggedOn;
					}
					else
					{
						var call = await _callsService.GetCallByIdAsync(model.CallId);
						call.Priority = (int)model.CallPriority;
						call.NatureOfCall = model.Call.NatureOfCall;
						call.Address = model.Call.Address;
						call.LoggedOn = model.Call.LoggedOn;
						call.Name = model.Call.Name;

						if (model.Call.Type == "No Type")
							call.Type = null;
						else
							call.Type = model.Call.Type;

						model.Call = await _callsService.SaveCallAsync(call, cancellationToken);
						model.Log.CallId = model.Call.CallId;
					}
				}

				if (model.LogType == LogTypes.Work)
				{
					var startedOn = form["Log.StartedOn"];
					var endedOn = form["Log.EndedOn"];

					if (!String.IsNullOrWhiteSpace(startedOn))
						model.Log.StartedOn = DateTime.Parse(startedOn);

					if (!String.IsNullOrWhiteSpace(endedOn))
						model.Log.EndedOn = DateTime.Parse(endedOn);
				}

				if (model.LogType == LogTypes.Meeting)
				{
					var startedOn = form["Log.StartedOn"];
					var endedOn = form["Log.EndedOn"];

					if (!String.IsNullOrWhiteSpace(startedOn))
						model.Log.StartedOn = DateTime.Parse(startedOn);

					if (!String.IsNullOrWhiteSpace(endedOn))
						model.Log.EndedOn = DateTime.Parse(endedOn);
				}

				if (model.LogType == LogTypes.Coroner)
				{
					var startedOn = form["coronerDate"];
					var caseNumber = form["caseNumber"];
					var coronerInstructors = form["coronerInstructors"];
					var coronerDestination = form["coronerDestination"];
					var coronerOthers = form["coronerOthers"];

					if (!String.IsNullOrWhiteSpace(startedOn))
						model.Log.StartedOn = DateTime.Parse(startedOn);

					if (!String.IsNullOrWhiteSpace(caseNumber))
						model.Log.ExternalId = caseNumber;

					if (!String.IsNullOrWhiteSpace(coronerInstructors))
						model.Log.Instructors = coronerInstructors;

					if (!String.IsNullOrWhiteSpace(coronerDestination))
						model.Log.Location = coronerDestination;

					if (!String.IsNullOrWhiteSpace(coronerOthers))
						model.Log.OtherPersonnel = coronerOthers;
				}

				if (model.LogType == LogTypes.Callback)
				{
					model.Log.CallId = model.CallId;
				}

				foreach (var i in unitsInReport)
				{
					var unit = new LogUnit();
					unit.UnitId = i;

					if (!string.IsNullOrWhiteSpace(form["unit_dispatchtime_" + i]))
						unit.Dispatched = DateTime.Parse(form["unit_dispatchtime_" + i]);

					if (!string.IsNullOrWhiteSpace(form["unit_enroutetime_" + i]))
						unit.Enroute = DateTime.Parse(form["unit_enroutetime_" + i]);

					if (!string.IsNullOrWhiteSpace(form["unit_onscenetime_" + i]))
						unit.OnScene = DateTime.Parse(form["unit_onscenetime_" + i]);

					if (!string.IsNullOrWhiteSpace(form["unit_releasedtime_" + i]))
						unit.Released = DateTime.Parse(form["unit_releasedtime_" + i]);

					if (!string.IsNullOrWhiteSpace(form["unit_inquarterstime_" + i]))
						unit.InQuarters = DateTime.Parse(form["unit_inquarterstime_" + i]);

					model.Log.Units.Add(unit);

					if (!string.IsNullOrWhiteSpace(form["unit_personnel_" + i]))
					{
						var personnelIds = form["unit_personnel_" + i].ToString().Split(char.Parse(","));

						foreach (var personnelId in personnelIds)
						{
							var logUser = new LogUser();
							logUser.UserId = personnelId;
							logUser.UnitId = i;

							model.Log.Users.Add(logUser);
						}
					}
				}

				if (!string.IsNullOrWhiteSpace(form["nonUnitPersonnel"]))
				{
					var personnelIds = form["nonUnitPersonnel"].ToString().Split(char.Parse(","));

					foreach (var personnelId in personnelIds)
					{
						var logUser = new LogUser();
						logUser.UserId = personnelId;

						model.Log.Users.Add(logUser);
					}
				}

				var savedLog = await _workLogsService.SaveLogAsync(model.Log, cancellationToken);

				try
				{
					if (files != null)
					{
						foreach (var file in files)
						{
							if (file != null && file.Length > 0)
							{
								LogAttachment attachment = new LogAttachment();
								attachment.LogId = savedLog.LogId;
								attachment.Type = file.ContentType;
								attachment.FileName = file.FileName;

								byte[] uploadedFile = new byte[file.OpenReadStream().Length];
								file.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

								attachment.Data = uploadedFile;
								attachment.UserId = UserId;
								attachment.Timestamp = DateTime.UtcNow;

								await _workLogsService.SaveLogAttachmentAsync(attachment, cancellationToken);
							}
						}
					}
				}
				catch { }

				_eventAggregator.SendMessage<LogAddedEvent>(new LogAddedEvent() { DepartmentId = DepartmentId, Log = model.Log });
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				model.ErrorMessage = "We encountered an error trying to save your log. Please check your form to ensure it's properly filled out and try again.";
				return View(model);
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Log_View)]
		public async Task<IActionResult> GetLogsList(string year)
		{
			List<LogForListJson> logsJson = new List<LogForListJson>();

			//var logs = await _workLogsService.GetAllLogsForDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			List<Log> logs;
			if (String.IsNullOrWhiteSpace(year))
				logs = await _workLogsService.GetAllLogsForDepartmentAsync(DepartmentId);
			else
				logs = await _workLogsService.GetAllLogsForDepartmentAndYearAsync(DepartmentId, year);

			foreach (var log in logs)
			{
				var logJson = new LogForListJson();
				logJson.LogId = log.LogId;
				logJson.Type = ((LogTypes)log.LogType).ToString();

				if (log.StationGroup != null)
					logJson.Group = log.StationGroup.Name;
				else
					logJson.Group = department.Name;

				logJson.LoggedBy = await UserHelper.GetFullNameForUser(log.LoggedByUserId);
				logJson.LoggedOn = log.LoggedOn.TimeConverterToString(department);

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || log.LoggedByUserId == UserId || (log.StationGroupId.HasValue && ClaimsAuthorizationHelper.IsUserGroupAdmin(log.StationGroupId.Value)))
					logJson.CanDelete = true;
				else
					logJson.CanDelete = false;

				logsJson.Add(logJson);
			}

			return Json(logsJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Log_Delete)]
		public async Task<IActionResult> DeleteWorkLog(int logId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserDeleteWorkLogAsync(UserId, logId))
				Unauthorized();

			await _workLogsService.DeleteLogAsync(logId, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Log_View)]
		public async Task<IActionResult> TrainingPerMonth()
		{
			List<TrainingMonthJson> logMonths = new List<TrainingMonthJson>();

			CultureInfo culture = new CultureInfo("en-us");
			Calendar calendar = culture.Calendar;
			var logs =
			await _workLogsService.GetAllLogsByDepartmentDateRangeAsync(DepartmentId, LogTypes.Training,
				new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1), new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59));

			// Week
			//    Call Type
			//        Call Count

			for (int i = 1; i <= 12; i++)
			{
				var monthLogs =
					logs.Where(x => calendar.GetMonth(x.LoggedOn) == i).GroupBy(x => x.Course);

				foreach (var monthLog in monthLogs)
				{
					var logMonth = new TrainingMonthJson();
					logMonth.MonthNumber = i;
					logMonth.Month = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(i);

					string type = "No Course";
					if (!String.IsNullOrWhiteSpace(monthLog.Key))
						type = monthLog.Key;

					logMonth.Type = type;
					logMonth.Count = monthLog.ToList().Count;

					logMonths.Add(logMonth);
				}
			}

			return Json(logMonths);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Log_View)]
		public async Task<IActionResult> GetNonUnitPersonnelForLog(int logId)
		{
			var personnelJson = new List<PersonnelForJson>();

			return Json(personnelJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Log_View)]
		public async Task<FileResult> GetAttachment(int logId, int attachmentId)
		{
			var attachment = await _workLogsService.GetAttachmentByIdAsync(attachmentId);

			if (attachment.LogId != logId)
				Unauthorized();

			return new FileContentResult(attachment.Data, attachment.Type)
			{
				FileDownloadName = attachment.FileName
			};
		}

		public PartialViewResult CreateUnitHtmlBlock(int unitId, string unitName)
		{
			var model = new UnitBlockPartialView
			{
				UnitId = unitId,
				UnitName = unitName
			};

			return PartialView("_UnitLogBlockPartial", model);
		}

		private async Task<NewLogView> PopulateLogViewModel(NewLogView model)
		{
			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			model.User = _usersService.GetUserById(UserId);
			model.Types = model.LogType.ToSelectList();
			model.CallPriorities = model.CallPriority.ToSelectList();
			model.Users.Add(String.Empty, "Not Applicable");
			await model.SetUsers(await _departmentsService.GetAllUsersForDepartment(DepartmentId));
			var groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup
			{
				Name = "Not Applicable"
			});
			groups.AddRange(await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId));
			model.Stations = groups;

			List<CallType> types = new List<CallType>();
			types.Add(new CallType { CallTypeId = 0, Type = "No Type" });
			types.AddRange(await _callsService.GetCallTypesForDepartmentAsync(DepartmentId));
			model.CallTypes = new SelectList(types, "Type", "Type");

			return model;
		}
	}
}
