using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Types;
using Microsoft.AspNetCore.Http;
using Resgrid.Model.Providers;
using System.IO;
using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.WebCore.Areas.User.Models.Types;
using Resgrid.Model.Events;
using Resgrid.Framework;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;
using Resgrid.Web.Areas.User.Models.Departments;
using Microsoft.AspNetCore.Authorization;
using CallType = Resgrid.Model.CallType;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using SharpKml.Dom;
using FirebaseAdmin.Messaging;
using Resgrid.Web.Areas.User.Models.Departments.UnitSettings;
using Resgrid.Services;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class TypesController : SecureBaseController
	{
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;
		private readonly ICallsService _callsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICertificationService _certificationService;
		private readonly IDocumentsService _documentsService;
		private readonly INotesService _notesService;
		private readonly IContactsService _contactsService;

		public TypesController(IUnitsService unitsService, ICustomStateService customStateService, ICallsService callsService, IDepartmentSettingsService departmentSettingsService,
			IAuthorizationService authorizationService, IEventAggregator eventAggregator, ICertificationService certificationService, IDocumentsService documentsService,
			INotesService notesService,
			IContactsService contactsService)
		{
			_unitsService = unitsService;
			_customStateService = customStateService;
			_callsService = callsService;
			_departmentSettingsService = departmentSettingsService;
			_authorizationService = authorizationService;
			_eventAggregator = eventAggregator;
			_certificationService = certificationService;
			_documentsService = documentsService;
			_notesService = notesService;
			_contactsService = contactsService;
		}

		#region Edit Unit Type

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewUnitType()
		{
			if (!await _authorizationService.CanUserAddUnitTypeAsync(UserId))
				Unauthorized();

			var model = new EditUnitTypeView();
			model.UnitType = new UnitType();
			model.UnitType.MapIconType = -1;

			var states = new List<CustomState>();
			states.Add(new CustomState
			{
				Name = "Standard Actions"
			});
			states.AddRange(await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId));
			model.States = states;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeleteUnitType(int unitTypeId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserEditUnitTypeAsync(UserId, unitTypeId))
				Unauthorized();

			var unitType = await _unitsService.GetUnitTypeByIdAsync(unitTypeId);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Before = unitType.CloneJsonToString();
			auditEvent.Type = AuditLogTypes.UnitTypeRemoved;
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			await _unitsService.DeleteUnitTypeAsync(unitTypeId, cancellationToken);

			return RedirectToAction("Types", "Department", new { Area = "User" });
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewUnitType(EditUnitTypeView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserAddUnitTypeAsync(UserId))
				Unauthorized();

			if (String.IsNullOrWhiteSpace(model.UnitType.Type))
				ModelState.AddModelError("NewUnitType", "You Must specify the new unit type.");

			if (ModelState.IsValid)
			{
				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.After = model.UnitType.CloneJsonToString();
				auditEvent.Type = AuditLogTypes.UnitTypeAdded;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				if (model.UnitType.CustomStatesId.HasValue && model.UnitType.CustomStatesId.Value > 0)
					await _unitsService.AddUnitTypeAsync(DepartmentId, model.UnitType.Type, model.UnitType.CustomStatesId.Value, model.UnitType.MapIconType, cancellationToken);
				else
					await _unitsService.AddUnitTypeAsync(DepartmentId, model.UnitType.Type, model.UnitType.MapIconType, cancellationToken);
			}

			return RedirectToAction("Types", "Department", new { Area = "User" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> EditUnitType(int unitTypeId)
		{
			if (!await _authorizationService.CanUserEditUnitTypeAsync(UserId, unitTypeId))
				Unauthorized();

			var model = new EditUnitTypeView();
			model.UnitType = await _unitsService.GetUnitTypeByIdAsync(unitTypeId);

			var states = new List<CustomState>();
			states.Add(new CustomState
			{
				Name = "Standard Actions"
			});
			states.AddRange(await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId));
			model.States = states;
			model.UnitCustomStatesId = model.UnitType.CustomStatesId.GetValueOrDefault();

			if (model.UnitType.MapIconType.HasValue)
				model.UnitTypeIcon = model.UnitType.MapIconType.Value;

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> EditUnitType(EditUnitTypeView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserEditUnitTypeAsync(UserId, model.UnitType.UnitTypeId))
				Unauthorized();

			var states = new List<CustomState>();
			states.Add(new CustomState
			{
				Name = "Standard Actions"
			});
			states.AddRange(await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId));
			model.States = states;

			var unitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);
			if (unitTypes.Any(x => x.Type == model.UnitType.Type && x.UnitTypeId != model.UnitType.UnitTypeId))
				ModelState.AddModelError("Type", string.Format("A Unit Type of ({0}) already exists.", model.UnitType.Type));

			if (ModelState.IsValid)
			{
				var unitType = await _unitsService.GetUnitTypeByIdAsync(model.UnitType.UnitTypeId);

				var auditEvent = new AuditEvent();
				auditEvent.Before = unitType.CloneJsonToString();

				unitType.Type = model.UnitType.Type;

				if (model.UnitCustomStatesId != 0)
					unitType.CustomStatesId = model.UnitCustomStatesId;
				else
					unitType.CustomStatesId = null;

				if (model.UnitTypeIcon >= 0)
					unitType.MapIconType = model.UnitTypeIcon;
				else
					unitType.MapIconType = null;

				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.After = unitType.CloneJsonToString();
				auditEvent.Type = AuditLogTypes.UnitTypeEdited;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _unitsService.SaveUnitTypeAsync(unitType, cancellationToken);

				return RedirectToAction("Types", "Department", new { Area = "User" });
			}

			return View(model);
		}

		#endregion Edit Unit Type

		#region Edit Call Type

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewCallType()
		{
			if (!await _authorizationService.CanUserAddCallTypeAsync(UserId))
				Unauthorized();

			var model = new EditCallTypeView();
			model.CallType = new CallType();
			model.CallType.MapIconType = -1;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewCallType(EditCallTypeView model, CancellationToken cancellationToken)
		{
			if (String.IsNullOrEmpty(model.CallType.Type))
				ModelState.AddModelError("NewCallType", "You Must specify the new call type.");

			if (!await _authorizationService.CanUserAddCallTypeAsync(UserId))
				Unauthorized();

			if (ModelState.IsValid)
			{
				CallType newCallType = new CallType();
				newCallType.DepartmentId = DepartmentId;
				newCallType.Type = model.CallType.Type;

				if (model.CallTypeIcon >= 0)
					newCallType.MapIconType = model.CallTypeIcon;

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CallTypeAdded;
				auditEvent.After = newCallType.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _callsService.SaveCallTypeAsync(newCallType, cancellationToken);
			}

			return RedirectToAction("Types", "Department", new { Area = "User" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> EditCallType(int callTypeId)
		{
			if (!await _authorizationService.CanUserModifyCallTypeAsync(UserId, callTypeId))
				Unauthorized();

			var model = new EditCallTypeView();
			model.CallType = await _callsService.GetCallTypeByIdAsync(callTypeId);

			if (model.CallType.MapIconType.HasValue)
				model.CallTypeIcon = model.CallType.MapIconType.Value;
			else
				model.CallTypeIcon = -1;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> EditCallType(EditCallTypeView model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				var auditEvent = new AuditEvent();
				var callType = await _callsService.GetCallTypeByIdAsync(model.CallType.CallTypeId);

				if (!await _authorizationService.CanUserModifyCallTypeAsync(UserId, model.CallType.CallTypeId))
					Unauthorized();

				auditEvent.Before = callType.CloneJsonToString();
				callType.Type = model.CallType.Type;

				if (model.CallTypeIcon >= 0)
					callType.MapIconType = model.CallTypeIcon;
				else
					callType.MapIconType = null;


				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CallTypeEdited;
				auditEvent.After = callType.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);


				await _callsService.SaveCallTypeAsync(callType, cancellationToken);

				return RedirectToAction("Types", "Department", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeleteCallType(int callTypeId, CancellationToken cancellationToken)
		{
			var callType = await _callsService.GetCallTypeByIdAsync(callTypeId);
			if (!await _authorizationService.CanUserModifyCallTypeAsync(UserId, callTypeId))
				Unauthorized();

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.CallTypeRemoved;
			auditEvent.Before = callType.CloneJsonToString();
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			await _callsService.DeleteCallTypeAsync(callTypeId, cancellationToken);

			return RedirectToAction("Types", "Department", new { Area = "User" });
		}

		#endregion Edit Call Type

		#region Call Priority

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewCallPriority()
		{
			if (!await _authorizationService.CanUserAddCallPriorityAsync(UserId))
				Unauthorized();

			var model = new NewCallPriorityView();
			model.CallPriority = new DepartmentCallPriority();
			model.AlertSounds = model.AudioType.ToSelectListInt();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewCallPriority(NewCallPriorityView model, IFormFile pushfileToUpload, IFormFile iOSPushfileToUpload, IFormFile alertfileToUpload,
			CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserAddCallPriorityAsync(UserId))
				Unauthorized();

			var priotiries = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, true);

			if (model.CallPriority.IsDefault && priotiries.Any(x => x.IsDefault && x.DepartmentId == DepartmentId && x.IsDeleted == false))
			{
				model.Message = "ERROR: Can only have 1 default call priorty and there already is a call priority marked as default for your department.";
				return View(model);
			}

			if (priotiries.Any(x => x.Name == model.CallPriority.Name && x.IsDeleted == false))
			{
				model.Message = "ERROR: Cannot have duplicate Call Priority names.";
				return View(model);
			}

			//if (pushfileToUpload != null && pushfileToUpload.Length > 0)
			//{
			//	var extenion = Path.GetExtension(pushfileToUpload.FileName);

			//	if (!String.IsNullOrWhiteSpace(extenion))
			//		extenion = extenion.ToLower();

			//	if (extenion != ".mp3")
			//		ModelState.AddModelError("pushfileToUpload", string.Format("Audio file type ({0}) is not supported for Push Notifications.", extenion));

			//	if (pushfileToUpload.Length > 1000000)
			//		ModelState.AddModelError("pushfileToUpload", "Android Push Audio file is too large, must be smaller then 1MB.");
			//}

			//if (iOSPushfileToUpload != null && iOSPushfileToUpload.Length > 0)
			//{
			//	var extenion = Path.GetExtension(iOSPushfileToUpload.FileName);

			//	if (!String.IsNullOrWhiteSpace(extenion))
			//		extenion = extenion.ToLower();

			//	if (extenion != ".caf")
			//		ModelState.AddModelError("iOSPushfileToUpload", string.Format("Audio file type ({0}) is not supported for iOS Push Notifications.", extenion));

			//	if (iOSPushfileToUpload.Length > 1000000)
			//		ModelState.AddModelError("iOSPushfileToUpload", "iOS Push Audio file is too large, must be smaller then 1MB.");

			//	//var fileAudioLength = await _audioValidatorProvider.GetWavFileDuration(pushfileToUpload.OpenReadStream());
			//	//if (fileAudioLength == null)
			//	//	ModelState.AddModelError("iOSPushfileToUpload", string.Format("Audio file type ({0}) is not supported for Push Notifications. CAF Files are required.", extenion));
			//	//else if (fileAudioLength != null && fileAudioLength.Value > new TimeSpan(0, 0, 25))
			//	//	ModelState.AddModelError("iOSPushfileToUpload", string.Format("iOS Push audio file length is longer then 25 seconds. iOS Push notification sounds must be 25 seconds or shorter.", extenion));
			//}

			if (alertfileToUpload != null && alertfileToUpload.Length > 0)
			{
				var extenion = Path.GetExtension(alertfileToUpload.FileName);

				if (!String.IsNullOrWhiteSpace(extenion))
					extenion = extenion.ToLower();

				if (extenion != ".wav")
					ModelState.AddModelError("alertfileToUpload", string.Format("Audio file type ({0}) is not supported for Alert Notifications.", extenion));

				if (alertfileToUpload.Length > 1000000)
					ModelState.AddModelError("alertfileToUpload", "Push Audio file is too large, must be smaller then 1MB.");
			}

			if (String.IsNullOrWhiteSpace(model.CallPriority.Name))
			{
				ModelState.AddModelError("CallPriority_Name", "You must specify a call priority name");
			}

			if (ModelState.IsValid)
			{
				//if (alertfileToUpload != null && alertfileToUpload.Length > 0)
				//{
				//	byte[] uploadedFile = new byte[alertfileToUpload.OpenReadStream().Length];
				//	alertfileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

				//	model.CallPriority.ShortNotificationSound = uploadedFile;
				//}

				//if (pushfileToUpload != null && pushfileToUpload.Length > 0)
				//{
				//	byte[] uploadedFile = new byte[pushfileToUpload.OpenReadStream().Length];
				//	pushfileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

				//	model.CallPriority.PushNotificationSound = uploadedFile;
				//}

				//if (iOSPushfileToUpload != null && iOSPushfileToUpload.Length > 0)
				//{
				//	byte[] uploadedFile = new byte[iOSPushfileToUpload.OpenReadStream().Length];
				//	iOSPushfileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

				//	model.CallPriority.IOSPushNotificationSound = uploadedFile;
				//}

				model.CallPriority.DepartmentId = DepartmentId;

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CallPriorityAdded;
				auditEvent.After = model.CallPriority.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _callsService.SaveCallPriorityAsync(model.CallPriority, cancellationToken);

				return RedirectToAction("Types", "Department", new { Area = "User" });
			}


			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeleteCallPriority(int priorityId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserDeleteCallPriorityAsync(UserId, priorityId))
				Unauthorized();

			var priority = await _callsService.GetCallPrioritiesByIdAsync(DepartmentId, priorityId, true);

			if (priority != null)
			{
				var auditEvent = new AuditEvent();
				auditEvent.Before = priority.CloneJsonToString();

				priority.IsDeleted = true;

				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CallPriorityRemoved;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _callsService.SaveCallPriorityAsync(priority, cancellationToken);
			}

			return RedirectToAction("Types", "Department", new { Area = "User" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> EditCallPriority(int priorityId)
		{
			if (!await _authorizationService.CanUserEditCallPriorityAsync(UserId, priorityId))
				Unauthorized();

			var model = new EditCallPriorityView();
			model.CallPriority = await _callsService.GetCallPrioritiesByIdAsync(DepartmentId, priorityId, true);

			if (model.CallPriority == null || model.CallPriority.DepartmentId != DepartmentId)
				Unauthorized();

			model.AlertSounds = model.AudioType.ToSelectListInt();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> EditCallPriority(EditCallPriorityView model, IFormFile pushfileToUpload, IFormFile iOSPushfileToUpload, IFormFile alertfileToUpload,
			CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserEditCallPriorityAsync(UserId, model.CallPriority.DepartmentCallPriorityId))
				Unauthorized();

			var priotiries = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, true);
			if (model.CallPriority.IsDefault && priotiries.Any(x => x.IsDefault && x.DepartmentCallPriorityId != model.CallPriority.DepartmentCallPriorityId))
			{
				model.Message = "ERROR: Can only have 1 default call priorty and there already is a call priority marked as default for your department.";
				return View(model);
			}

			//if (pushfileToUpload != null && pushfileToUpload.Length > 0)
			//{
			//	var extenion = Path.GetExtension(pushfileToUpload.FileName);

			//	if (!String.IsNullOrWhiteSpace(extenion))
			//		extenion = extenion.ToLower();

			//	if (extenion != "wav")
			//		ModelState.AddModelError("pushfileToUpload", string.Format("Audio file type ({0}) is not supported for Push Notifications.", extenion));

			//	if (pushfileToUpload.Length > 1000000)
			//		ModelState.AddModelError("pushfileToUpload", "Push Audio file is too large, must be smaller then 1MB.");
			//}

			//if (iOSPushfileToUpload != null && iOSPushfileToUpload.Length > 0)
			//{
			//	var extenion = Path.GetExtension(iOSPushfileToUpload.FileName);

			//	if (!String.IsNullOrWhiteSpace(extenion))
			//		extenion = extenion.ToLower();

			//	if (extenion != ".caf")
			//		ModelState.AddModelError("iOSPushfileToUpload", string.Format("Audio file type ({0}) is not supported for iOS Push Notifications.", extenion));

			//	if (iOSPushfileToUpload.Length > 1000000)
			//		ModelState.AddModelError("iOSPushfileToUpload", "iOS Push Audio file is too large, must be smaller then 1MB.");

			//	//var fileAudioLength = await _audioValidatorProvider.GetWavFileDuration(pushfileToUpload.OpenReadStream());
			//	//if (fileAudioLength == null)
			//	//	ModelState.AddModelError("iOSPushfileToUpload", string.Format("Audio file type ({0}) is not supported for Push Notifications. CAF Files are required.", extenion));
			//	//else if (fileAudioLength != null && fileAudioLength.Value > new TimeSpan(0, 0, 25))
			//	//	ModelState.AddModelError("iOSPushfileToUpload", string.Format("iOS Push audio file length is longer then 25 seconds. iOS Push notification sounds must be 25 seconds or shorter.", extenion));
			//}

			//if (alertfileToUpload != null && alertfileToUpload.Length > 0)
			//{
			//	var extenion = Path.GetExtension(alertfileToUpload.FileName);

			//	if (!String.IsNullOrWhiteSpace(extenion))
			//		extenion = extenion.ToLower();

			//	if (extenion != "wav")
			//		ModelState.AddModelError("alertfileToUpload", string.Format("Audio file type ({0}) is not supported for Alert Notifications.", extenion));

			//	if (alertfileToUpload.Length > 1000000)
			//		ModelState.AddModelError("alertfileToUpload", "Push Audio file is too large, must be smaller then 1MB.");
			//}

			if (ModelState.IsValid)
			{
				var priority = await _callsService.GetCallPrioritiesByIdAsync(DepartmentId, model.CallPriority.DepartmentCallPriorityId, true);

				var auditEvent = new AuditEvent();
				auditEvent.Before = priority.CloneJsonToString();

				priority.Name = model.CallPriority.Name;
				priority.Color = model.CallPriority.Color;
				priority.IsDefault = model.CallPriority.IsDefault;
				priority.DispatchPersonnel = model.CallPriority.DispatchPersonnel;
				priority.DispatchUnits = model.CallPriority.DispatchUnits;
				priority.ForceNotifyAllPersonnel = model.CallPriority.ForceNotifyAllPersonnel;
				priority.Tone = model.CallPriority.Tone;

				//if (alertfileToUpload != null && alertfileToUpload.Length > 0)
				//{
				//	byte[] uploadedFile = new byte[alertfileToUpload.OpenReadStream().Length];
				//	alertfileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

				//	priority.ShortNotificationSound = uploadedFile;
				//}

				//if (pushfileToUpload != null && pushfileToUpload.Length > 0)
				//{
				//	byte[] uploadedFile = new byte[pushfileToUpload.OpenReadStream().Length];
				//	pushfileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

				//	priority.PushNotificationSound = uploadedFile;
				//}


				//if (iOSPushfileToUpload != null && iOSPushfileToUpload.Length > 0)
				//{
				//	byte[] uploadedFile = new byte[iOSPushfileToUpload.OpenReadStream().Length];
				//	iOSPushfileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

				//	model.CallPriority.IOSPushNotificationSound = uploadedFile;
				//}

				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CallPriorityEdited;
				auditEvent.After = priority.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _callsService.SaveCallPriorityAsync(priority, cancellationToken);
				return RedirectToAction("Types", "Department", new { Area = "User" });
			}

			return View(model);
		}

		#endregion Call Priority

		#region Certification Types

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeleteCertificationType(int certificationTypeId, CancellationToken cancellationToken)
		{
			if (certificationTypeId <= 0)
				return RedirectToAction("Types", "Department", new { Area = "User" });

			if (!await _authorizationService.CanUserDeleteCertificationTypeAsync(UserId, certificationTypeId))
				Unauthorized();

			var type = await _certificationService.GetCertificationTypeByIdAsync(certificationTypeId);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Before = type.CloneJsonToString();
			auditEvent.Type = AuditLogTypes.CertificationTypeRemoved;
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			await _certificationService.DeleteCertificationTypeByIdAsync(certificationTypeId, cancellationToken);

			return RedirectToAction("Types", "Department", new { Area = "User" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewCertificationType()
		{
			if (!await _authorizationService.CanUserAddCertificationTypeAsync(UserId))
				Unauthorized();

			var model = new NewCertificationTypeView();
			model.Type = new DepartmentCertificationType();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewCertificationType(NewCertificationTypeView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserAddCertificationTypeAsync(UserId))
				Unauthorized();

			if (String.IsNullOrEmpty(model.Type.Type))
				ModelState.AddModelError("NewCertificationType", "You Must specify the new certification type.");

			if (await _certificationService.DoesCertificationTypeAlreadyExistAsync(DepartmentId, model.Type.Type))
				ModelState.AddModelError("Name", "Supplied new Certification Type already exists, please change the name of the Certification Type and try again.");

			if (ModelState.IsValid)
			{
				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.After = model.Type.CloneJsonToString();
				auditEvent.Type = AuditLogTypes.CertificationTypeAdded;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _certificationService.SaveNewCertificationTypeAsync(model.Type.Type.Trim(), DepartmentId, cancellationToken);

				return RedirectToAction("Types", "Department", new { Area = "User" });
			}

			return View(model);
		}

		#endregion Certification Types

		#region Document Types

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewDocumentType()
		{
			if (!await _authorizationService.CanUserAddDocumentTypeAsync(UserId))
				Unauthorized();

			var model = new NewDocumentCategoryView();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewDocumentType(NewDocumentCategoryView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserAddDocumentTypeAsync(UserId))
				Unauthorized();

			if (String.IsNullOrEmpty(model.Name))
				ModelState.AddModelError("Name", "You Must specify the new document category name.");

			if (await _documentsService.DoesDocumentCategoryAlreadyExistAsync(DepartmentId, model.Name))
				ModelState.AddModelError("Name", "Supplied new Document Category already exists, please change the category name and try again.");

			if (ModelState.IsValid)
			{
				var category = new DocumentCategory();
				category.DepartmentId = DepartmentId;
				category.AddedById = UserId;
				category.AddedOn = DateTime.UtcNow;
				category.Name = model.Name.Trim();

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.After = category.CloneJsonToString();
				auditEvent.Type = AuditLogTypes.DocumentCategoryAdded;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _documentsService.SaveDocumentCategoryAsync(category, cancellationToken);

				return RedirectToAction("Types", "Department", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeleteDocumentType(string documentTypeId, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(documentTypeId))
				return RedirectToAction("Types", "Department", new { Area = "User" });

			if (!await _authorizationService.CanUserDeleteDocumentTypeAsync(UserId, documentTypeId))
				Unauthorized();

			var type = await _documentsService.GetDocumentCategoryByIdAsync(documentTypeId);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Before = type.CloneJsonToString();
			auditEvent.Type = AuditLogTypes.DocumentCategoryRemoved;
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			await _documentsService.DeleteDocumentCategoryAsync(type, cancellationToken);

			return RedirectToAction("Types", "Department", new { Area = "User" });
		}

		#endregion Document Types

		#region Note Types

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewNoteType()
		{
			if (!await _authorizationService.CanUserAddNoteTypeAsync(UserId))
				Unauthorized();

			var model = new NewNoteCategoryView();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewNoteType(NewNoteCategoryView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserAddNoteTypeAsync(UserId))
				Unauthorized();

			if (String.IsNullOrEmpty(model.Name))
				ModelState.AddModelError("Name", "You Must specify the new note category name.");

			if (await _notesService.DoesNoteTypeAlreadyExistAsync(DepartmentId, model.Name))
				ModelState.AddModelError("Name", "Supplied new Note Type already exists, please change the type name and try again.");

			if (ModelState.IsValid)
			{
				var category = new NoteCategory();
				category.DepartmentId = DepartmentId;
				category.AddedById = UserId;
				category.AddedOn = DateTime.UtcNow;
				category.Name = model.Name.Trim();

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.After = category.CloneJsonToString();
				auditEvent.Type = AuditLogTypes.NoteCategoryAdded;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _notesService.SaveNoteCategoryAsync(category, cancellationToken);

				return RedirectToAction("Types", "Department", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeleteNoteType(string noteTypeId, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(noteTypeId))
				return RedirectToAction("Types", "Department", new { Area = "User" });

			if (!await _authorizationService.CanUserDeleteNoteTypeAsync(UserId, noteTypeId))
				Unauthorized();

			var type = await _notesService.GetNoteCategoryByIdAsync(noteTypeId);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Before = type.CloneJsonToString();
			auditEvent.Type = AuditLogTypes.NoteCategoryRemoved;
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			await _notesService.DeleteNoteCategoryAsync(type, cancellationToken);

			return RedirectToAction("Types", "Department", new { Area = "User" });
		}

		#endregion Note Types

		#region List Ordering

		[HttpGet]
		public async Task<IActionResult> ListOrdering()
		{
			var model = new ListOrderingView();
			model.AllPersonnelStatuses = await _customStateService.GetCustomPersonnelStatusesOrDefaultsAsync(DepartmentId);
			model.PersonnelStatusOrders = await _departmentSettingsService.GetDepartmentPersonnelListStatusSortOrderAsync(DepartmentId);

			if (model.PersonnelStatusOrders == null)
				model.PersonnelStatusOrders = new List<PersonnelListStatusOrder>();

			if (model.AllPersonnelStatuses != null)
			{
				var availableStatuses = from status in model.AllPersonnelStatuses
					where !model.PersonnelStatusOrders.Select(x => x.StatusId).Contains(status.CustomStateDetailId)
					select status;

				model.AvailablePersonnelStatuses = availableStatuses.ToList();
			}

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> SavePersonnelStatusListOrdering(IFormCollection form, CancellationToken cancellationToken)
		{
			List<int> options = (from object key in form.Keys
				where key.ToString().StartsWith("personnelStatus_")
				select int.Parse(key.ToString().Replace("personnelStatus_", ""))).ToList();

			if (options != null || options.Any())
			{
				var personnelStatusOrdering = new List<PersonnelListStatusOrder>();

				foreach (var i in options)
				{
					if (form.ContainsKey("personnelStatus_" + i))
					{
						var weight = form["personnelStatus_" + i];
						var statusId = form["personnelStatusValue_" + i];

						var statusOrder = new PersonnelListStatusOrder();
						statusOrder.Weight = int.Parse(weight);
						statusOrder.StatusId = int.Parse(statusId);

						personnelStatusOrdering.Add(statusOrder);
					}
				}

				if (personnelStatusOrdering.Any())
					personnelStatusOrdering = personnelStatusOrdering.OrderBy(x => x.Weight).ToList();

				await _departmentSettingsService.SetDepartmentPersonnelListStatusSortOrderAsync(DepartmentId, personnelStatusOrdering, cancellationToken);
			}

			return RedirectToAction("ListOrdering");
		}

		[HttpGet]
		public async Task<IActionResult> DeletePersonnelListStatus(int statusId, CancellationToken cancellationToken)
		{
			var personnelStatusOrders = await _departmentSettingsService.GetDepartmentPersonnelListStatusSortOrderAsync(DepartmentId);

			personnelStatusOrders.RemoveAll(x => x.StatusId == statusId);

			if (personnelStatusOrders.Any())
				personnelStatusOrders = personnelStatusOrders.OrderBy(x => x.Weight).ToList();

			await _departmentSettingsService.SetDepartmentPersonnelListStatusSortOrderAsync(DepartmentId, personnelStatusOrders, cancellationToken);

			return RedirectToAction("ListOrdering");
		}

		#endregion List Ordering

		#region Contact Note Types

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewContactNoteType()
		{
			if (!await _authorizationService.CanUserAddNoteTypeAsync(UserId))
				Unauthorized();

			var model = new NewContactNoteCategoryView();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> NewContactNoteType(NewContactNoteCategoryView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserAddNoteTypeAsync(UserId))
				Unauthorized();

			if (String.IsNullOrEmpty(model.Name))
				ModelState.AddModelError("Name", "You Must specify the new contact note type name.");

			if (await _contactsService.DoesContactNoteTypeAlreadyExistAsync(DepartmentId, model.Name))
				ModelState.AddModelError("Name", "Supplied new Contact Note Type already exists, please change the type name and try again.");

			if (ModelState.IsValid)
			{
				var type = new ContactNoteType();
				type.DepartmentId = DepartmentId;
				type.AddedByUserId = UserId;
				type.AddedOn = DateTime.UtcNow;
				type.Name = model.Name.Trim();
				type.Color = model.Color.Trim();

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.After = type.CloneJsonToString();
				auditEvent.Type = AuditLogTypes.ContactNoteTypeAdded;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _contactsService.SaveContactNoteTypeAsync(type, cancellationToken);

				return RedirectToAction("Types", "Department", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeleteContactNoteType(string contactNoteTypeId, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(contactNoteTypeId))
				return RedirectToAction("Types", "Department", new { Area = "User" });

			if (!await _authorizationService.CanUserDeleteContactNoteTypeAsync(UserId, contactNoteTypeId))
				Unauthorized();

			var type = await _contactsService.GetContactNoteTypeByIdAsync(contactNoteTypeId);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Before = type.CloneJsonToString();
			auditEvent.Type = AuditLogTypes.ContactNoteTypeRemoved;
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			await _contactsService.DeleteContactNoteTypeAsync(type, cancellationToken);

			return RedirectToAction("Types", "Department", new { Area = "User" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> EditContactNoteType(string typeId)
		{
			if (!await _authorizationService.CanUserAddNoteTypeAsync(UserId))
				Unauthorized();

			var type = await _contactsService.GetContactNoteTypeByIdAsync(typeId);

			if (!await _authorizationService.CanUserEditContactNoteTypeAsync(UserId, typeId))
				Unauthorized();

			var model = new NewContactNoteCategoryView();
			model.TypeId = type.ContactNoteTypeId;
			model.Name = type.Name;
			model.Color = type.Color;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> EditContactNoteType(NewContactNoteCategoryView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserEditContactNoteTypeAsync(UserId, model.TypeId))
				Unauthorized();

			if (String.IsNullOrEmpty(model.Name))
				ModelState.AddModelError("Name", "You Must specify the new contact note type name.");

			if (await _contactsService.DoesContactNoteTypeAlreadyExistAsync(DepartmentId, model.Name))
				ModelState.AddModelError("Name", "Supplied new Contact Note Type already exists, please change the type name and try again.");

			if (ModelState.IsValid)
			{
				var type = await _contactsService.GetContactNoteTypeByIdAsync(model.TypeId);
				type.DepartmentId = DepartmentId;
				type.EditedByUserId = UserId;
				type.EditedOn = DateTime.UtcNow;
				type.Name = model.Name.Trim();
				type.Color = model.Color.Trim();

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.After = type.CloneJsonToString();
				auditEvent.Type = AuditLogTypes.ContactNoteTypeEdited;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _contactsService.SaveContactNoteTypeAsync(type, cancellationToken);

				return RedirectToAction("Types", "Department", new { Area = "User" });
			}

			return View(model);
		}

		#endregion Contact Note Types
	}
}
