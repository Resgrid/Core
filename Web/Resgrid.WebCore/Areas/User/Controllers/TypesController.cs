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

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class TypesController : SecureBaseController
	{
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;
		private readonly ICallsService _callsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public TypesController(IUnitsService unitsService, ICustomStateService customStateService, ICallsService callsService, IDepartmentSettingsService departmentSettingsService)
		{
			_unitsService = unitsService;
			_customStateService = customStateService;
			_callsService = callsService;
			_departmentSettingsService = departmentSettingsService;
		}

		#region Edit Unit Type
		[HttpGet]
		public async Task<IActionResult> EditUnitType(int unitTypeId)
		{
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
				unitType.Type = model.UnitType.Type;

				if (model.UnitCustomStatesId != 0)
					unitType.CustomStatesId = model.UnitCustomStatesId;
				else
					unitType.CustomStatesId = null;

				if (model.UnitTypeIcon >= 0)
					unitType.MapIconType = model.UnitTypeIcon;
				else
					unitType.MapIconType = null;

				await _unitsService.SaveUnitTypeAsync(unitType, cancellationToken);

				return RedirectToAction("Types", "Department", new { Area = "User" });
			}

			return View(model);
		}
		#endregion Edit Unit Type

		#region Call Priority
		[HttpGet]
		public async Task<IActionResult> NewCallPriority()
		{
			var model = new NewCallPriorityView();
			model.CallPriority = new DepartmentCallPriority();
			model.AlertSounds = model.AudioType.ToSelectListInt();

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> NewCallPriority(NewCallPriorityView model, IFormFile pushfileToUpload, IFormFile iOSPushfileToUpload, IFormFile alertfileToUpload, CancellationToken cancellationToken)
		{
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

			if (pushfileToUpload != null && pushfileToUpload.Length > 0)
			{
				var extenion = Path.GetExtension(pushfileToUpload.FileName);

				if (!String.IsNullOrWhiteSpace(extenion))
					extenion = extenion.ToLower();

				if (extenion != ".mp3")
					ModelState.AddModelError("pushfileToUpload", string.Format("Audio file type ({0}) is not supported for Push Notifications.", extenion));

				if (pushfileToUpload.Length > 1000000)
					ModelState.AddModelError("pushfileToUpload", "Android Push Audio file is too large, must be smaller then 1MB.");
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

				//var fileAudioLength = await _audioValidatorProvider.GetWavFileDuration(pushfileToUpload.OpenReadStream());
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

				await _callsService.SaveCallPriorityAsync(model.CallPriority, cancellationToken);

				return RedirectToAction("Types", "Department", new { Area = "User" });
			}


			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> DeleteCallPriority(int priorityId, CancellationToken cancellationToken)
		{
			var priority = await _callsService.GetCallPrioritiesByIdAsync(DepartmentId, priorityId, true);

			if (priority != null)
			{
				priority.IsDeleted = true;
				await _callsService.SaveCallPriorityAsync(priority, cancellationToken);
			}

			return RedirectToAction("Types", "Department", new { Area = "User" });
		}

		[HttpGet]
		public async Task<IActionResult> EditCallPriority(int priorityId)
		{
			var model = new EditCallPriorityView();
			model.CallPriority = await _callsService.GetCallPrioritiesByIdAsync(DepartmentId, priorityId, true);

			if (model.CallPriority == null || model.CallPriority.DepartmentId != DepartmentId)
				Unauthorized();

			model.AlertSounds = model.AudioType.ToSelectListInt();

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> EditCallPriority(EditCallPriorityView model, IFormFile pushfileToUpload, IFormFile iOSPushfileToUpload, IFormFile alertfileToUpload, CancellationToken cancellationToken)
		{
			var priotiries = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, true);
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

				//var fileAudioLength = await _audioValidatorProvider.GetWavFileDuration(pushfileToUpload.OpenReadStream());
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
			}

			if (ModelState.IsValid)
			{
				var priority = await _callsService.GetCallPrioritiesByIdAsync(DepartmentId, model.CallPriority.DepartmentCallPriorityId, true);
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

				await _callsService.SaveCallPriorityAsync(priority, cancellationToken);
				return RedirectToAction("Types", "Department", new { Area = "User" });
			}

			return View(model);
		}
		#endregion Call Priority

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
	}
}
