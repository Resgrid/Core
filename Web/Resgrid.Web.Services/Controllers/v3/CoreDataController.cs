using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.CoreData;
using Resgrid.Web.Services.Controllers.Version3.Models.Groups;
using Resgrid.Web.Services.Controllers.Version3.Models.Personnel;
using Resgrid.Web.Services.Controllers.Version3.Models.Roles;
using Resgrid.Web.Services.Controllers.Version3.Models.Security;
using Resgrid.Web.Services.Controllers.Version3.Models.Units;
using CoreDataResult = Resgrid.Web.Services.Controllers.Version3.Models.CoreDataResult;
using Resgrid.Web.Services.Controllers.Version3.Models.CallPriorities;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;


namespace Resgrid.Web.Services.Controllers.Version3
{
	[Produces("application/json")]
	[Route("api/v{version:ApiVersion}/[controller]")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class CoreDataController : V3AuthenticatedApiControllerbase
	{
		private readonly IUsersService _usersService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ICustomStateService _customStateService;
		private readonly IPermissionsService _permissionsService;
		private readonly ICallsService _callsService;
		private readonly IFirebaseService _firebaseService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public CoreDataController(
			IUsersService usersService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IUnitsService unitsService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			ICustomStateService customStateService,
			IPermissionsService permissionsService,
			ICallsService callsService,
			IFirebaseService firebaseService,
			IDepartmentSettingsService departmentSettingsService
			)
		{
			_usersService = usersService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_unitsService = unitsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_customStateService = customStateService;
			_permissionsService = permissionsService;
			_callsService = callsService;
			_firebaseService = firebaseService;
			_departmentSettingsService = departmentSettingsService;
		}

		/// <summary>
		/// Gets the core data.
		/// </summary>
		/// <returns>CoreDataResult.</returns>
		[HttpGet("GetCoreData")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CoreDataResult>> GetCoreData()
		{
			var results = new CoreDataResult();
			results.Personnel = new List<PersonnelInfoResult>();
			results.Groups = new List<GroupInfoResult>();
			results.Units = new List<UnitInfoResult>();
			results.Roles = new List<RoleInfoResult>();
			results.Statuses = new List<CustomStatusesResult>();
			results.Priorities = new List<CallPriorityResult>();
			results.Departments = new List<JoinedDepartmentResult>();
			results.PersonnelListStatusOrders = new List<PersonnelListStatusOrder>();

			var users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
			var groups = await _departmentGroupsService.GetAllDepartmentGroupsForDepartmentAsync(DepartmentId);
			var rolesForUsersInDepartment = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(DepartmentId);
			var allRoles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);
			var allProfiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId);
			var allGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var unitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);
			var callPriorites = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId);
			var personnelListStatusOrderings = await _departmentSettingsService.GetDepartmentPersonnelListStatusSortOrderAsync(DepartmentId);
			var personnelListSortOrder = await _departmentSettingsService.GetDepartmentPersonnelSortOrderAsync(DepartmentId);


			if (personnelListStatusOrderings != null)
				results.PersonnelListStatusOrders = personnelListStatusOrderings;

			results.PersonnelNameSorting = (int)personnelListSortOrder;

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

				var result = new PersonnelInfoResult();

				if (profile != null)
				{
					result.Fnm = profile.FirstName;
					result.Lnm = profile.LastName;
					result.Id = profile.IdentificationNumber;

					
					result.Mnu = profile.MobileNumber;
				}
				else
				{
					result.Fnm = "Unknown";
					result.Lnm = "Check Profile";
					result.Id = "";
					result.Mnu = "";
				}

				if (user != null)
					result.Eml = user.Email;

				result.Did = DepartmentId;
				result.Uid = user.UserId.ToString();

				if (group != null)
				{
					result.Gid = group.DepartmentGroupId;
					result.Gnm = group.Name;
				}

				result.Roles = new List<string>();
				if (roles != null && roles.Count > 0)
				{
					foreach (var role in roles)
					{
						if (role != null)
							result.Roles.Add(role.Name);
					}
				}

