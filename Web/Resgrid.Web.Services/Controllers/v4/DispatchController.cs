using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Web.Services.Models.v4.Forms;
using Resgrid.Web.Services.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Web.Services.Models.v4.Personnel;
using Resgrid.Web.Services.Models.v4.Dispatch;
using Resgrid.Web.Services.Models.v4.Groups;
using Resgrid.Web.Services.Models.v4.Mapping;
using Resgrid.Web.Services.Models.v4.Units;
using Resgrid.Web.Services.Models.v4.CallTypes;
using Resgrid.Web.Services.Models.v4.CallPriorities;
using Resgrid.Web.Services.Models.v4.Calls;
using Resgrid.Web.Services.Models.v4.Roles;
using Resgrid.Web.Services.Models.v4.CustomStatuses;
using Resgrid.Web.Services.Models.v4.UnitStatus;
using Resgrid.Model;
using Resgrid.Web.Services.Models.v4.UnitRoles;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// API Calls that are used for the Dispatch App
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class DispatchController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUserStateService _userStateService;
		private readonly IUnitsService _unitsService;
		private readonly ICallsService _callsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ICustomStateService _customStateService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly ICqrsProvider _cqrsProvider;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ITemplatesService _templatesService;
		private readonly IFormsService _formsService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly INearestUnitService _nearestUnitService;
		private readonly IDispatchScopeService _dispatchScopeService;
		private readonly IMappingService _mappingService;

		public DispatchController(
			IUsersService usersService,
			IActionLogsService actionLogsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IUserStateService userStateService,
			IUnitsService unitsService,
			ICallsService callsService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			ICustomStateService customStateService,
			IGeoLocationProvider geoLocationProvider,
			ICqrsProvider cqrsProvider,
			IDepartmentSettingsService departmentSettingsService,
			ITemplatesService templatesService,
			IFormsService formsService,
			IMappingService mappingService,
			Model.Services.IAuthorizationService authorizationService,
			INearestUnitService nearestUnitService,
			IDispatchScopeService dispatchScopeService
			)
		{
			_nearestUnitService = nearestUnitService;
			_dispatchScopeService = dispatchScopeService;
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_userStateService = userStateService;
			_unitsService = unitsService;
			_callsService = callsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_customStateService = customStateService;
			_geoLocationProvider = geoLocationProvider;
			_cqrsProvider = cqrsProvider;
			_departmentSettingsService = departmentSettingsService;
			_templatesService = templatesService;
			_formsService = formsService;
			_mappingService = mappingService;
			_authorizationService = authorizationService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all the information required to populate the New Call form
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetNewCallData")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<NewCallFormResult>> GetNewCallData()
		{
			var mainResult = new NewCallFormResult();
			var result = new NewCallResultData();
			result.Personnel = new List<PersonnelInfoResultData>();
			result.Groups = new List<GroupResultData>();
			result.Units = new List<UnitResultData>();
			result.Roles = new List<RoleResultData>();
			result.Statuses = new List<CustomStatusResultData>();
			result.UnitStatuses = new List<UnitStatusResultData>();
			result.UnitRoles = new List<UnitRoleResultData>();
			result.Priorities = new List<CallPriorityResultData>();
			result.CallTypes = new List<CallTypeResultData>();
			result.PoiTypes = new List<PoiTypeResultData>();
			result.DestinationPois = new List<PoiResultData>();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
			var groups = await _departmentGroupsService.GetAllDepartmentGroupsForDepartmentAsync(DepartmentId);
			var rolesForUsersInDepartment = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(DepartmentId);
			var allRoles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);
			var allProfiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId);
			var allGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var unitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);
			var callPriorites = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId);
			var callTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);
			var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var poiTypes = await _mappingService.GetPOITypesForDepartmentAsync(DepartmentId);
			var pois = await _mappingService.GetPOIsForDepartmentAsync(DepartmentId);
			var canViewPII = await _authorizationService.CanUserViewPIIAsync(UserId, DepartmentId);

			foreach (var user in users)
			{
				UserProfile profile = null;
				if (allProfiles.ContainsKey(user.UserId))
					profile = allProfiles[user.UserId];

				DepartmentGroup group = null;
				if (groups.ContainsKey(user.UserId))
					group = groups[user.UserId];

				List<PersonnelRole> roles = null;
				if (rolesForUsersInDepartment.ContainsKey(user.UserId))
					roles = rolesForUsersInDepartment[user.UserId];

				var action = await _actionLogsService.GetLastActionLogForUserAsync(user.UserId, DepartmentId);
				var userState = await _userStateService.GetLastUserStateByUserIdAsync(user.UserId);

				result.Personnel.Add(await PersonnelController.ConvertPersonnelInfo(user, department, profile, group, roles, action, userState, canViewPII));
			}

			foreach (var group in allGroups)
			{
				result.Groups.Add(GroupsController.ConvertGroupData(group));
			}

			var unitStatuses = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);

			// Security > View Units, the same filter GetAllUnits applies, for the units and their statuses alike.
			var viewableUnitIds = new HashSet<int>();
			foreach (var unit in units)
			{
				if (await _authorizationService.CanUserViewUnitViaMatrixAsync(unit.UnitId, UserId, DepartmentId))
					viewableUnitIds.Add(unit.UnitId);
			}

			foreach (var unit in units.Where(u => viewableUnitIds.Contains(u.UnitId)))
			{
				if (!string.IsNullOrWhiteSpace(unit.Type))
				{
					var unitType = unitTypes.FirstOrDefault(x => x.Type == unit.Type);

					result.Units.Add(await UnitWithLocationIfVisibleAsync(UnitsController.ConvertUnitsData(unit, unitStatuses.FirstOrDefault(x => x.UnitId == unit.UnitId), null, TimeZone), unit.UnitId));
				}
				else
				{
					result.Units.Add(await UnitWithLocationIfVisibleAsync(UnitsController.ConvertUnitsData(unit, unitStatuses.FirstOrDefault(x => x.UnitId == unit.UnitId), null, TimeZone), unit.UnitId));
				}

				// Add unit roles for this unit
				var roles = await _unitsService.GetRolesForUnitAsync(unit.UnitId);
				foreach (var role in roles)
				{
					result.UnitRoles.Add(UnitRolesController.ConvertUnitRoleData(role));
				}
			}

			foreach (var us in unitStatuses.Where(x => viewableUnitIds.Contains(x.UnitId)))
			{
				var customState = await CustomStatesHelper.GetCustomUnitState(us);
				var latestUnitLocation = await _unitsService.GetLatestUnitLocationAsync(us.UnitId, us.Timestamp);

				var group = allGroups.FirstOrDefault(x => x.DepartmentGroupId == us.Unit.StationGroupId);
				var unitStatus = UnitStatusController.ConvertUnitStatusData(us.Unit, us, latestUnitLocation, customState, group, TimeZone, activeCalls, allGroups, pois);
				if (!await UnitLocationVisibility.CanSeeAsync(_authorizationService, us.UnitId, UserId, DepartmentId))
					UnitLocationVisibility.Withhold(unitStatus);
				result.UnitStatuses.Add(unitStatus);
			}

			foreach (var role in allRoles)
			{
				result.Roles.Add(RolesController.ConvertRoleData(role));
			}

			var customStates = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(DepartmentId);

			foreach (var customState in customStates)
			{
				if (customState.IsDeleted)
					continue;

				foreach (var stateDetail in customState.GetActiveDetails())
				{
					if (stateDetail.IsDeleted)
						continue;

					result.Statuses.Add(CustomStatusesController.ConvertCustomStatusData(customState, stateDetail));
				}

			}

			foreach (var priority in callPriorites)
			{
				result.Priorities.Add(CallPrioritiesController.ConvertPriorityData(priority));
			}

			if (callTypes != null && callTypes.Any())
			{
				foreach (var callType in callTypes)
				{
					result.CallTypes.Add(CallTypesController.ConvertTypeData(callType));
				}
			}

			if (poiTypes != null && poiTypes.Any())
			{
				foreach (var poiType in poiTypes)
				{
					result.PoiTypes.Add(MappingController.ConvertPoiTypeData(poiType));

					if (poiType.IsDestination && poiType.Pois != null && poiType.Pois.Any())
					{
						foreach (var poi in poiType.Pois)
						{
							result.DestinationPois.Add(MappingController.ConvertPoiData(poi, poiType));
						}
					}
				}
			}

			mainResult.Data = result;

			return mainResult;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="unitId"></param>
		/// <returns></returns>
		[HttpGet("GetSetUnitStatusData")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetSetUnitStateResult>> GetSetUnitStatusData(string unitId)
		{
			var result = new GetSetUnitStateResult();
			result.Data = new GetSetUnitStateResultData();

			if (string.IsNullOrWhiteSpace(unitId))
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			var unit = await _unitsService.GetUnitByIdAsync(int.Parse(unitId));

			if (unit == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (unit.DepartmentId != DepartmentId)
				return Unauthorized();

			result.Data.UnitId = unitId;
			result.Data.UnitName = unit.Name;
			result.Data.Stations = new List<GroupResultData>();
			result.Data.Calls = new List<CallResultData>();
			result.Data.DestinationPois = new List<PoiResultData>();
			result.Data.PoiTypes = new List<PoiTypeResultData>();
			result.Data.Statuses = new List<CustomStatusResultData>();

			var type = await _unitsService.GetUnitTypeByNameAsync(DepartmentId, unit.Type);
			// Group-scoped dispatch (off by default): only offer calls in the caller's area or that they are on.
			var activeCalls = await _dispatchScopeService.FilterCallsForUserAsync(DepartmentId, UserId, await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId));
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);
			var poiTypes = await _mappingService.GetPOITypesForDepartmentAsync(DepartmentId);

			var callDefault = new CallResultData();
			callDefault.CallId = "0";
			callDefault.Name = "No Call";
			result.Data.Calls.Add(callDefault);

			if (activeCalls != null)
			{
				foreach (var c in activeCalls)
				{
					result.Data.Calls.Add(CallsController.ConvertCall(c, null, null, TimeZone));
				}
			}

			var groupInfoDefault = new GroupResultData();
			groupInfoDefault.GroupId = "0";
			groupInfoDefault.Name = "No Station";
			result.Data.Stations.Add(groupInfoDefault);

			if (stations != null)
			{
				foreach (var group in stations)
				{
					result.Data.Stations.Add(GroupsController.ConvertGroupData(group));
				}
			}

			if (poiTypes != null && poiTypes.Any())
			{
				foreach (var poiType in poiTypes)
				{
					result.Data.PoiTypes.Add(MappingController.ConvertPoiTypeData(poiType));

					if (poiType.IsDestination && poiType.Pois != null && poiType.Pois.Any())
					{
						foreach (var poi in poiType.Pois)
						{
							result.Data.DestinationPois.Add(MappingController.ConvertPoiData(poi, poiType));
						}
					}
				}
			}

			if (type != null && type.CustomStatesId.HasValue)
			{
				var customState = await _customStateService.GetCustomSateByIdAsync(type.CustomStatesId.Value);

				if (!customState.IsDeleted)
				{
					foreach (var stateDetail in customState.GetActiveDetails())
					{
						if (stateDetail.IsDeleted)
							continue;

						result.Data.Statuses.Add(CustomStatusesController.ConvertCustomStatusData(customState, stateDetail));
					}
				}
			}
			else
			{
				// Units without a custom status set use the same built-in statuses (and destination settings) that
				// /Statuses/GetAllUnitStatuses serves as the "0" group, so Responding / On Scene / Staging prompt for
				// a call here too. Id, Type and StateId all carry the UnitStateTypes value, as before.
				foreach (var defaultStatus in _customStateService.GetDefaultUnitStatuses())
				{
					var customStateResult = new CustomStatusResultData();
					customStateResult.Id = defaultStatus.CustomStateDetailId.ToString();
					customStateResult.Type = defaultStatus.CustomStateDetailId;
					customStateResult.StateId = defaultStatus.CustomStateDetailId.ToString();
					customStateResult.Text = defaultStatus.ButtonText;
					customStateResult.BColor = defaultStatus.ButtonColor;
					customStateResult.Color = defaultStatus.TextColor;
					customStateResult.Gps = defaultStatus.GpsRequired;
					customStateResult.Note = defaultStatus.NoteType;
					customStateResult.Detail = defaultStatus.DetailType;

					result.Data.Statuses.Add(customStateResult);
				}
			}

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Returns all the personnel for display in the new call personnel table
		/// </summary>
		/// <returns>Array of PersonnelForCallResult objects for each person in the department</returns>
		[HttpGet("GetPersonnelForCallGrid")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetPersonnelForCallGridResult>> GetPersonnelForCallGrid()
		{
			var result = new GetPersonnelForCallGridResult();

			var users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);//.GetAllUsersForDepartmentUnlimitedMinusDisabled(DepartmentId);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			var lastUserActionlogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(DepartmentId);
			var userStates = await _userStateService.GetLatestStatesForDepartmentAsync(DepartmentId);

			var personnelSortOrder = await _departmentSettingsService.GetDepartmentPersonnelSortOrderAsync(DepartmentId);
			var personnelStatusSortOrder = await _departmentSettingsService.GetDepartmentPersonnelListStatusSortOrderAsync(DepartmentId);

			foreach (var user in users)
			{
				var person = new GetPersonnelForCallGridResultData();
				person.UserId = user.UserId;
				person.Name = await UserHelper.GetFullNameForUser(personnelNames, user.UserName, user.UserId);

				var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, DepartmentId);

				if (group != null)
					person.Group = group.Name;

				var roles = await _personnelRolesService.GetRolesForUserAsync(user.UserId, DepartmentId);
				person.Roles = new List<string>();
				foreach (var role in roles)
				{
					person.Roles.Add(role.Name);
				}

				var currentStaffing = userStates.FirstOrDefault(x => x.UserId == user.UserId);
				if (currentStaffing != null)
				{
					var staffing = await CustomStatesHelper.GetCustomPersonnelStaffing(DepartmentId, currentStaffing);

					if (staffing != null)
					{
						person.Staffing = staffing.ButtonText;
						person.StaffingColor = staffing.ButtonClassToColor();
					}
				}
				else
				{
					person.Staffing = "Available";
					person.StaffingColor = "#000";
				}

				var currentStatus = lastUserActionlogs.FirstOrDefault(x => x.UserId == user.UserId);
				if (currentStatus != null)
				{
					var status = await CustomStatesHelper.GetCustomPersonnelStatus(DepartmentId, currentStatus);
					if (status != null)
					{
						person.Status = status.ButtonText;
						person.StatusColor = status.ButtonClassToColor();
					}

					person.Location = currentStatus.GeoLocationData;
				}
				else
				{
					person.Status = "Standing By";
					person.StatusColor = "#000";
				}

				person.Eta = "N/A";

				if (currentStatus != null)
				{
					if (personnelStatusSortOrder != null && personnelStatusSortOrder.Any())
					{
						var statusSorting = personnelStatusSortOrder.FirstOrDefault(x => x.StatusId == currentStatus.ActionTypeId);
						if (statusSorting != null)
							person.Weight = statusSorting.Weight;
						else
							person.Weight = 9000;
					}
					else
					{
						person.Weight = 9000;
					}
				}
				else
					person.Weight = 9000;

				result.Data.Add(person);
			}

			switch (personnelSortOrder)
			{
				case PersonnelSortOrders.Default:
					result.Data = result.Data.OrderBy(x => x.Weight).ToList();
					break;
				case PersonnelSortOrders.FirstName:
					result.Data = result.Data.OrderBy(x => x.Weight).ThenBy(x => x.FirstName).ToList();
					break;
				case PersonnelSortOrders.LastName:
					result.Data = result.Data.OrderBy(x => x.Weight).ThenBy(x => x.LastName).ToList();
					break;
				case PersonnelSortOrders.Group:
					result.Data = result.Data.OrderBy(x => x.Weight).ThenBy(x => x.GroupId).ToList();
					break;
				default:
					result.Data = result.Data.OrderBy(x => x.Weight).ToList();
					break;
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Returns all the groups for display in the new call groups table
		/// </summary>
		/// <returns>Array of GroupsForCallResult objects for each group in the department</returns>
		[HttpGet("GetGroupsForCallGrid")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetGroupsForCallGridResult>> GetGroupsForCallGrid()
		{
			var result = new GetGroupsForCallGridResult();
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			foreach (var group in groups)
			{
				GetGroupsForCallGridResultData groupJson = new GetGroupsForCallGridResultData();
				groupJson.GroupId = group.DepartmentGroupId.ToString();
				groupJson.Name = group.Name;

				if (group.Members != null)
					groupJson.Count = group.Members.Count;
				else
					groupJson.Count = 0;

				result.Data.Add(groupJson);
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		private async Task<UnitResultData> UnitWithLocationIfVisibleAsync(UnitResultData data, int unitId)
		{
			if (!await UnitLocationVisibility.CanSeeAsync(_authorizationService, unitId, UserId, DepartmentId))
				UnitLocationVisibility.Withhold(data);

			return data;
		}

		/// <summary>
		/// Nearest available unit board for an incident location. Lists every unit (a team, an apparatus or an
		/// individual set up as a unit) and every responder in the caller's dispatch scope, ranked available-first
		/// then by ETA, with status, live position, crew shift coverage and role mix. Nothing is dispatched.
		/// </summary>
		/// <param name="latitude">Incident latitude</param>
		/// <param name="longitude">Incident longitude</param>
		/// <param name="useRoadEta">Look up drive times for the closest available units; omit to follow the department setting</param>
		/// <param name="cancellationToken">Request cancellation</param>
		/// <returns>GetNearestUnitsResult with the board</returns>
		[HttpGet("GetNearestUnits")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetNearestUnitsResult>> GetNearestUnits(double latitude, double longitude, bool? useRoadEta = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			var board = await _nearestUnitService.GetBoardAsync(new NearestUnitRequest
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Latitude = latitude,
				Longitude = longitude,
				UseRoadEta = useRoadEta
			}, cancellationToken);

			var result = new GetNearestUnitsResult
			{
				Data = board,
				PageSize = board.Units.Count,
				Status = ResponseHelper.Success
			};

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Returns all the roles for display in the new call groups table
		/// </summary>
		/// <returns>Array of RolesForCallResult objects for each role in the department</returns>
		[HttpGet("GetRolesForCallGrid")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetRolesForCallGridResult>> GetRolesForCallGrid()
		{
			var result = new GetRolesForCallGridResult();
			var roles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);

			foreach (var role in roles)
			{
				var roleJson = new GetRolesForCallGridResultData();
				roleJson.RoleId = role.PersonnelRoleId.ToString();
				roleJson.Name = role.Name;

				if (role.Users != null)
					roleJson.Count = role.Users.Count;
				else
					roleJson.Count = 0;

				result.Data.Add(roleJson);
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Returns all the call quick templates
		/// </summary>
		/// <returns>Array of CallTemplateResult objects for each role in the department</returns>
		[HttpGet("GetCallTemplates")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetCallTemplatesResult>> GetCallTemplates()
		{
			var result = new GetCallTemplatesResult();

			var templates = await _templatesService.GetAllCallQuickTemplatesForDepartmentAsync(DepartmentId);

			foreach (var template in templates)
			{
				GetCallTemplatesResultData templateJson = new GetCallTemplatesResultData();
				templateJson.Id = template.CallQuickTemplateId.ToString();
				templateJson.IsDisabled = template.IsDisabled;
				templateJson.Name = template.Name;
				templateJson.CallName = template.CallName;
				templateJson.CallNature = template.CallNature;
				templateJson.CallType = template.CallType;
				templateJson.CallPriority = template.CallPriority;
				templateJson.CreatedByUserId = template.CreatedByUserId;
				templateJson.CreatedOn = template.CreatedOn;

				result.Data.Add(templateJson);
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}
	}
}
