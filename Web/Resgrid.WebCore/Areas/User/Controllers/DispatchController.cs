using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models;
using Resgrid.Web.Areas.User.Models.BigBoardX;
using Resgrid.Web.Areas.User.Models.Calls;
using Resgrid.Web.Areas.User.Models.Dispatch;
using Resgrid.Model.Identity;
using Resgrid.Web.Areas.User.Models.Home;
using Resgrid.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using RestSharp;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class DispatchController : SecureBaseController
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
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUnitsService _unitsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICustomStateService _customStateService;
		private readonly ITemplatesService _templatesService;
		private readonly IPdfProvider _pdfProvider;
		private readonly IProtocolsService _protocolsService;

		public DispatchController(IDepartmentsService departmentsService, IUsersService usersService, ICallsService callsService,
			IDepartmentGroupsService departmentGroupsService, ICommunicationService communicationService, IQueueService queueService,
			Model.Services.IAuthorizationService authorizationService, IWorkLogsService workLogsService, IGeoLocationProvider geoLocationProvider,
						IPersonnelRolesService personnelRolesService, IDepartmentSettingsService departmentSettingsService, IUserProfileService userProfileService,
						IUnitsService unitsService, IActionLogsService actionLogsService, IEventAggregator eventAggregator, ICustomStateService customStateService,
						ITemplatesService templatesService, IPdfProvider pdfProvider, IProtocolsService protocolsService)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_callsService = callsService;
			_departmentGroupsService = departmentGroupsService;
			_communicationService = communicationService;
			_queueService = queueService;
			_authorizationService = authorizationService;
			_workLogsService = workLogsService;
			_geoLocationProvider = geoLocationProvider;
			_personnelRolesService = personnelRolesService;
			_departmentSettingsService = departmentSettingsService;
			_userProfileService = userProfileService;
			_unitsService = unitsService;
			_actionLogsService = actionLogsService;
			_eventAggregator = eventAggregator;
			_customStateService = customStateService;
			_templatesService = templatesService;
			_pdfProvider = pdfProvider;
			_protocolsService = protocolsService;
		}
		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> Index()
		{
			return await Dashboard();
		}

		[Authorize(Policy = ResgridResources.Call_View)]

		public async Task<IActionResult> Dashboard()
		{
			var model = new CallsDashboardModel();

			var address = await _departmentSettingsService.GetBigBoardCenterAddressDepartmentAsync(DepartmentId);
			var center = await _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartmentAsync(DepartmentId);

			if (center != null)
			{
				var points = center.Split(char.Parse(","));

				if (points != null && points.Length == 2)
				{
					model.Latitude = points[0];
					model.Longitude = points[1];
				}
			}
			else if (address != null)
			{
				string coordinates =
					await _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3} {4}", address.Address1,
						address.City, address.State, address.PostalCode, address.Country));

				if (!String.IsNullOrEmpty(coordinates))
				{
					double newLat;
					double newLon;
					var coordinatesArr = coordinates.Split(char.Parse(","));
					if (double.TryParse(coordinatesArr[0], out newLat) && double.TryParse(coordinatesArr[1], out newLon))
					{
						model.Latitude = newLat.ToString();
						model.Longitude = newLon.ToString();
					}
				}
			}

			model.NewCall = new Call();

			return View(model);
		}

		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> ArchivedCalls()
		{
			var model = new CallsDashboardModel();
			model.Years = new List<SelectListItem>();

			var years = await _callsService.GetCallYearsByDeptartmentAsync(DepartmentId);

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
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> NewCall()
		{
			var model = new NewCallView();
			model.Call = new Call();
			model = await FillNewCallView(model);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> Chat()
		{
			var modal = new ChatView();
			modal.DepartmentId = DepartmentId;
			modal.UserId = UserId;
			modal.Name = ClaimsAuthorizationHelper.GetFullName();

			return View(modal);
		}


		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> NewCall(NewCallView model, IFormCollection collection, CancellationToken cancellationToken)
		{
			model = await FillNewCallView(model);

			if (ModelState.IsValid)
			{
				model.Call.ReportingUserId = UserId;
				model.Call.LoggedOn = DateTime.UtcNow;
				model.Call.DepartmentId = DepartmentId;
				model.Call.Priority = (int)model.CallPriority;
				model.Call.State = 0;
				model.Call.NatureOfCall = System.Net.WebUtility.HtmlDecode(model.Call.NatureOfCall);
				model.Call.Notes = System.Net.WebUtility.HtmlDecode(model.Call.Notes);

				if (!String.IsNullOrWhiteSpace(model.What3Word))
					model.Call.W3W = model.What3Word;

				if (model.Call.Type == "No Type")
					model.Call.Type = null;

				if (!String.IsNullOrEmpty(model.Latitude) && !String.IsNullOrEmpty(model.Longitude))
					model.Call.GeoLocationData = string.Format("{0},{1}", model.Latitude, model.Longitude);

				List<string> dispatchingUserIds = new List<string>();
				List<int> dispatchingGroupIds = new List<int>();
				List<int> dispatchingUnitIds = new List<int>();
				List<int> dispatchingRoleIds = new List<int>();
				List<int> activeProtocols = new List<int>();
				List<int> pendingProtocols = new List<int>();

				foreach (var key in collection.Keys)
				{
					if (key.ToString().StartsWith("dispatchUser_"))
					{
						string userId = key.ToString().Replace("dispatchUser_", "");
						dispatchingUserIds.Add(userId);
					}
					else if (key.ToString().StartsWith("dispatchGroup_"))
					{
						var groupId = int.Parse(key.ToString().Replace("dispatchGroup_", ""));
						dispatchingGroupIds.Add(groupId);
					}
					else if (key.ToString().StartsWith("dispatchUnit_"))
					{
						var unitId = int.Parse(key.ToString().Replace("dispatchUnit_", ""));
						dispatchingUnitIds.Add(unitId);
					}
					else if (key.ToString().StartsWith("dispatchRole_"))
					{
						var roleId = int.Parse(key.ToString().Replace("dispatchRole_", ""));
						dispatchingRoleIds.Add(roleId);
					}
					else if (key.ToString().StartsWith("activeProtocol_"))
					{
						var activeProtocolId = int.Parse(key.ToString().Replace("activeProtocol_", ""));
						activeProtocols.Add(activeProtocolId);
					}
					else if (key.ToString().StartsWith("pendingProtocol_"))
					{
						var pendingProtocolId = int.Parse(key.ToString().Replace("pendingProtocol_", ""));

						if (collection[key] == "1")
							pendingProtocols.Add(pendingProtocolId);
					}
				}

				model.Call.Dispatches = new Collection<CallDispatch>();

				// Add all users dispatch's
				if (dispatchingUserIds.Any())
				{
					foreach (var userId in dispatchingUserIds)
					{
						CallDispatch cd = new CallDispatch();
						cd.UserId = userId;

						model.Call.Dispatches.Add(cd);
					}
				}

				// Add all groups dispatch's
				if (dispatchingGroupIds.Any())
				{
					model.Call.GroupDispatches = new List<CallDispatchGroup>();

					foreach (var id in dispatchingGroupIds)
					{
						CallDispatchGroup dispatch = new CallDispatchGroup();
						dispatch.DepartmentGroupId = id;

						model.Call.GroupDispatches.Add(dispatch);
					}
				}

				// Add all unit dispatch's
				if (dispatchingUnitIds.Any())
				{
					model.Call.UnitDispatches = new List<CallDispatchUnit>();

					foreach (var id in dispatchingUnitIds)
					{
						CallDispatchUnit dispatch = new CallDispatchUnit();
						dispatch.UnitId = id;

						model.Call.UnitDispatches.Add(dispatch);
					}
				}

				// Add all role dispatch's
				if (dispatchingRoleIds.Any())
				{
					model.Call.RoleDispatches = new List<CallDispatchRole>();

					foreach (var id in dispatchingRoleIds)
					{
						CallDispatchRole dispatch = new CallDispatchRole();
						dispatch.RoleId = id;

						model.Call.RoleDispatches.Add(dispatch);
					}
				}

				model.Call.Protocols = new List<CallProtocol>();
				if (activeProtocols.Any())
				{
					foreach (var id in activeProtocols)
					{
						CallProtocol protocol = new CallProtocol();
						protocol.DispatchProtocolId = id;

						if (collection.ContainsKey($"protocolCode_{id}"))
							protocol.Data = collection[$"protocolCode_{id}"];

						model.Call.Protocols.Add(protocol);
					}
				}

				if (pendingProtocols.Any())
				{
					foreach (var id in pendingProtocols)
					{
						CallProtocol protocol = new CallProtocol();
						protocol.DispatchProtocolId = id;

						if (collection.ContainsKey($"protocolCode_{id}"))
							protocol.Data = collection[$"protocolCode_{id}"];

						model.Call.Protocols.Add(protocol);
					}
				}

				if (model.Call.UnitDispatches != null && model.Call.UnitDispatches.Any())
				{
					foreach (var unitDispatch in model.Call.UnitDispatches)
					{
						var unitRoleAssignments = await _unitsService.GetActiveRolesForUnitAsync(unitDispatch.UnitId);

						if (unitRoleAssignments != null && unitRoleAssignments.Any())
						{
							foreach(var unitRoleAssignment in unitRoleAssignments)
							{
								if (!model.Call.Dispatches.Any(x => x.UserId == unitRoleAssignment.UserId))
								{
									CallDispatch cd = new CallDispatch();
									cd.UserId = unitRoleAssignment.UserId;

									model.Call.Dispatches.Add(cd);
								}
							}
						}	
					}
				}

				model.Call.CallSource = (int)CallSources.User;

				if (!string.IsNullOrWhiteSpace(model.Call.GeoLocationData) && string.IsNullOrWhiteSpace(model.Call.Address))
				{
					try
					{
						model.Call.Address = await _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(model.Latitude), double.Parse(model.Longitude));
					}
					catch { /* If no addy, no addy */ }
				}
				var call = await _callsService.SaveCallAsync(model.Call, cancellationToken);

				var cqi = new CallQueueItem();
				cqi.Call = call;

				// If we have any group, unit or role dispatches just bet the farm and add all profiles for now.
				if (dispatchingGroupIds.Any() || dispatchingUnitIds.Any() || dispatchingRoleIds.Any())
					cqi.Profiles = (await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId)).Select(x => x.Value).ToList();
				else if (cqi.Call.Dispatches != null && cqi.Call.Dispatches.Any())
					cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(cqi.Call.Dispatches.Select(x => x.UserId).ToList());
				else
					cqi.Profiles = new List<UserProfile>();

				if (dispatchingUserIds.Any() || dispatchingGroupIds.Any() || dispatchingUnitIds.Any() || dispatchingRoleIds.Any())
					await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);

				_eventAggregator.SendMessage<CallAddedEvent>(new CallAddedEvent() { DepartmentId = DepartmentId, Call = call });

				return RedirectToAction("Dashboard", "Dispatch", new { Area = "User" });
			}

			return View("NewCall", model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> UpdateCall(int callId)
		{
			if (!await _authorizationService.CanUserEditCallAsync(UserId, callId))
				Unauthorized();

			UpdateCallView model = new UpdateCallView();
			model = await FillUpdateCallView(model);
			model.Call = await _callsService.GetCallByIdAsync(callId);
			model.CallPriority = model.Call.Priority;

			if (!String.IsNullOrEmpty(model.Call.GeoLocationData))
			{
				string[] loc = model.Call.GeoLocationData.Split(char.Parse(","));
				model.Latitude = loc[0];
				model.Longitude = loc[1];
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> UpdateCall(UpdateCallView model, IFormCollection collection, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserEditCallAsync(UserId, model.Call.CallId))
				Unauthorized();

			model = await FillUpdateCallView(model);

			if (ModelState.IsValid)
			{
				var call = await _callsService.GetCallByIdAsync(model.Call.CallId);
				call = await _callsService.PopulateCallData(call, true, true, true, true, true, true, true);

				call.NatureOfCall = System.Net.WebUtility.HtmlDecode(model.Call.NatureOfCall);
				call.Notes = System.Net.WebUtility.HtmlDecode(model.Call.Notes);
				call.Name = model.Call.Name;
				call.Priority = (int)model.CallPriority;
				call.IsCritical = model.Call.IsCritical;
				call.MapPage = model.Call.MapPage;
				call.CallSource = (int)CallSources.User;
				call.ContactName = model.Call.ContactName;
				call.ContactNumber = model.Call.ContactNumber;
				call.Public = model.Call.Public;
				call.ExternalIdentifier = model.Call.ExternalIdentifier;
				call.ReferenceNumber = model.Call.ReferenceNumber;
				call.IncidentNumber = model.Call.IncidentNumber;
				call.Address = model.Call.Address;
				call.W3W = model.What3Word;
				call.Type = model.Call.Type;

				if (!string.IsNullOrEmpty(model.Call.Address))
				{
					call.Address = model.Call.Address;
				}

				if (!String.IsNullOrEmpty(model.Latitude) && !String.IsNullOrEmpty(model.Longitude))
					call.GeoLocationData = string.Format("{0},{1}", model.Latitude, model.Longitude);

				List<CallDispatch> existingDispatches = new List<CallDispatch>(call.Dispatches);

				List<string> dispatchingUserIds = new List<string>();
				List<int> dispatchingGroupIds = new List<int>();
				List<int> dispatchingUnitIds = new List<int>();
				List<int> dispatchingRoleIds = new List<int>();
				foreach (var key in collection.Keys)
				{
					if (key.ToString().StartsWith("dispatchUser_"))
					{
						string userId = key.ToString().Replace("dispatchUser_", "");
						dispatchingUserIds.Add(userId);
					}
					else if (key.ToString().StartsWith("dispatchGroup_"))
					{
						var groupId = int.Parse(key.ToString().Replace("dispatchGroup_", ""));
						dispatchingGroupIds.Add(groupId);
					}
					else if (key.ToString().StartsWith("dispatchUnit_"))
					{
						var unitId = int.Parse(key.ToString().Replace("dispatchUnit_", ""));
						dispatchingUnitIds.Add(unitId);
					}
					else if (key.ToString().StartsWith("dispatchRole_"))
					{
						var roleId = int.Parse(key.ToString().Replace("dispatchRole_", ""));
						dispatchingRoleIds.Add(roleId);
					}
				}

				//await _callsService.DeleteDispatchesAsync(call.Dispatches.ToList(), cancellationToken);
				//await _callsService.DeleteGroupDispatchesAsync(call.GroupDispatches.ToList(), cancellationToken);
				//await _callsService.DeleteRoleDispatchesAsync(call.RoleDispatches.ToList(), cancellationToken);
				//await _callsService.DeleteUnitDispatchesAsync(call.UnitDispatches.ToList(), cancellationToken);

				// Add all users dispatch's
				if (dispatchingUserIds.Any())
				{
					//model.Call.Dispatches = new Collection<CallDispatch>();
					//model.Call.Dispatches.RemoveAll()

					var dispatchesToRemove = call.Dispatches.Select(x => x.UserId).Where(y => !dispatchingUserIds.Contains(y)).ToList();

					foreach (var userId in dispatchesToRemove)
					{
						var item = call.Dispatches.First(x => x.UserId == userId);
						call.Dispatches.Remove(item);
					}

					foreach (var userId in dispatchingUserIds)
					{
						if (!call.Dispatches.Any(x => x.UserId == userId))
						{
							CallDispatch cd = new CallDispatch();
							cd.CallId = call.CallId;
							cd.UserId = userId;

							call.Dispatches.Add(cd);
						}
					}
				}

				// Add all groups dispatch's
				if (dispatchingGroupIds.Any())
				{
					//model.Call.GroupDispatches = new List<CallDispatchGroup>();

					var dispatchesToRemove = call.GroupDispatches.Select(x => x.DepartmentGroupId).Where(y => !dispatchingGroupIds.Contains(y)).ToList();

					foreach (var id in dispatchesToRemove)
					{
						call.GroupDispatches.Remove(call.GroupDispatches.First(x => x.DepartmentGroupId == id));
					}

					foreach (var id in dispatchingGroupIds)
					{
						if (!call.GroupDispatches.Any(x => x.DepartmentGroupId == id))
						{
							CallDispatchGroup dispatch = new CallDispatchGroup();
							dispatch.CallId = call.CallId;
							dispatch.DepartmentGroupId = id;

							call.GroupDispatches.Add(dispatch);
						}
					}
				}

				// Add all unit dispatch's
				if (dispatchingUnitIds.Any())
				{
					//model.Call.UnitDispatches = new List<CallDispatchUnit>();

					var dispatchesToRemove = call.UnitDispatches.Select(x => x.UnitId).Where(y => !dispatchingUnitIds.Contains(y)).ToList();

					foreach (var id in dispatchesToRemove)
					{
						call.UnitDispatches.Remove(call.UnitDispatches.First(x => x.UnitId == id));
					}

					foreach (var id in dispatchingUnitIds)
					{
						if (!call.UnitDispatches.Any(x => x.UnitId == id))
						{
							CallDispatchUnit dispatch = new CallDispatchUnit();
							dispatch.CallId = call.CallId;
							dispatch.UnitId = id;

							call.UnitDispatches.Add(dispatch);
						}
					}
				}

				// Add all role dispatch's
				if (dispatchingRoleIds.Any())
				{
					//model.Call.RoleDispatches = new List<CallDispatchRole>();

					var dispatchesToRemove = call.RoleDispatches.Select(x => x.RoleId).Where(y => !dispatchingRoleIds.Contains(y)).ToList();

					foreach (var id in dispatchesToRemove)
					{
						call.RoleDispatches.Remove(call.RoleDispatches.First(x => x.RoleId == id));
					}

					foreach (var id in dispatchingRoleIds)
					{
						if (!call.RoleDispatches.Any(x => x.RoleId == id))
						{
							CallDispatchRole dispatch = new CallDispatchRole();
							dispatch.CallId = call.CallId;
							dispatch.RoleId = id;

							call.RoleDispatches.Add(dispatch);
						}
					}
				}

				await _callsService.SaveCallAsync(call, cancellationToken);

				if (model.RebroadcastCall)
				{
					var cqi = new CallQueueItem();
					cqi.Call = call;

					// If we have any group, unit or role dispatches just bet the farm and all all profiles for now.
					if (dispatchingGroupIds.Any() || dispatchingUnitIds.Any() || dispatchingRoleIds.Any())
						cqi.Profiles = (await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId)).Select(x => x.Value).ToList();
					else if (cqi.Call.Dispatches != null && cqi.Call.Dispatches.Any())
						cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(cqi.Call.Dispatches.Select(x => x.UserId).ToList());
					else
						cqi.Profiles = new List<UserProfile>();


					if (dispatchingUserIds.Any() || dispatchingGroupIds.Any() || dispatchingUnitIds.Any() || dispatchingRoleIds.Any())
						await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);


					_eventAggregator.SendMessage<CallAddedEvent>(new CallAddedEvent() { DepartmentId = DepartmentId, Call = call });
				}

				//	scope.Complete();
				//}

				return RedirectToAction("Dashboard", "Dispatch", new { Area = "User" });
			}

			return View(model);
		}

		[Authorize(Policy = ResgridResources.Call_Delete)]
		public async Task<IActionResult> DeleteCall(int callId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserEditCallAsync(UserId, callId))
				Unauthorized();

			var call = await _callsService.GetCallByIdAsync(callId);
			call.IsDeleted = true;

			await _callsService.SaveCallAsync(call, cancellationToken);
			return RedirectToAction("Dashboard", "Dispatch", new { Area = "User" });
		}

		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> ViewCall(int callId)
		{
			if (!await _authorizationService.CanUserViewCallAsync(UserId, callId))
				Unauthorized();

			var model = new ViewCallView();
			model.Call = await _callsService.GetCallByIdAsync(callId);
			model = await FillViewCallView(model);
			model.CallPriority = (CallPriority)model.Call.Priority;
			model.Stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);
			model.Protocols = await _protocolsService.GetAllProtocolsForDepartmentAsync(DepartmentId);

			model.Call = await _callsService.PopulateCallData(model.Call, true, true, true, true, true, true, true);

			if (model.Stations == null)
				model.Stations = new List<DepartmentGroup>();

			if (!String.IsNullOrEmpty(model.Call.GeoLocationData))
			{
				string[] loc = model.Call.GeoLocationData.Split(char.Parse(","));
				model.Latitude = loc[0];
				model.Longitude = loc[1];
			}
			else if (!String.IsNullOrEmpty(model.Call.Address))
			{
				string coordinates = await _geoLocationProvider.GetLatLonFromAddress(model.Call.Address);

				if (!String.IsNullOrEmpty(coordinates))
				{
					model.Latitude = coordinates.Split(char.Parse(","))[0];
					model.Longitude = coordinates.Split(char.Parse(","))[1];
				}
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> AddArchivedCall()
		{
			var model = new NewCallView();
			model.Call = new Call();
			model = await FillNewCallView(model);
			model.Call.LoggedOn = DateTime.UtcNow.TimeConverter(model.Department);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> AddArchivedCall(NewCallView model, IFormCollection collection, CancellationToken cancellationToken)
		{
			model = await FillNewCallView(model);

			if (ModelState.IsValid)
			{
				model.Call.ReportingUserId = UserId;
				model.Call.DepartmentId = DepartmentId;
				model.Call.Priority = (int)model.CallPriority;
				model.Call.State = 0;
				model.Call.LoggedOn = model.Call.LoggedOn.ToUniversalTime();
				model.Call.NatureOfCall = System.Net.WebUtility.HtmlDecode(model.Call.NatureOfCall);
				model.Call.Notes = System.Net.WebUtility.HtmlDecode(model.Call.Notes);

				if (!String.IsNullOrWhiteSpace(model.What3Word))
					model.Call.W3W = model.What3Word;

				if (model.Call.Type == "No Type")
					model.Call.Type = null;

				if (!String.IsNullOrEmpty(model.Latitude) && !String.IsNullOrEmpty(model.Longitude))
					model.Call.GeoLocationData = string.Format("{0},{1}", model.Latitude, model.Longitude);

				List<string> dispatchingUserIds = new List<string>();
				List<int> dispatchingGroupIds = new List<int>();
				List<int> dispatchingUnitIds = new List<int>();
				List<int> dispatchingRoleIds = new List<int>();
				foreach (var key in collection.Keys)
				{
					if (key.ToString().StartsWith("dispatchUser_"))
					{
						string userId = key.ToString().Replace("dispatchUser_", "");
						dispatchingUserIds.Add(userId);
					}
					else if (key.ToString().StartsWith("dispatchGroup_"))
					{
						var groupId = int.Parse(key.ToString().Replace("dispatchGroup_", ""));
						dispatchingGroupIds.Add(groupId);
					}
					else if (key.ToString().StartsWith("dispatchUnit_"))
					{
						var unitId = int.Parse(key.ToString().Replace("dispatchUnit_", ""));
						dispatchingUnitIds.Add(unitId);
					}
					else if (key.ToString().StartsWith("dispatchRole_"))
					{
						var roleId = int.Parse(key.ToString().Replace("dispatchRole_", ""));
						dispatchingRoleIds.Add(roleId);
					}
				}

				// Add all users dispatch's
				if (dispatchingUserIds.Any())
				{
					model.Call.Dispatches = new Collection<CallDispatch>();

					foreach (var userId in dispatchingUserIds)
					{
						CallDispatch cd = new CallDispatch();
						cd.UserId = userId;

						model.Call.Dispatches.Add(cd);
					}
				}

				// Add all groups dispatch's
				if (dispatchingGroupIds.Any())
				{
					model.Call.GroupDispatches = new List<CallDispatchGroup>();

					foreach (var id in dispatchingGroupIds)
					{
						CallDispatchGroup dispatch = new CallDispatchGroup();
						dispatch.DepartmentGroupId = id;

						model.Call.GroupDispatches.Add(dispatch);
					}
				}

				// Add all unit dispatch's
				if (dispatchingUnitIds.Any())
				{
					model.Call.UnitDispatches = new List<CallDispatchUnit>();

					foreach (var id in dispatchingUnitIds)
					{
						CallDispatchUnit dispatch = new CallDispatchUnit();
						dispatch.UnitId = id;

						model.Call.UnitDispatches.Add(dispatch);
					}
				}

				// Add all role dispatch's
				if (dispatchingRoleIds.Any())
				{
					model.Call.RoleDispatches = new List<CallDispatchRole>();

					foreach (var id in dispatchingRoleIds)
					{
						CallDispatchRole dispatch = new CallDispatchRole();
						dispatch.RoleId = id;

						model.Call.RoleDispatches.Add(dispatch);
					}
				}

				model.Call.CallSource = (int)CallSources.User;

				if (!string.IsNullOrWhiteSpace(model.Call.GeoLocationData) && string.IsNullOrWhiteSpace(model.Call.Address))
				{
					try
					{
						model.Call.Address = await _geoLocationProvider.GetAproxAddressFromLatLong(double.Parse(model.Latitude), double.Parse(model.Longitude));
					}
					catch { /* If No addy, no addy */ }
				}
				var call = await _callsService.SaveCallAsync(model.Call, cancellationToken);

				if (model.ReCalcuateCallNumbers)
				{
					await _callsService.RegenerateCallNumbersAsync(DepartmentId, cancellationToken);
				}

				return RedirectToAction("ArchivedCalls", "Dispatch", new { Area = "User" });
			}

			return View("AddArchivedCall", model);
		}

		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> CallData(int callId)
		{
			if (!await _authorizationService.CanUserViewCallAsync(UserId, callId))
				Unauthorized();

			var model = new ViewCallView();
			model.Call = await _callsService.GetCallByIdAsync(callId);
			model.CallPriority = (CallPriority)model.Call.Priority;
			model = await FillViewCallView(model);

			if (!String.IsNullOrEmpty(model.Call.GeoLocationData))
			{
				string[] loc = model.Call.GeoLocationData.Split(char.Parse(","));
				model.Latitude = loc[0];
				model.Longitude = loc[1];
			}
			else if (!String.IsNullOrEmpty(model.Call.Address))
			{
				string coordinates = await _geoLocationProvider.GetLatLonFromAddress(model.Call.Address);

				if (!String.IsNullOrEmpty(coordinates))
				{
					model.Latitude = coordinates.Split(char.Parse(","))[0];
					model.Longitude = coordinates.Split(char.Parse(","))[1];
				}
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> CloseCall(int callId)
		{
			if (!await _authorizationService.CanUserEditCallAsync(UserId, callId))
				Unauthorized();

			CloseCallView model = new CloseCallView();
			model = await FillCloseCallView(model);
			var call = await _callsService.GetCallByIdAsync(callId);
			model.CallId = call.CallId;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> CloseCall(CloseCallView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserEditCallAsync(UserId, model.CallId))
				Unauthorized();

			model = await FillCloseCallView(model);
			var call = await _callsService.GetCallByIdAsync(model.CallId);

			if (ModelState.IsValid)
			{
				call.ClosedByUserId = UserId;
				call.ClosedOn = DateTime.UtcNow;
				call.CompletedNotes = System.Net.WebUtility.HtmlDecode(model.ClosedCallNotes);
				call.State = (int)model.CallState;

				await _callsService.SaveCallAsync(call, cancellationToken);

				return RedirectToAction("Dashboard", "Dispatch", new { Area = "User" });
			}

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> AddCallNote([FromBody]AddCallNoteInput model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserEditCallAsync(UserId, model.CallId))
				Unauthorized();

			var note = new CallNote();
			note.UserId = UserId;
			note.CallId = model.CallId;
			note.Note = model.Note;
			note.Timestamp = DateTime.UtcNow;
			note.Source = (int)CallNoteSources.Web;

			await _callsService.SaveCallNoteAsync(note, cancellationToken);

			return new StatusCodeResult((int)HttpStatusCode.OK);
		}

		[HttpGet]
		public async Task<IActionResult> GetCallNotes(int callId)
		{
			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null)
				return new StatusCodeResult((int)HttpStatusCode.NotFound);

			if (call.DepartmentId != DepartmentId)
				Unauthorized();

			call.Department = await _departmentsService.GetDepartmentByIdAsync(call.DepartmentId);
			call = await _callsService.PopulateCallData(call, false, false, true, false, false, false, false);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
			List<CallNoteJson> callNotes = new List<CallNoteJson>();

			foreach (var callNote in call.CallNotes)
			{
				var name = personnelNames.FirstOrDefault(x => x.UserId == callNote.UserId);

				if (name != null)
				{
					CallNoteJson note = new CallNoteJson();
					note.Name = name.Name;
					note.Timestamp = callNote.Timestamp.TimeConverter(call.Department).FormatForDepartment(call.Department);
					note.Note = callNote.Note;
					note.UserId = callNote.UserId;

					if (callNote.Latitude.HasValue && callNote.Longitude.HasValue)
					{
						note.Location = $"{callNote.Latitude.Value},{callNote.Longitude.Value}";
						note.Latitude = callNote.Latitude;
						note.Longitude = callNote.Longitude;
					}
					else
					{
						note.Location = "No Location";
					}

					callNotes.Add(note);
				}
			}

			return Json(callNotes);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> CallExport(int callId)
		{
			if (!await _authorizationService.CanUserViewCallAsync(UserId, callId))
				Unauthorized();

			var model = new CallExportView();
			model.Call = await _callsService.GetCallByIdAsync(callId);
			model.CallLogs = await _workLogsService.GetCallLogsForCallAsync(callId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(model.Call.DepartmentId, false);
			model.UnitStates = (await _unitsService.GetUnitStatesForCallAsync(model.Call.DepartmentId, callId)).OrderBy(x => x.UnitId).OrderBy(y => y.Timestamp).ToList();
			model.ActionLogs = (await _actionLogsService.GetActionLogsForCallAsync(model.Call.DepartmentId, callId)).OrderBy(x => x.UserId).OrderBy(y => y.Timestamp).ToList();
			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			model.Units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			model.Call = await _callsService.PopulateCallData(model.Call, true, true, true, true, true, true, true);

			return View(model);
		}

		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> CallExportEx(string query)
		{
			if (String.IsNullOrWhiteSpace(query))
				Unauthorized();


			var decryptedQuery = SymmetricEncryption.Decrypt(query, Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);

			if (!decryptedQuery.Contains("|"))
			{
				// Legacy query, just the call id
				var callId = SymmetricEncryption.Decrypt(query, Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);

				if (String.IsNullOrWhiteSpace(callId))
					Unauthorized();

				var call = await _callsService.GetCallByIdAsync(int.Parse(callId));

				if (call == null)
					Unauthorized();

				var model = new CallExportView();
				model.Call = call;
				model.CallLogs = await _workLogsService.GetCallLogsForCallAsync(call.CallId);
				model.Department = await _departmentsService.GetDepartmentByIdAsync(model.Call.DepartmentId, false);
				model.UnitStates = (await _unitsService.GetUnitStatesForCallAsync(model.Call.DepartmentId, call.CallId)).OrderBy(x => x.UnitId).OrderBy(y => y.Timestamp).ToList();
				model.ActionLogs = (await _actionLogsService.GetActionLogsForCallAsync(model.Call.DepartmentId, call.CallId)).OrderBy(x => x.UserId).OrderBy(y => y.Timestamp).ToList();
				model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(model.Call.DepartmentId);
				model.Units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
				model.Call = await _callsService.PopulateCallData(model.Call, true, true, true, true, true, true, true);

				return View(model);
			}
			else
			{
				var items = decryptedQuery.Split(char.Parse("|"));

				if (String.IsNullOrWhiteSpace(items[0]) || items[0] == "0")
					Unauthorized();

				var call = await _callsService.GetCallByIdAsync(int.Parse(items[0]));

				if (call == null)
					Unauthorized();

				var model = new CallExportView();
				model.Call = call;
				model.CallLogs = await _workLogsService.GetCallLogsForCallAsync(call.CallId);
				model.Department = await _departmentsService.GetDepartmentByIdAsync(model.Call.DepartmentId, false);
				model.UnitStates = (await _unitsService.GetUnitStatesForCallAsync(model.Call.DepartmentId, call.CallId)).OrderBy(x => x.UnitId).OrderBy(y => y.Timestamp).ToList();
				model.ActionLogs = (await _actionLogsService.GetActionLogsForCallAsync(model.Call.DepartmentId, call.CallId)).OrderBy(x => x.UserId).OrderBy(y => y.Timestamp).ToList();
				model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(model.Call.DepartmentId);
				model.Units = await _unitsService.GetUnitsForDepartmentAsync(model.Call.DepartmentId);
				model.Call = await _callsService.PopulateCallData(model.Call, true, true, true, true, true, true, true);

				if (!String.IsNullOrWhiteSpace(items[2]) && items[2] != "0")
				{
					model.Station = await _departmentGroupsService.GetGroupByIdAsync(int.Parse(items[2]));

					string startLat = "";
					string startLon = "";

					if (!String.IsNullOrWhiteSpace(model.Station.Latitude) && !String.IsNullOrWhiteSpace(model.Station.Longitude))
					{
						startLat = model.Station.Latitude;
						startLon = model.Station.Longitude;
					}
					else if (model.Station.Address != null)
					{
						var location = await _geoLocationProvider.GetLatLonFromAddress(model.Station.Address.FormatAddress());

						if (!String.IsNullOrWhiteSpace(location))
						{
							var locationParts = location.Split(char.Parse(","));
							startLat = locationParts[0];
							startLon = locationParts[1];
						}
					}

					string endLat = "";
					string endLon = "";

					var callCocationParts = call.GeoLocationData.Split(char.Parse(","));
					endLat = callCocationParts[0];
					endLon = callCocationParts[1];

					model.StartLat = startLat;
					model.StartLon = startLon;
					model.EndLat = endLat;
					model.EndLon = endLon;
				}


				return View(model);
			}
		}

		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> CallExportPdf(string query)
		{
			if (String.IsNullOrWhiteSpace(query))
				Unauthorized();

			//var decryptedQuery = SymmetricEncryption.Decrypt(query, Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);

			var client = new RestClient(Config.SystemBehaviorConfig.ResgridBaseUrl);
			var request = new RestRequest($"User/Dispatch/CallExportEx?query={HttpUtility.UrlEncode(query)}", Method.GET);

			var response = client.Execute(request);

			if (!string.IsNullOrWhiteSpace(response.Content))
			{
				Regex rRemScript = new Regex(@"<script[^>]*>[\s\S]*?</script>");
				var content = rRemScript.Replace(response.Content, "");

				var file = _pdfProvider.ConvertHtmlToPdf(content);

				MemoryStream output = new MemoryStream();
				output.Write(file, 0, file.Length);
				output.Position = 0;

				return File(output, "application/pdf");
			}

			return NotFound();
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> ReOpenCall(int callId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserViewCallAsync(UserId, callId))
				Unauthorized();

			await _callsService.ReOpenCallByIdAsync(callId, cancellationToken);

			return RedirectToAction("Dashboard", "Dispatch", new { Area = "User" });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetCallDispatchAudio(int callId)
		{
			var call = await _callsService.GetCallByIdAsync(callId);
			call = await _callsService.PopulateCallData(call, false, true, false, false, false, false, false);

			if (call.Attachments != null && call.Attachments.Count > 0)
			{
				return File(call.Attachments.First().Data, "audio/mpeg");
			}

			return RedirectToAction("Dashboard");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetActiveCallsForGrid()
		{
			var calls = new List<CallJson>();

			var activeCalls = (await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId)).OrderBy(x => x.LoggedOn);

			var genericCall = new CallJson()
			{
				DispatchTime = DateTime.UtcNow,
				Priority = "Low",
				Name = "Generic Call"
			};
			calls.Add(genericCall);

			foreach (var call in activeCalls)
			{
				var jsonCall = new CallJson();
				jsonCall.CallId = call.CallId;
				jsonCall.DispatchTime = call.LoggedOn;
				jsonCall.Priority = call.GetPriorityText();
				jsonCall.Name = call.Name;

				calls.Add(jsonCall);
			}

			return Json(calls);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetAllCallsForGrid()
		{
			var calls = new List<CallJson>();

			var activeCalls = (await _callsService.GetAllCallsByDepartmentAsync(DepartmentId)).OrderBy(x => x.LoggedOn);

			foreach (var call in activeCalls)
			{
				var jsonCall = new CallJson();
				jsonCall.CallId = call.CallId;
				jsonCall.DispatchTime = call.LoggedOn;
				jsonCall.Priority = call.GetPriorityText();
				jsonCall.Name = call.Name;
				jsonCall.State = ((CallStates)call.State).ToString();

				calls.Add(jsonCall);
			}

			return Json(calls);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public PartialViewResult SmallActiveCallGrid()
		{
			return PartialView("_SmallActiveCallsGridPartial");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public PartialViewResult SmallCallGrid()
		{
			return PartialView("_SmallCallsGridPartial");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetCallById(int callId)
		{
			var call = new CallJson();
			var savedCall = await _callsService.GetCallByIdAsync(callId);
			savedCall.Department = await _departmentsService.GetDepartmentByIdAsync(savedCall.DepartmentId);

			call.CallId = savedCall.CallId;
			call.DispatchTime = savedCall.LoggedOn.TimeConverter(savedCall.Department);
			call.Priority = savedCall.GetPriorityText();
			call.PriorityEnum = (CallPriority)savedCall.Priority;
			call.Name = savedCall.Name;
			call.State = ((CallStates)savedCall.State).ToString();
			call.Nature = savedCall.NatureOfCall;
			call.Address = savedCall.Address;

			return Json(call);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetPersonnelForCall(int callId)
		{
			List<CallPersonnelForJson> personnelJson = new List<CallPersonnelForJson>();
			var users = await _departmentsService.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(DepartmentId);
			var call = await _callsService.GetCallByIdAsync(callId);

			foreach (var user in users)
			{
				CallPersonnelForJson person = new CallPersonnelForJson();
				person.UserId = user.UserId;

				var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, DepartmentId);
				if (group != null)
					person.Group = group.Name;

				var roles = await _personnelRolesService.GetRolesForUserAsync(user.UserId, DepartmentId);
				person.Roles = new List<string>();
				foreach (var role in roles)
				{
					person.Roles.Add(role.Name);
				}

				person.Dispatched = call.HasUserBeenDispatched(user.UserId);

				if (person.Dispatched)
					person.CheckValue = "checked";

				personnelJson.Add(person);
			}

			return Json(personnelJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetAllDispatchesForCall(int callId)
		{
			List<CallDispatchJson> dispatchJson = new List<CallDispatchJson>();
			var users = await _departmentsService.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(DepartmentId);
			var call = await _callsService.GetCallByIdAsync(callId);
			call = await _callsService.PopulateCallData(call, true, true, true, true, true, true, true);

			foreach (var userDispatch in call.Dispatches)
			{
				CallDispatchJson dispatch = new CallDispatchJson();
				dispatch.DisptachCode = $"#dispatchUser_{userDispatch.UserId}";

				dispatchJson.Add(dispatch);
			}

			foreach (var groupDispatch in call.GroupDispatches)
			{
				CallDispatchJson dispatch = new CallDispatchJson();
				dispatch.DisptachCode = $"#dispatchGroup_{groupDispatch.DepartmentGroupId}";

				dispatchJson.Add(dispatch);
			}

			foreach (var roleDispatch in call.RoleDispatches)
			{
				CallDispatchJson dispatch = new CallDispatchJson();
				dispatch.DisptachCode = $"#dispatchRole_{roleDispatch.CallDispatchRoleId}";

				dispatchJson.Add(dispatch);
			}

			foreach (var unitDispatch in call.UnitDispatches)
			{
				CallDispatchJson dispatch = new CallDispatchJson();
				dispatch.DisptachCode = $"#dispatchUnit_{unitDispatch.UnitId}";

				dispatchJson.Add(dispatch);
			}

			return Json(dispatchJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetMapDataForCall(int callId)
		{
			var serializerSettings = new JsonSerializerSettings
			{
				ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
				NullValueHandling = NullValueHandling.Include,
				Formatting = Formatting.Indented,
				Converters = new JsonConverter[]
				{
					new Newtonsoft.Json.Converters.IsoDateTimeConverter(),
					new Newtonsoft.Json.Converters.StringEnumConverter()
				},
			};

			var call = await _callsService.GetCallByIdAsync(callId);
			var coordiantes = await _departmentSettingsService.GetMapCenterCoordinatesAsync(await _departmentsService.GetDepartmentByIdAsync(DepartmentId));
			var model = new BigBoardMapModel();
			model.CenterLat = coordiantes.Latitude.Value;
			model.CenterLon = coordiantes.Longitude.Value;

			if (!String.IsNullOrWhiteSpace(call.GeoLocationData))
			{
				string[] coordinates = call.GeoLocationData.Split(char.Parse(","));

				if (coordinates.Count() == 2)
				{
					double newLat;
					double newLon;
					if (double.TryParse(coordinates[0], out newLat) && double.TryParse(coordinates[1], out newLon))
					{
						model.CenterLat = newLat;
						model.CenterLon = newLon;
					}
				}

				var markerInfo = new MapMakerInfo();
				markerInfo.Latitude = model.CenterLat;
				markerInfo.Longitude = model.CenterLon;
				markerInfo.Title = call.Name;
				model.MapMakerInfos.Add(markerInfo);
			}

			var result = JsonConvert.SerializeObject(model, Formatting.Indented, serializerSettings);
			return Content(result, "application/json");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> CallsYTD()
		{
			List<CallWeekJson> callWeeks = new List<CallWeekJson>();

			CultureInfo culture = new CultureInfo("en-us");
			Calendar calendar = culture.Calendar;

			var calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 1), new DateTime(DateTime.UtcNow.Year, 12, 31, 23, 59, 59));

			// Week
			//    Call Type
			//        Call Count

			for (int i = 1; i <= 52; i++)
			{
				var weekCalls =
					calls.Where(x => calendar.GetWeekOfYear(x.LoggedOn, CalendarWeekRule.FirstFullWeek, DayOfWeek.Sunday) == i).GroupBy(x => x.Type);

				foreach (var weekCall in weekCalls)
				{
					var callWeek = new CallWeekJson();
					callWeek.WeekNumber = i;

					string type = "Call";
					if (!String.IsNullOrWhiteSpace(weekCall.Key))
						type = weekCall.Key;

					callWeek.Type = type;
					callWeek.Count = weekCall.ToList().Count;

					callWeeks.Add(callWeek);
				}
			}

			return Json(callWeeks);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> CallsTypesInRange(string startDate, string endDate)
		{
			List<CallTypesJson> callTypes = new List<CallTypesJson>();

			if (!String.IsNullOrWhiteSpace(startDate) && !String.IsNullOrWhiteSpace(endDate))
			{
				var calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, DateTime.Parse(System.Net.WebUtility.UrlDecode((startDate))), DateTime.Parse(System.Net.WebUtility.UrlDecode(endDate)));

				var groupedCalls = calls.GroupBy(x => x.Type);
				foreach (var grouppedCall in groupedCalls)
				{
					string key = "No Type";
					if (!String.IsNullOrWhiteSpace(grouppedCall.Key))
						key = grouppedCall.Key;

					callTypes.Add(new CallTypesJson() { Count = grouppedCall.ToList().Count, Type = key });
				}
			}

			return Json(callTypes);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> CallsStatesInRange(string startDate, string endDate)
		{
			List<CallTypesJson> callTypes = new List<CallTypesJson>();

			if (!String.IsNullOrWhiteSpace(startDate) && !String.IsNullOrWhiteSpace(endDate))
			{
				var calls = await _callsService.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, DateTime.Parse(System.Net.WebUtility.UrlDecode((startDate))), DateTime.Parse(System.Net.WebUtility.UrlDecode(endDate)));

				var groupedCallStates = calls.GroupBy(x => x.State);
				foreach (var grouppedCall in groupedCallStates)
				{
					callTypes.Add(new CallTypesJson() { Count = grouppedCall.ToList().Count, Type = ((CallStates)grouppedCall.Key).ToString() });
				}
			}

			return Json(callTypes);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]

		public async Task<IActionResult> GetActiveCallsList()
		{
			List<CallListJson> callsJson = new List<CallListJson>();

			var calls = (await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId)).OrderByDescending(x => x.LoggedOn);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var call in calls)
			{
				var callJson = new CallListJson();
				callJson.CallId = call.CallId;
				callJson.Number = call.Number;
				callJson.Name = call.Name;
				callJson.State = _callsService.CallStateToString((CallStates)call.State);
				callJson.StateColor = _callsService.CallStateToColor((CallStates)call.State);
				callJson.Timestamp = call.LoggedOn.TimeConverterToString(department);
				callJson.Priority = await _callsService.CallPriorityToStringAsync(call.Priority, DepartmentId);
				callJson.Color = await _callsService.CallPriorityToColorAsync(call.Priority, DepartmentId);

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || call.ReportingUserId == UserId)
					callJson.CanDeleteCall = true;
				else
					callJson.CanDeleteCall = false;

				callsJson.Add(callJson);
			}

			return Json(callsJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetArchivedCallsList(string year)
		{
			List<CallListJson> callsJson = new List<CallListJson>();

			List<Call> calls;
			if (String.IsNullOrWhiteSpace(year))
				calls = await _callsService.GetClosedCallsByDepartmentAsync(DepartmentId);
			else
				calls = await _callsService.GetClosedCallsByDepartmentYearAsync(DepartmentId, year);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var call in calls)
			{
				var callJson = new CallListJson();
				callJson.CallId = call.CallId;
				callJson.Number = call.Number;
				callJson.Name = call.Name;
				callJson.State = _callsService.CallStateToString((CallStates)call.State);
				callJson.StateColor = _callsService.CallStateToColor((CallStates)call.State);
				callJson.Timestamp = call.LoggedOn.TimeConverterToString(department);
				callJson.Priority = await _callsService.CallPriorityToStringAsync(call.Priority, DepartmentId);
				callJson.Color = await _callsService.CallPriorityToColorAsync(call.Priority, DepartmentId);

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || call.ReportingUserId == UserId)
					callJson.CanDeleteCall = true;
				else
					callJson.CanDeleteCall = false;

				callsJson.Add(callJson);
			}

			return Json(callsJson);
		}

		[HttpPost]
		public async Task<IActionResult> AttachCallFile(FileAttachInput model, IFormFile fileToUpload, CancellationToken cancellationToken)
		{
			if (fileToUpload == null || fileToUpload.Length <= 0)
				ModelState.AddModelError("fileToUpload", "You must select a file to attach.");
			else
			{
				var extenion = fileToUpload.FileName.Substring(fileToUpload.FileName.IndexOf(char.Parse(".")) + 1,
					fileToUpload.FileName.Length - fileToUpload.FileName.IndexOf(char.Parse(".")) - 1);

				if (!String.IsNullOrWhiteSpace(extenion))
					extenion = extenion.ToLower();

				if (extenion != "jpg" && extenion != "jpeg" && extenion != "png" && extenion != "gif" && extenion != "gif" && extenion != "pdf" && extenion != "doc"
					&& extenion != "docx" && extenion != "ppt" && extenion != "pptx" && extenion != "pps" && extenion != "ppsx" && extenion != "odt"
					&& extenion != "xls" && extenion != "xlsx" && extenion != "mp3" && extenion != "m4a" && extenion != "ogg" && extenion != "wav"
					&& extenion != "mp4" && extenion != "m4v" && extenion != "mov" && extenion != "wmv" && extenion != "avi" && extenion != "mpg" && extenion != "txt")
					ModelState.AddModelError("fileToUpload", string.Format("Document type ({0}) is not importable.", extenion));

				if (fileToUpload.Length > 10000000)
					ModelState.AddModelError("fileToUpload", "File is too large, must be smaller then 10MB.");
			}

			if (ModelState.IsValid)
			{
				var attachment = new CallAttachment();
				attachment.CallId = model.CallId;
				attachment.UserId = UserId;
				attachment.Name = model.FriendlyName;
				attachment.FileName = fileToUpload.FileName;
				attachment.CallAttachmentType = (int)CallAttachmentTypes.File;
				attachment.Timestamp = DateTime.UtcNow;

				byte[] uploadedFile = new byte[fileToUpload.OpenReadStream().Length];
				fileToUpload.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

				attachment.Data = uploadedFile;
				attachment.Size = (int)fileToUpload.Length;

				await _callsService.SaveCallAttachmentAsync(attachment, cancellationToken);
			}

			return RedirectToAction("CallData", new { callId = model.CallId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<FileResult> GetCallFile(int callAttachmentId)
		{
			var attachment = await _callsService.GetCallAttachmentAsync(callAttachmentId);

			if (attachment.Call.DepartmentId != DepartmentId)
				Unauthorized();

			return new FileContentResult(attachment.Data, FileHelper.GetContentTypeByExtension(Path.GetExtension(attachment.FileName)))
			{
				FileDownloadName = attachment.FileName
			};
		}

		[HttpGet]
		public async Task<IActionResult> GetCoordinatesFromW3W(string words)
		{
			var result = _geoLocationProvider.GetCoordinatesFromW3W(words) ?? new Coordinates();

			return Json(result);
		}

		public async Task<IActionResult> GetTopActiveCalls()
		{
			var model = new TopActiveCallsView();
			model.Calls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			return PartialView("_ActiveTopCallsPartial", model);
		}

		[HttpGet]
		public async Task<IActionResult> GetCallImage(int callId, int attachmentId)
		{
			var callAttachment = await _callsService.GetCallAttachmentAsync(attachmentId);

			if (callAttachment == null || callAttachment.CallId != callId)
				return null;

			return File(callAttachment.Data, "image/jpeg");
		}

		#region Private Helpers
		private async Task<NewCallView> FillNewCallView(NewCallView model)
		{
			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			model.User = _usersService.GetUserById(UserId);
			model.CenterCoordinates = await _departmentSettingsService.GetMapCenterCoordinatesAsync(model.Department);
			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(model.Department.DepartmentId);
			model.Units = await _unitsService.GetUnitsForDepartmentAsync(model.Department.DepartmentId);
			model.UnitStates = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(model.Department.DepartmentId);
			model.UnitStatuses = await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(model.Department.DepartmentId);

			var priorites = await _callsService.GetCallPrioritiesForDepartmentAsync(model.Department.DepartmentId);
			//model.CallPriorities = model.CallPriority.ToSelectList();
			model.CallPriorities = new SelectList(priorites, "DepartmentCallPriorityId", "Name", priorites.FirstOrDefault(x => x.IsDefault));
			model.UnGroupedUsers = new List<IdentityUser>();

			List<CallType> types = new List<CallType>();
			types.Add(new CallType { CallTypeId = 0, Type = "No Type" });
			types.AddRange(await _callsService.GetCallTypesForDepartmentAsync(DepartmentId));
			model.CallTypes = new SelectList(types, "Type", "Type");

			var templates = await _templatesService.GetAllCallQuickTemplatesForDepartmentAsync(DepartmentId);
			if (templates != null)
				model.CallTemplates = new SelectList(templates, "CallQuickTemplateId", "Name");

			var allUsers = await _departmentsService.GetAllUsersForDepartmentAsync(model.Department.DepartmentId);

			List<string> groupedUserIds = new List<string>();

			foreach (var dg in model.Groups)
			{
				foreach (var u in dg.Members)
				{
					groupedUserIds.Add(u.UserId);
				}
			}

			var ungroupedUsers = from u in allUsers
								 where !(groupedUserIds.Contains(u.UserId))
								 select u;

			foreach (var u in ungroupedUsers)
			{
				model.UnGroupedUsers.Add(allUsers.Where(x => x.UserId == u.UserId).FirstOrDefault());
			}

			model.Call.ReportingUserId = UserId;

			return model;
		}

		private async Task<UpdateCallView> FillUpdateCallView(UpdateCallView model)
		{
			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			model.User = _usersService.GetUserById(UserId);
			model.CenterCoordinates = await _departmentSettingsService.GetMapCenterCoordinatesAsync(model.Department);
			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(model.Department.DepartmentId);
			model.Units = await _unitsService.GetUnitsForDepartmentAsync(model.Department.DepartmentId);
			model.UnitStates = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(model.Department.DepartmentId);

			var priorites = await _callsService.GetCallPrioritiesForDepartmentAsync(model.Department.DepartmentId);
			model.CallPriorities = new SelectList(priorites, "DepartmentCallPriorityId", "Name", priorites.FirstOrDefault(x => x.IsDefault));
			model.UnGroupedUsers = new List<IdentityUser>();

			List<CallType> types = new List<CallType>();
			types.Add(new CallType { CallTypeId = 0, Type = "No Type" });
			types.AddRange(await _callsService.GetCallTypesForDepartmentAsync(DepartmentId));
			model.CallTypes = new SelectList(types, "Type", "Type");

			var allUsers = await _departmentsService.GetAllUsersForDepartmentAsync(model.Department.DepartmentId);

			List<string> groupedUserIds = new List<string>();

			foreach (var dg in model.Groups)
			{
				foreach (var u in dg.Members)
				{
					groupedUserIds.Add(u.UserId);
				}
			}

			var ungroupedUsers = from u in allUsers
								 where !(groupedUserIds.Contains(u.UserId))
								 select u;

			foreach (var u in ungroupedUsers)
			{
				model.UnGroupedUsers.Add(allUsers.Where(x => x.UserId == u.UserId).FirstOrDefault());
			}

			return model;
		}

		private async Task<ViewCallView> FillViewCallView(ViewCallView model)
		{
			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			model.User = _usersService.GetUserById(UserId);

			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(model.Department.DepartmentId);
			model.CallPriorities = model.CallPriority.ToSelectList();
			model.UnGroupedUsers = new List<IdentityUser>();

			model.UnitStates = (await _unitsService.GetUnitStatesForCallAsync(model.Call.DepartmentId, model.Call.CallId)).OrderBy(y => y.Timestamp).ToList();
			model.ActionLogs = (await _actionLogsService.GetActionLogsForCallAsync(model.Call.DepartmentId, model.Call.CallId)).OrderBy(y => y.Timestamp).ToList();

			model.UserGroupRoles = _usersService.GetUserGroupAndRolesByDepartmentId(model.Call.DepartmentId, true, true, true);

			var allUsers = await _departmentsService.GetAllUsersForDepartmentAsync(model.Department.DepartmentId);

			List<string> groupedUserIds = new List<string>();

			foreach (var dg in model.Groups)
			{
				foreach (var u in dg.Members)
				{
					groupedUserIds.Add(u.UserId);
				}
			}

			var ungroupedUsers = from u in allUsers
								 where !(groupedUserIds.Contains(u.UserId))
								 select u;

			foreach (var u in ungroupedUsers)
			{
				model.UnGroupedUsers.Add(allUsers.Where(x => x.UserId == u.UserId).FirstOrDefault());
			}


			var units = await _unitsService.GetUnitsForDepartmentAsync(model.Call.DepartmentId);

			if (units != null)
				model.Units = units;
			else
				model.Units = new List<Unit>();

			return model;
		}

		private async Task<CloseCallView> FillCloseCallView(CloseCallView model)
		{
			model.Department = await _departmentsService.GetDepartmentByUserIdAsync(UserId);
			model.User = _usersService.GetUserById(UserId);

			model.CallStates = model.CallState.ToSelectList();

			return model;
		}
		#endregion Private Helpers
	}
}
