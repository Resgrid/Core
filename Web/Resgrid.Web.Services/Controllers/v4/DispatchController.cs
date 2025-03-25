using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Web.Services.Models.v4.Forms;
using Resgrid.Web.Services.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Web.Services.Models.v4.Personnel;
using Resgrid.Web.Services.Models.v4.Dispatch;
using Resgrid.Web.Services.Models.v4.Groups;
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
			Model.Services.IAuthorizationService authorizationService
			)
		{
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

			foreach (var unit in units)
			{
				if (!string.IsNullOrWhiteSpace(unit.Type))
				{
					var unitType = unitTypes.FirstOrDefault(x => x.Type == unit.Type);

					result.Units.Add(UnitsController.ConvertUnitsData(unit, unitStatuses.FirstOrDefault(x => x.UnitId == unit.UnitId), null, TimeZone));
				}
				else
				{
					result.Units.Add(UnitsController.ConvertUnitsData(unit, unitStatuses.FirstOrDefault(x => x.UnitId == unit.UnitId), null, TimeZone));
				}

				// Add unit roles for this unit
				var roles = await _unitsService.GetRolesForUnitAsync(unit.UnitId);
				foreach (var role in roles)
				{
					result.UnitRoles.Add(UnitRolesController.ConvertUnitRoleData(role));
				}
			}

			foreach (var us in unitStatuses)
			{
				var customState = await CustomStatesHelper.GetCustomUnitState(us);
				var latestUnitLocation = await _unitsService.GetLatestUnitLocationAsync(us.UnitId, us.Timestamp);

				var group = allGroups.FirstOrDefault(x => x.DepartmentGroupId == us.Unit.StationGroupId);
				result.UnitStatuses.Add(UnitStatusController.ConvertUnitStatusData(us.Unit, us, latestUnitLocation, customState, group, TimeZone, activeCalls, allGroups));
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
			result.Data.Statuses = new List<CustomStatusResultData>();

			var type = await _unitsService.GetUnitTypeByNameAsync(DepartmentId, unit.Type);
			var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);

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
				var customStateResult = new CustomStatusResultData();
				customStateResult.Id = "0";
				customStateResult.Type = 0;
				customStateResult.StateId = "0";
				customStateResult.Text = "Available";
				customStateResult.BColor = "#FFFFFF";
				customStateResult.Color = "#000000";
				customStateResult.Gps = false;
				customStateResult.Note = 0;
				customStateResult.Detail = 0;

				result.Data.Statuses.Add(customStateResult);

				var customStateResult2 = new CustomStatusResultData();
				customStateResult2.Id = "3";
				customStateResult2.Type = 3;
				customStateResult2.StateId = "3";
				customStateResult2.Text = "Committed";
				customStateResult2.BColor = "#FFFFFF";
				customStateResult2.Color = "#000000";
				customStateResult2.Gps = false;
				customStateResult2.Note = 0;
				customStateResult2.Detail = 0;

				result.Data.Statuses.Add(customStateResult2);

				var customStateResult3 = new CustomStatusResultData();
				customStateResult3.Id = "1";
				customStateResult3.Type = 1;
				customStateResult3.StateId = "1";
				customStateResult3.Text = "Delayed";
				customStateResult3.BColor = "#FFFFFF";
				customStateResult3.Color = "#000000";
				customStateResult3.Gps = false;
				customStateResult3.Note = 0;
				customStateResult3.Detail = 0;

				result.Data.Statuses.Add(customStateResult3);

				var customStateResult4 = new CustomStatusResultData();
				customStateResult4.Id = "4";
				customStateResult4.Type = 4;
				customStateResult4.StateId = "4";
				customStateResult4.Text = "Out Of Service";
				customStateResult4.BColor = "#FFFFFF";
				customStateResult4.Color = "#000000";
				customStateResult4.Gps = false;
				customStateResult4.Note = 0;
				customStateResult4.Detail = 0;

				result.Data.Statuses.Add(customStateResult4);

				var customStateResult5 = new CustomStatusResultData();
				customStateResult5.Id = "2";
				customStateResult5.Type = 2;
				customStateResult5.StateId = "2";
				customStateResult5.Text = "Unavailable";
				customStateResult5.BColor = "#FFFFFF";
				customStateResult5.Color = "#000000";
				customStateResult5.Gps = false;
				customStateResult5.Note = 0;
				customStateResult5.Detail = 0;

				result.Data.Statuses.Add(customStateResult5);
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