				results.Personnel.Add(result);
			}


			results.Rights = new DepartmentRightsResult();
			var currentUser = await _usersService.GetUserByNameAsync(UserName);

			if (currentUser == null)
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			results.Rights.Adm = department.IsUserAnAdmin(currentUser.UserId);
			results.Rights.Grps = new List<GroupRight>();

			var currentGroup = await _departmentGroupsService.GetGroupForUserAsync(currentUser.UserId, DepartmentId);

			if (currentGroup != null)
			{
				var groupRight = new GroupRight();
				groupRight.Gid = currentGroup.DepartmentGroupId;
				groupRight.Adm = currentGroup.IsUserGroupAdmin(currentUser.UserId);

				results.Rights.Grps.Add(groupRight);
			}

			foreach (var group in allGroups)
			{
				var groupInfo = new GroupInfoResult();
				groupInfo.Gid = group.DepartmentGroupId;
				groupInfo.Nme = group.Name;

				if (group.Type.HasValue)
					groupInfo.Typ = group.Type.Value;

				if (group.Address != null)
					groupInfo.Add = group.Address.FormatAddress();

				results.Groups.Add(groupInfo);
			}

			foreach (var unit in units)
			{
				var unitResult = new UnitInfoResult();
				unitResult.Uid = unit.UnitId;
				unitResult.Did = DepartmentId;
				unitResult.Nme = unit.Name;
				unitResult.Typ = unit.Type;

				if (!string.IsNullOrWhiteSpace(unit.Type))
				{
					var unitType = unitTypes.FirstOrDefault(x => x.Type == unit.Type);

					if (unitType != null)
						unitResult.Cid = unitType.CustomStatesId.GetValueOrDefault();
				}
				else
				{
					unitResult.Cid = 0;
				}

				if (unit.StationGroup != null)
				{
					unitResult.Sid = unit.StationGroup.DepartmentGroupId;
					unitResult.Snm = unit.StationGroup.Name;
				}

				results.Units.Add(unitResult);
			}

			foreach (var role in allRoles)
			{
				var roleResult = new RoleInfoResult();
				roleResult.Rid = role.PersonnelRoleId;
				roleResult.Nme = role.Name;

				results.Roles.Add(roleResult);
			}

			var customStates = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(DepartmentId);

			foreach (var customState in customStates)
			{
				if (customState != null)
				{
					if (customState.IsDeleted || customState.Details == null)
						continue;

					foreach (var stateDetail in customState.Details)
					{
						if (stateDetail == null || stateDetail.IsDeleted)
							continue;

						var customStateResult = new CustomStatusesResult();
						customStateResult.Id = stateDetail.CustomStateDetailId;
						customStateResult.Type = customState.Type;
						customStateResult.StateId = stateDetail.CustomStateId;
						customStateResult.Text = stateDetail.ButtonText;
						customStateResult.BColor = stateDetail.ButtonColor;
						customStateResult.Color = stateDetail.TextColor;
						customStateResult.Gps = stateDetail.GpsRequired;
						customStateResult.Note = stateDetail.NoteType;
						customStateResult.Detail = stateDetail.DetailType;

						if (customState.IsDeleted)
							customStateResult.IsDeleted = true;
						else
							customStateResult.IsDeleted = stateDetail.IsDeleted;

						results.Statuses.Add(customStateResult);
					}
				}
			}

			foreach (var priority in callPriorites)
			{
				var priorityResult = new CallPriorityResult();
				priorityResult.Id = priority.DepartmentCallPriorityId;
				priorityResult.DepartmentId = priority.DepartmentId;
				priorityResult.Name = priority.Name;
				priorityResult.Color = priority.Color;
				priorityResult.Sort = priority.Sort;
				priorityResult.IsDeleted = priority.IsDeleted;
				priorityResult.IsDefault = priority.IsDefault;

				results.Priorities.Add(priorityResult);
			}

			var members = await _departmentsService.GetAllDepartmentsForUserAsync(UserId);
			foreach (var member in members)
			{
				if (member.IsDeleted)
					continue;

				if (member.IsDisabled.GetValueOrDefault())
					continue;

				var depRest = new JoinedDepartmentResult();
				depRest.Did = member.DepartmentId;
				depRest.Nme = member.Department.Name;

				results.Departments.Add(depRest);
			}

			return Ok(results);
		}
	}
}
