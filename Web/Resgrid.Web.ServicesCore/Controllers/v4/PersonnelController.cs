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
using Resgrid.Web.Services.Models.v4.Personnel;
using Resgrid.Model;
using Resgrid.Model.Identity;
using System;
using Resgrid.Model.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Operations to perform against personnel in a department
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class PersonnelController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUserStateService _userStateService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public PersonnelController(
			IUsersService usersService,
			IActionLogsService actionLogsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IUserStateService userStateService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			IDepartmentSettingsService departmentSettingsService
			)
		{
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_userStateService = userStateService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_departmentSettingsService = departmentSettingsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets information about a specific person
		/// </summary>
		/// <param name="userId">UserId of the person to get info for</param>
		/// <returns>PersonnelInfoResult with information pertaining to that user</returns>
		[HttpGet("GetPersonnelInfo")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<ActionResult<PersonnelInfoResult>> GetPersonnelInfo(string userId)
		{
			var result = new PersonnelInfoResult();
			var user = _usersService.GetUserById(userId);

			if (user == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);

				return result;
			}

			var department = await _departmentsService.GetDepartmentByUserIdAsync(user.UserId);
			if (department == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);

				return result;
			}

			if (department.DepartmentId != DepartmentId)
				return Unauthorized();

			var profile = await _userProfileService.GetProfileByUserIdAsync(user.UserId);
			var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, DepartmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(user.UserId, DepartmentId);
			var action = await _actionLogsService.GetLastActionLogForUserAsync(user.UserId, DepartmentId);
			var userState = await _userStateService.GetLastUserStateByUserIdAsync(user.UserId);

			result.Data = ConvertPersonnelInfo(user, department, profile, group, roles, action, userState);

			return Ok(result);
		}

		public static PersonnelInfoResultData ConvertPersonnelInfo(IdentityUser user, Department department, UserProfile profile,
			DepartmentGroup group, List<PersonnelRole> roles, ActionLog action, UserState userState)
		{
			var personnelData = new PersonnelInfoResultData();
			if (profile != null)
			{
				personnelData.FirstName = profile.FirstName;
				personnelData.LastName = profile.LastName;
				personnelData.IdentificationNumber = profile.IdentificationNumber;
				personnelData.MobilePhone = profile.MobileNumber;
			}
			else
			{
				personnelData.FirstName = "Unknown";
				personnelData.LastName = "Check Profile";
				personnelData.IdentificationNumber = "";
				personnelData.MobilePhone = "";
			}
			personnelData.EmailAddress = user.Email;
			personnelData.DepartmentId = department.DepartmentId.ToString();
			personnelData.UserId = user.UserId.ToString();

			if (group != null)
			{
				personnelData.GroupId = group.DepartmentGroupId.ToString();
				personnelData.GroupName = group.Name;
			}

			personnelData.Roles = new List<string>();
			if (roles != null && roles.Count > 0)
			{
				foreach (var role in roles)
				{
					personnelData.Roles.Add(role.Name);
				}
			}

			personnelData.StatusId = ((int)ActionTypes.StandingBy).ToString();
			personnelData.StaffingId = userState.State.ToString();
			personnelData.StaffingTimestamp = userState.Timestamp.TimeConverter(department);

			if (action == null)
			{
				personnelData.StatusTimestamp = DateTime.UtcNow.TimeConverter(department);
			}
			else
			{
				personnelData.StatusId = action.ActionTypeId.ToString();
				personnelData.StatusTimestamp = action.Timestamp.TimeConverter(department);

				if (action.DestinationId.HasValue)
				{
					if (action.ActionTypeId == (int)ActionTypes.RespondingToScene)
						personnelData.StatusDestinationId = action.DestinationId.Value.ToString();
					else if (action.ActionTypeId == (int)ActionTypes.RespondingToStation)
						personnelData.StatusDestinationId = action.DestinationId.Value.ToString();
					else if (action.ActionTypeId == (int)ActionTypes.AvailableStation)
						personnelData.StatusDestinationId = action.DestinationId.Value.ToString();
				}
			}

			return personnelData;
		}
	}
}
