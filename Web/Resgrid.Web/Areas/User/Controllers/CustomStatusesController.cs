using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.CustomStatuses;
using Resgrid.Web.Helpers;
using SharpKml.Dom;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class CustomStatusesController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly ICustomStateService _customStateService;
		private readonly IUnitsService _unitsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IEventAggregator _eventAggregator;

		public CustomStatusesController(ICustomStateService customStateService, IUnitsService unitsService, IAuthorizationService authorizationService, IEventAggregator eventAggregator)
		{
			_customStateService = customStateService;
			_unitsService = unitsService;
			_authorizationService = authorizationService;
			_eventAggregator = eventAggregator;
		}
		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.CustomStates_View)]
		public async Task<IActionResult> Index()
		{
			var model = new CustomStatusesIndexView();
			model.UnitStates = new List<CustomState>();

			var allStatuses = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(DepartmentId);

			if (allStatuses != null && allStatuses.Count > 0)
			{
				model.UnitStates.AddRange(allStatuses.Where(x => x.Type == (int)CustomStateTypes.Unit));
				model.PersonnelStatuses = allStatuses.FirstOrDefault(x => x.Type == (int)CustomStateTypes.Personnel);
				model.PersonellStaffing = allStatuses.FirstOrDefault(x => x.Type == (int)CustomStateTypes.Staffing);
			}

			return View(model);
		}

		[Authorize(Policy = ResgridResources.CustomStates_Create)]
		public async Task<IActionResult> New(int type)
		{
			var model = new NewCustomStateView();
			model.Type = (CustomStateTypes)type;
			model.State = new CustomState();
			model.State.Details = new Collection<CustomStateDetail>();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.CustomStates_Create)]
		public async Task<IActionResult> New(NewCustomStateView model, IFormCollection form, CancellationToken cancellationToken)
		{
			List<int> options = (from object key in form.Keys
													 where key.ToString().StartsWith("buttonText_")
													 select int.Parse(key.ToString().Replace("buttonText_", ""))).ToList();

			if (options == null || !options.Any())
				ModelState.AddModelError("Detail", "You must supply options (which turn into the buttons) for personnel to act on before creating your custom statuses.");

			if (ModelState.IsValid)
			{
				if (options.Count > 0)
					model.State.Details = new Collection<CustomStateDetail>();

				model.State.DepartmentId = DepartmentId;
				model.State.Type = (int)model.Type;

				foreach (var i in options)
				{
					if (form.ContainsKey("buttonText_" + i))
					{
						var text = form["buttonText_" + i];
						var color = form["buttonColor_" + i];
						var textColor = form["textColor_" + i];
						var order = form["order_" + i];
						var baseType = form["baseType_" + i];

						bool gps = false;
						var gpsValue = form["requireGps_" + i];

						if (gpsValue == "on")
							gps = true;

						var noteType = int.Parse(form["noteType_" + i]);
						var detailType = int.Parse(form["detailType_" + i]);

						var detail = new CustomStateDetail();
						detail.ButtonText = text;
						detail.ButtonColor = color;
						detail.GpsRequired = gps;
						detail.NoteType = noteType;
						detail.DetailType = detailType;
						detail.TextColor = textColor;

						if (!string.IsNullOrWhiteSpace(order))
							detail.Order = int.Parse(order);
						else
							detail.Order = 0;

						if (!string.IsNullOrWhiteSpace(baseType))
							detail.BaseType = int.Parse(baseType);
						else
							detail.BaseType = 0;

						model.State.Details.Add(detail);
					}
				}

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CustomStatusAdded;
				auditEvent.After = model.State.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _customStateService.SaveAsync(model.State, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.CustomStates_Delete)]
		public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
		{
			var state = await _customStateService.GetCustomSateByIdAsync(id);

			if (!await _authorizationService.CanUserModifyCustomStatusAsync(UserId, state.CustomStateId))
				Unauthorized();

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.CustomStatusRemoved;
			auditEvent.Before = state.CloneJsonToString();
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			await _customStateService.DeleteAsync(state, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.CustomStates_Update)]
		public async Task<IActionResult> Edit(int id)
		{
			if (!await _authorizationService.CanUserModifyCustomStatusAsync(UserId, id))
				Unauthorized();

			var model = new EditStatusView();
			model.State = await _customStateService.GetCustomSateByIdAsync(id);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.CustomStates_Update)]
		public async Task<IActionResult> EditDetail(int stateDetailId)
		{
			if (!await _authorizationService.CanUserModifyCustomStateDetailAsync(UserId, stateDetailId))
				Unauthorized();

			var model = new EditDetailView();
			model.Detail = await _customStateService.GetCustomDetailByIdAsync(stateDetailId);
			model.Detail.CustomState = await _customStateService.GetCustomSateByIdAsync(model.Detail.CustomStateId);
			model.DetailTypes = model.DetailType.ToSelectList();
			model.NoteTypes = model.NoteType.ToSelectList();
			model.BaseTypes = model.BaseType.ToSelectList();

			model.DetailType = (CustomStateDetailTypes)model.Detail.DetailType;
			model.NoteType = (CustomStateNoteTypes)model.Detail.NoteType;
			model.BaseType = (ActionBaseTypes)model.Detail.BaseType;

			if (String.IsNullOrWhiteSpace(model.Detail.TextColor))
				model.Detail.TextColor = "#000000";

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.CustomStates_Update)]
		public async Task<IActionResult> EditDetail(EditDetailView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserModifyCustomStateDetailAsync(UserId, model.Detail.CustomStateDetailId))
				Unauthorized();

			model.DetailTypes = model.DetailType.ToSelectList();
			model.NoteTypes = model.NoteType.ToSelectList();

			if (ModelState.IsValid)
			{
				var auditEvent = new AuditEvent();
				var detail = await _customStateService.GetCustomDetailByIdAsync(model.Detail.CustomStateDetailId);
				auditEvent.Before = detail.CloneJsonToString();

				detail.ButtonColor = model.Detail.ButtonColor;
				detail.ButtonText = model.Detail.ButtonText;
				detail.TextColor = model.Detail.TextColor;
				detail.NoteType = (int)model.NoteType;
				detail.Order = model.Detail.Order;
				detail.GpsRequired = model.Detail.GpsRequired;
				detail.DetailType = (int)model.DetailType;
				detail.BaseType = (int)model.BaseType;

				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CustomStatusDetailUpdated;
				auditEvent.After = detail.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _customStateService.SaveDetailAsync(detail, DepartmentId, cancellationToken);

				return RedirectToAction("Edit", new { id = detail.CustomStateId });
			}

			model.Detail.CustomState = await _customStateService.GetCustomSateByIdAsync(model.Detail.CustomStateId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.CustomStates_Update)]
		public async Task<IActionResult> Edit(EditStatusView model, IFormCollection form, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserModifyCustomStatusAsync(UserId, model.State.CustomStateId))
				Unauthorized();

			if (ModelState.IsValid)
			{
				List<int> options = (from object key in form.Keys
														 where key.ToString().StartsWith("buttonText_")
														 select int.Parse(key.ToString().Replace("buttonText_", ""))).ToList();

				var auditEvent = new AuditEvent();
				var details = new List<CustomStateDetail>();
				var state = await _customStateService.GetCustomSateByIdAsync(model.State.CustomStateId);

				auditEvent.Before = state.CloneJsonToString();

				state.Name = model.State.Name;
				state.Description = model.State.Description;

				foreach (var i in options)
				{
					if (form.ContainsKey("buttonText_" + i))
					{
						var text = form["buttonText_" + i];
						var color = form["buttonColor_" + i];
						var textColor = form["textColor_" + i];
						var order = form["order_" + i];
						var baseType = form["baseType_" + i];
						var customStateDetailId = form["customStateDetailId_" + i];

						bool gps = false;
						var gpsValue = form["requireGps_" + i];

						if (gpsValue == "on")
							gps = true;

						var noteType = int.Parse(form["noteType_" + i]);
						var detailType = int.Parse(form["detailType_" + i]);

						var detail = new CustomStateDetail();
						detail.ButtonText = text;
						detail.ButtonColor = color;
						detail.GpsRequired = gps;
						detail.NoteType = noteType;
						detail.DetailType = detailType;
						detail.TextColor = textColor;
						detail.Order = int.Parse(order);
						detail.BaseType = int.Parse(baseType);

						if (!string.IsNullOrWhiteSpace(customStateDetailId))
							detail.CustomStateDetailId = int.Parse(customStateDetailId);
						else
							detail.CustomStateDetailId = 0;

						details.Add(detail);
					}
				}

				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CustomStatusUpdated;
				auditEvent.After = state.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _customStateService.UpdateAsync(state, details, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		#region Async Methods
		[HttpGet]
		[Authorize(Policy = ResgridResources.CustomStates_View)]
		public async Task<IActionResult> GetPersonnelStatusesForDepartment(bool includeAny)
		{
			List<PersonnelStatusJson> personnelStauses = new List<PersonnelStatusJson>();
			var customStauses = await _customStateService.GetActivePersonnelStateForDepartmentAsync(DepartmentId);

			if (includeAny)
				personnelStauses.Add(new PersonnelStatusJson() { Id = -1, Name = "Any" });

			if (customStauses != null)
			{
				foreach (var detail in customStauses.GetActiveDetails())
				{
					var status = new PersonnelStatusJson();
					status.Id = detail.CustomStateDetailId;
					status.Name = detail.ButtonText;

					personnelStauses.Add(status);
				}
			}
			else
			{
				personnelStauses.Add(new PersonnelStatusJson() { Id = 0, Name = "Standing By" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 1, Name = "Not Responding" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 2, Name = "Responding" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 3, Name = "On Scene" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 4, Name = "Available Station" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 5, Name = "Responding Station" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 6, Name = "Responding Scene" });
			}

			return Json(personnelStauses);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.CustomStates_View)]
		public async Task<IActionResult> GetPersonnelStaffingLevelsForDepartment(bool includeAny)
		{
			List<PersonnelStatusJson> personnelStauses = new List<PersonnelStatusJson>();
			var customStaffingLevels = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);

			if (includeAny)
				personnelStauses.Add(new PersonnelStatusJson() { Id = -1, Name = "Any" });

			if (customStaffingLevels != null)
			{
				foreach (var detail in customStaffingLevels.GetActiveDetails())
				{
					var status = new PersonnelStatusJson();
					status.Id = detail.CustomStateDetailId;
					status.Name = detail.ButtonText;

					personnelStauses.Add(status);
				}
			}
			else
			{
				personnelStauses.Add(new PersonnelStatusJson() { Id = 0, Name = "Available" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 1, Name = "Delayed" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 2, Name = "Unavailable" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 3, Name = "Committed" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 4, Name = "On Shift" });
			}

			return Json(personnelStauses);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.CustomStates_View)]
		public async Task<IActionResult> GetUnitStatusesLevelsForDepartment(bool includeAny, int unitTypeId)
		{
			List<PersonnelStatusJson> personnelStauses = new List<PersonnelStatusJson>();
			CustomState customState = null;

			var unitType = await _unitsService.GetUnitTypeByIdAsync(unitTypeId);

			if (unitType.CustomStatesId.HasValue && unitType.CustomStatesId.Value > 0)
				customState = await _customStateService.GetCustomSateByIdAsync(unitType.CustomStatesId.Value);

			if (includeAny)
				personnelStauses.Add(new PersonnelStatusJson() { Id = -1, Name = "Any" });

			if (customState != null)
			{
				foreach (var detail in customState.GetActiveDetails())
				{
					var status = new PersonnelStatusJson();
					status.Id = detail.CustomStateDetailId;
					status.Name = detail.ButtonText;

					personnelStauses.Add(status);
				}
			}
			else
			{
				personnelStauses.Add(new PersonnelStatusJson() { Id = 0, Name = "Available" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 1, Name = "Delayed" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 2, Name = "Unavailable" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 3, Name = "Committed" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 4, Name = "Out Of Service" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 5, Name = "Responding" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 6, Name = "On Scene" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 7, Name = "Staging" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 8, Name = "Returning" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 9, Name = "Cancelled" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 10, Name = "Released" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 11, Name = "Manual" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 12, Name = "Enroute" });
		}

			return Json(personnelStauses);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.CustomStates_View)]
		public async Task<IActionResult> GetUnitStatusesLevelsForDepartmentCombined(bool includeAny)
		{
			List<PersonnelStatusJson> personnelStauses = new List<PersonnelStatusJson>();
			var customStates = await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId);

			if (includeAny)
				personnelStauses.Add(new PersonnelStatusJson() { Id = -1, Name = "Any" });

			if (customStates != null && customStates.Any())
			{
				foreach (var customState in customStates)
				{
					foreach (var detail in customState.GetActiveDetails())
					{
						var status = new PersonnelStatusJson();
						status.Id = detail.CustomStateDetailId;
						status.Name = $"{customState.Name}:{detail.ButtonText}";

						personnelStauses.Add(status);
					}
				}
			}
			else
			{
				personnelStauses.Add(new PersonnelStatusJson() { Id = 0, Name = "Available" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 1, Name = "Delayed" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 2, Name = "Unavailable" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 3, Name = "Committed" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 4, Name = "Out Of Service" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 5, Name = "Responding" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 6, Name = "On Scene" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 7, Name = "Staging" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 8, Name = "Returning" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 9, Name = "Cancelled" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 10, Name = "Released" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 11, Name = "Manual" });
				personnelStauses.Add(new PersonnelStatusJson() { Id = 12, Name = "Enroute" });
			}

			return Json(personnelStauses);
		}
		#endregion Async Methods
	}
}
