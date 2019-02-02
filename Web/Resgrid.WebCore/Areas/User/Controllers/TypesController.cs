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
using Resgrid.WebCore.Areas.User.Models.Types;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class TypesController : SecureBaseController
	{
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;
		private readonly ICallsService _callsService;
		private readonly IAudioValidatorProvider _audioValidatorProvider;

		public TypesController(IUnitsService unitsService, ICustomStateService customStateService, ICallsService callsService, IAudioValidatorProvider audioValidatorProvider)
		{
			_unitsService = unitsService;
			_customStateService = customStateService;
			_callsService = callsService;
			_audioValidatorProvider = audioValidatorProvider;
		}

		[HttpGet]
		public IActionResult EditUnitType(int unitTypeId)
		{
			var model = new EditUnitTypeView();
			model.UnitType = _unitsService.GetUnitTypeById(unitTypeId);

			var states = new List<CustomState>();
			states.Add(new CustomState
			{
				Name = "Standard Actions"
			});
			states.AddRange(_customStateService.GetAllActiveUnitStatesForDepartment(DepartmentId));
			model.States = states;
			model.UnitCustomStatesId = model.UnitType.CustomStatesId.GetValueOrDefault();

			return View(model);
		}

		[HttpPost]
		public IActionResult EditUnitType(EditUnitTypeView model)
		{
			var states = new List<CustomState>();
			states.Add(new CustomState
			{
				Name = "Standard Actions"
			});
			states.AddRange(_customStateService.GetAllActiveUnitStatesForDepartment(DepartmentId));
			model.States = states;

			var unitTypes = _unitsService.GetUnitTypesForDepartment(DepartmentId);
			if (unitTypes.Any(x => x.Type == model.UnitType.Type && x.UnitTypeId != model.UnitType.UnitTypeId))
				ModelState.AddModelError("Type", string.Format("A Unit Type of ({0}) already exists.", model.UnitType.Type));

			if (ModelState.IsValid)
			{
				var unitType = _unitsService.GetUnitTypeById(model.UnitType.UnitTypeId);
				unitType.Type = model.UnitType.Type;

				if (model.UnitCustomStatesId != 0)
					unitType.CustomStatesId = model.UnitCustomStatesId;
				else
					unitType.CustomStatesId = null;

				_unitsService.SaveUnitType(unitType);

				return RedirectToAction("Types", "Department", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		public IActionResult NewCallPriority()
		{
			var model = new NewCallPriorityView();
			model.CallPriority = new DepartmentCallPriority();
			model.AlertSounds = model.AudioType.ToSelectListInt();

			return View(model);
		}

		[HttpPost]
		public IActionResult NewCallPriority(NewCallPriorityView model, IFormFile pushfileToUpload, IFormFile iOSPushfileToUpload, IFormFile alertfileToUpload)
		{
			var priotiries = _callsService.GetCallPrioritesForDepartment(DepartmentId, true);

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

			if (pushfileToUpload != null && pushfileToUpload.Length > 0)
			{
				var extenion = Path.GetExtension(pushfileToUpload.FileName);

				if (!String.IsNullOrWhiteSpace(extenion))
					extenion = extenion.ToLower();

				if (extenion != ".mp3")
					ModelState.AddModelError("pushfileToUpload", string.Format("Audio file type ({0}) is not supported for Push Notifications.", extenion));

				if (pushfileToUpload.Length > 1000000)
					ModelState.AddModelError("pushfileToUpload", "Android Push Audio file is too large, must be smaller then 1MB.");

				var fileAudioLength = _audioValidatorProvider.GetMp3FileDuration(pushfileToUpload.OpenReadStream());
				if (fileAudioLength == null)
					ModelState.AddModelError("pushfileToUpload", string.Format("Audio file type ({0}) is not supported for Android Push Notifications. MP3 Files are required.", extenion));
				else if (fileAudioLength != null && fileAudioLength.Value > new TimeSpan(0, 0, 25))
					ModelState.AddModelError("pushfileToUpload", string.Format("Android Push audio file length is longer then 25 seconds. Android Push notification sounds must be 25 seconds or shorter.", extenion));
			}

			if (iOSPushfileToUpload != null && iOSPushfileToUpload.Length > 0)
			{
				var extenion = Path.GetExtension(iOSPushfileToUpload.FileName);

				if (!String.IsNullOrWhiteSpace(extenion))
					extenion = extenion.ToLower();

				if (extenion != ".caf")
					ModelState.AddModelError("iOSPushfileToUpload", string.Format("Audio file type ({0}) is not supported for iOS Push Notifications.", extenion));

				if (iOSPushfileToUpload.Length > 1000000)
					ModelState.AddModelError("iOSPushfileToUpload", "iOS Push Audio file is too large, must be smaller then 1MB.");

				//var fileAudioLength = _audioValidatorProvider.GetWavFileDuration(pushfileToUpload.OpenReadStream());
				//if (fileAudioLength == null)
				//	ModelState.AddModelError("iOSPushfileToUpload", string.Format("Audio file type ({0}) is not supported for Push Notifications. CAF Files are required.", extenion));
				//else if (fileAudioLength != null && fileAudioLength.Value > new TimeSpan(0, 0, 25))
				//	ModelState.AddModelError("iOSPushfileToUpload", string.Format("iOS Push audio file length is longer then 25 seconds. iOS Push notification sounds must be 25 seconds or shorter.", extenion));
			}

			if (alertfileToUpload != null && alertfileToUpload.Length > 0)
			{
				var extenion = Path.GetExtension(alertfileToUpload.FileName);

				if (!String.IsNullOrWhiteSpace(extenion))
					extenion = extenion.ToLower();

				if (extenion != ".wav")
					ModelState.AddModelError("alertfileToUpload", string.Format("Audio file type ({0}) is not supported for Alert Notifications.", extenion));

				if (alertfileToUpload.Length > 1000000)
					ModelState.AddModelError("alertfileToUpload", "Push Audio file is too large, must be smaller then 1MB.");

				var fileAudioLength = _audioValidatorProvider.GetWavFileDuration(alertfileToUpload.OpenReadStream());
				if (fileAudioLength == null)
					ModelState.AddModelError("alertfileToUpload", string.Format("Audio file type ({0}) is not supported for Browser Alert Notifications. WAV Files are required.", extenion));
				else if (fileAudioLength != null && fileAudioLength.Value > new TimeSpan(0, 0, 5))
					ModelState.AddModelError("alertfileToUpload", string.Format("Browser alert audio file length is longer then 5 seconds. Push notification sounds must be 5 seconds or shorter.", extenion));
			}

			if (String.IsNullOrWhiteSpace(model.CallPriority.Name))
			{
				ModelState.AddModelError("CallPriority_Name", "You must specify a call priority name");
			}

			if (ModelState.IsValid)
			{
				if (alertfileToUpload != null && alertfileToUpload.Length > 0)
				{
					byte[] uploadedFile = new byte[alertfileToUpload.OpenReadStream().Length];
					alertfileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

					model.CallPriority.ShortNotificationSound = uploadedFile;
				}

				if (pushfileToUpload != null && pushfileToUpload.Length > 0)
				{
					byte[] uploadedFile = new byte[pushfileToUpload.OpenReadStream().Length];
					pushfileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

					model.CallPriority.PushNotificationSound = uploadedFile;
				}

				if (iOSPushfileToUpload != null && iOSPushfileToUpload.Length > 0)
				{
					byte[] uploadedFile = new byte[iOSPushfileToUpload.OpenReadStream().Length];
					iOSPushfileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

					model.CallPriority.IOSPushNotificationSound = uploadedFile;
				}

				model.CallPriority.DepartmentId = DepartmentId;

				_callsService.SaveCallPriority(model.CallPriority);

				return RedirectToAction("Types", "Department", new { Area = "User" });
			}


			return View(model);
		}

		[HttpGet]
		public IActionResult DeleteCallPriority(int priorityId)
		{
			var priority = _callsService.GetCallPrioritesById(DepartmentId, priorityId, true);

			if (priority != null)
			{
				priority.IsDeleted = true;
				_callsService.SaveCallPriority(priority);
			}

			return RedirectToAction("Types", "Department", new { Area = "User" });
		}

		[HttpGet]
		public IActionResult EditCallPriority(int priorityId)
		{
			var model = new EditCallPriorityView();
			model.CallPriority = _callsService.GetCallPrioritesById(DepartmentId, priorityId, true);

			if (model.CallPriority == null || model.CallPriority.DepartmentId != DepartmentId)
				Unauthorized();

			model.AlertSounds = model.AudioType.ToSelectListInt();

			return View(model);
		}

		[HttpPost]
		public IActionResult EditCallPriority(EditCallPriorityView model, IFormFile pushfileToUpload, IFormFile iOSPushfileToUpload, IFormFile alertfileToUpload)
		{
			var priotiries = _callsService.GetCallPrioritesForDepartment(DepartmentId, true);
			if (model.CallPriority.IsDefault && priotiries.Any(x => x.IsDefault && x.DepartmentCallPriorityId != model.CallPriority.DepartmentCallPriorityId))
			{
				model.Message = "ERROR: Can only have 1 default call priorty and there already is a call priority marked as default for your department.";
				return View(model);
			}

			if (pushfileToUpload != null && pushfileToUpload.Length > 0)
			{
				var extenion = Path.GetExtension(pushfileToUpload.FileName);

				if (!String.IsNullOrWhiteSpace(extenion))
					extenion = extenion.ToLower();

				if (extenion != "wav")
					ModelState.AddModelError("pushfileToUpload", string.Format("Audio file type ({0}) is not supported for Push Notifications.", extenion));

				if (pushfileToUpload.Length > 1000000)
					ModelState.AddModelError("pushfileToUpload", "Push Audio file is too large, must be smaller then 1MB.");

				var fileAudioLength = _audioValidatorProvider.GetWavFileDuration(pushfileToUpload.OpenReadStream());
				if (fileAudioLength == null)
					ModelState.AddModelError("pushfileToUpload", string.Format("Audio file type ({0}) is not supported for Push Notifications. WAV Files are required.", extenion));
				else if (fileAudioLength != null && fileAudioLength.Value > new TimeSpan(0, 0, 25))
					ModelState.AddModelError("pushfileToUpload", string.Format("Push audio file length is longer then 25 seconds. Push notification sounds must be 25 seconds or shorter.", extenion));
			}

			if (iOSPushfileToUpload != null && iOSPushfileToUpload.Length > 0)
			{
				var extenion = Path.GetExtension(iOSPushfileToUpload.FileName);

				if (!String.IsNullOrWhiteSpace(extenion))
					extenion = extenion.ToLower();

				if (extenion != ".caf")
					ModelState.AddModelError("iOSPushfileToUpload", string.Format("Audio file type ({0}) is not supported for iOS Push Notifications.", extenion));

				if (iOSPushfileToUpload.Length > 1000000)
					ModelState.AddModelError("iOSPushfileToUpload", "iOS Push Audio file is too large, must be smaller then 1MB.");

				//var fileAudioLength = _audioValidatorProvider.GetWavFileDuration(pushfileToUpload.OpenReadStream());
				//if (fileAudioLength == null)
				//	ModelState.AddModelError("iOSPushfileToUpload", string.Format("Audio file type ({0}) is not supported for Push Notifications. CAF Files are required.", extenion));
				//else if (fileAudioLength != null && fileAudioLength.Value > new TimeSpan(0, 0, 25))
				//	ModelState.AddModelError("iOSPushfileToUpload", string.Format("iOS Push audio file length is longer then 25 seconds. iOS Push notification sounds must be 25 seconds or shorter.", extenion));
			}

			if (alertfileToUpload != null && alertfileToUpload.Length > 0)
			{
				var extenion = Path.GetExtension(alertfileToUpload.FileName);

				if (!String.IsNullOrWhiteSpace(extenion))
					extenion = extenion.ToLower();

				if (extenion != "wav")
					ModelState.AddModelError("alertfileToUpload", string.Format("Audio file type ({0}) is not supported for Alert Notifications.", extenion));

				if (alertfileToUpload.Length > 1000000)
					ModelState.AddModelError("alertfileToUpload", "Push Audio file is too large, must be smaller then 1MB.");

				var fileAudioLength = _audioValidatorProvider.GetWavFileDuration(alertfileToUpload.OpenReadStream());
				if (fileAudioLength == null)
					ModelState.AddModelError("alertfileToUpload", string.Format("Audio file type ({0}) is not supported for Browser Alert Notifications. WAV Files are required.", extenion));
				else if (fileAudioLength != null && fileAudioLength.Value > new TimeSpan(0, 0, 5))
					ModelState.AddModelError("alertfileToUpload", string.Format("Browser alert audio file length is longer then 5 seconds. Push notification sounds must be 5 seconds or shorter.", extenion));
			}

			if (ModelState.IsValid)
			{
				var priority = _callsService.GetCallPrioritesById(DepartmentId, model.CallPriority.DepartmentCallPriorityId, true);
				priority.Name = model.CallPriority.Name;
				priority.Color = model.CallPriority.Color;
				priority.IsDefault = model.CallPriority.IsDefault;
				priority.DispatchPersonnel = model.CallPriority.DispatchPersonnel;
				priority.DispatchUnits = model.CallPriority.DispatchUnits;
				priority.ForceNotifyAllPersonnel = model.CallPriority.ForceNotifyAllPersonnel;
				priority.Tone = model.CallPriority.Tone;

				if (alertfileToUpload != null && alertfileToUpload.Length > 0)
				{
					byte[] uploadedFile = new byte[alertfileToUpload.OpenReadStream().Length];
					alertfileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

					priority.ShortNotificationSound = uploadedFile;
				}

				if (pushfileToUpload != null && pushfileToUpload.Length > 0)
				{
					byte[] uploadedFile = new byte[pushfileToUpload.OpenReadStream().Length];
					pushfileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

					priority.PushNotificationSound = uploadedFile;
				}


				if (iOSPushfileToUpload != null && iOSPushfileToUpload.Length > 0)
				{
					byte[] uploadedFile = new byte[iOSPushfileToUpload.OpenReadStream().Length];
					iOSPushfileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

					model.CallPriority.IOSPushNotificationSound = uploadedFile;
				}

				_callsService.SaveCallPriority(priority);
				return RedirectToAction("Types", "Department", new { Area = "User" });
			}

			return View(model);
		}
	}
}
