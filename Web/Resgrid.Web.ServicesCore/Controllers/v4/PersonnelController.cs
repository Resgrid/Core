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
using System.Web;
using Resgrid.Framework;
using Resgrid.Web.Helpers;

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
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly ICustomStateService _customStateService;

		public PersonnelController(
			IUsersService usersService,
			IActionLogsService actionLogsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IUserStateService userStateService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			IDepartmentSettingsService departmentSettingsService,
			Model.Services.IAuthorizationService authorizationService,
			ICustomStateService customStateService
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
			_authorizationService = authorizationService;
			_customStateService = customStateService;
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

			if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(user.UserId, UserId, DepartmentId))
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);

				return result;
			}

			var profile = await _userProfileService.GetProfileByUserIdAsync(user.UserId);
			var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, DepartmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(user.UserId, DepartmentId);
			var action = await _actionLogsService.GetLastActionLogForUserAsync(user.UserId, DepartmentId);
			var userState = await _userStateService.GetLastUserStateByUserIdAsync(user.UserId);
			var canViewPII = await _authorizationService.CanUserViewPIIAsync(UserId, DepartmentId);

			result.Data = await ConvertPersonnelInfo(user, department, profile, group, roles, action, userState, canViewPII);

			return Ok(result);
		}

		/// <summary>
		/// Gets information about all users in a department
		/// </summary>
		/// <param name="activeFilter">The active filter to reduce personnel returned</param>
		/// <returns>PersonnelInfoResult with information pertaining to that user</returns>
		[HttpGet("GetAllPersonnelInfos")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<ActionResult<GetAllPersonnelInfosResult>> GetAllPersonnelInfos(string activeFilter)
		{
			var result = new GetAllPersonnelInfosResult();
			result.Data = new List<PersonnelInfoResultData>();

			string[] activeFilters = null;
			if (!String.IsNullOrWhiteSpace(activeFilter))
			{
				var filter = HttpUtility.UrlDecode(activeFilter);
				activeFilters = filter.Split(char.Parse("|"));
			}

			var filters = await GetFilterOptions();
			var actionLogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(DepartmentId);
			var userStates = await _userStateService.GetLatestStatesForDepartmentAsync(DepartmentId);
			var users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
			Department department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			var personnelSortOrder = await _departmentSettingsService.GetDepartmentPersonnelSortOrderAsync(DepartmentId);
			var personnelStatusSortOrder = await _departmentSettingsService.GetDepartmentPersonnelListStatusSortOrderAsync(DepartmentId);
			var canViewPII = await _authorizationService.CanUserViewPIIAsync(UserId, DepartmentId);

			foreach (var u in users)
			{
				if (!await _authorizationService.CanUserViewPersonViaMatrixAsync(u.UserId, UserId, DepartmentId))
					continue;

				var log = (from l in actionLogs
						   where l.UserId == u.UserId
						   select l).FirstOrDefault();

				var state = (from l in userStates
							 where l.UserId == u.UserId
							 select l).FirstOrDefault();

				var name = await UserHelper.GetFullNameForUser(personnelNames, u.UserName, u.UserId);
				var group = await _departmentGroupsService.GetGroupForUserAsync(u.UserId, DepartmentId);
				var roles = await _personnelRolesService.GetRolesForUserAsync(u.UserId, DepartmentId);
				var profile = await _userProfileService.GetProfileByUserIdAsync(u.UserId);

				var s = await ConvertPersonnelInfo(u, department, profile, group, roles, log, state, canViewPII);

				if (log != null)
				{
					if (personnelStatusSortOrder != null && personnelStatusSortOrder.Any())
					{
						var statusSorting = personnelStatusSortOrder.FirstOrDefault(x => x.StatusId == log.ActionTypeId);
						if (statusSorting != null)
							s.Weight = statusSorting.Weight;
						else
							s.Weight = 9000;
					}
					else
					{
						s.Weight = 9000;
					}
				}
				else
					s.Weight = 9000;

				if (activeFilter != null && activeFilter.Any())
				{
					foreach (var afilter in activeFilters)
					{
						var text = GetTextValue(afilter, filters);

						if (afilter.Substring(0, 2) == "G:")
						{
							if (group != null && group.Name != null && text == group.Name)
							{
								result.Data.Add(s);
								break;
							}
						}
						else if (afilter.Substring(0, 2) == "R:")
						{
							if (roles.Any(x => x.Name == text))
							{
								result.Data.Add(s);
								break;
							}
						}
						else if (afilter.Substring(0, 2) == "U:")
						{
							if (state != null && !String.IsNullOrWhiteSpace(text))
							{
								if (s.Staffing.ToString() == text || s.Staffing.ToString() == text.Replace(" ", ""))
								{
									result.Data.Add(s);
									break;
								}
							}
						}
						else if (afilter.Substring(0, 2) == "S:")
						{
							if (state != null && !String.IsNullOrWhiteSpace(text))
							{
								if (s.Status.ToString() == text || s.Status.ToString() == text.Replace(" ", ""))
								{
									result.Data.Add(s);
									break;
								}
							}
						}

					}
				}
				else
				{
					result.Data.Add(s);
				}
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
		/// Gets all the options available to filter personnel against compatible Resgrid APIs
		/// </summary>
		/// <returns>GetPersonnelFilterOptionsResult with information pertaining to each filter option</returns>
		[HttpGet("GetPersonnelFilterOptions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<ActionResult<GetPersonnelFilterOptionsResult>> GetPersonnelFilterOptions()
		{
			var result = new GetPersonnelFilterOptionsResult();
			result.Data = new List<FilterResult>();

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var roles = await _personnelRolesService.GetAllRolesForDepartmentAsync(DepartmentId);

			var customState = await _customStateService.GetActivePersonnelStateForDepartmentAsync(DepartmentId);
			List<CustomStateDetail> statuses = new List<CustomStateDetail>();

			if (customState == null || customState.GetActiveDetails() == null || customState.GetActiveDetails().Count() <= 0)
				statuses = _customStateService.GetDefaultPersonStatuses();
			else
				statuses = customState.GetActiveDetails();

			var customStateStaffing = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);
			List<CustomStateDetail> staffing = new List<CustomStateDetail>();

			if (customStateStaffing == null || customStateStaffing.GetActiveDetails() == null || customStateStaffing.GetActiveDetails().Count() <= 0)
				staffing = _customStateService.GetDefaultPersonStaffings();
			else
				staffing = customStateStaffing.GetActiveDetails();

			var everyone = new FilterResult()
			{
				Id = "0",
				Type = "",
				Name = "Everyone"
			};
			result.Data.Add(everyone);

			if (groups != null && groups.Any())
			{
				foreach(var group in groups)
				{
					result.Data.Add(new FilterResult()
					{
						Id = $"G:{group.DepartmentGroupId}",
						Type = "Groups",
						Name = group.Name
					});
				}
			}

			if (roles != null && roles.Any())
			{
				foreach(var role in roles)
				{
					result.Data.Add(new FilterResult()
					{
						Id = $"R:{role.PersonnelRoleId}",
						Type = "Roles",
						Name = role.Name
					});
				}
			}

			if (staffing != null && staffing.Any())
			{
				foreach(var staff in staffing)
				{
					result.Data.Add(new FilterResult()
					{
						Id = $"U:{staff.CustomStateDetailId}",
						Type = "Staffing",
						Name = staff.ButtonText
					});
				}
			}

			if (statuses != null && statuses.Any())
			{
				foreach (var stat in statuses)
				{
					result.Data.Add(new FilterResult()
					{
						Id = $"S:{stat.CustomStateDetailId}",
						Type = "Status",
						Name = stat.ButtonText
					});
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		public static async Task<PersonnelInfoResultData> ConvertPersonnelInfo(IdentityUser user, Department department, UserProfile profile,
			DepartmentGroup group, List<PersonnelRole> roles, ActionLog action, UserState userState, bool canViewPII)
		{
			var personnelData = new PersonnelInfoResultData();
			if (profile != null)
			{
				personnelData.FirstName = profile.FirstName;
				personnelData.LastName = profile.LastName;
				personnelData.IdentificationNumber = profile.IdentificationNumber;

				if (canViewPII)
					personnelData.MobilePhone = profile.MobileNumber;
			}
			else
			{
				personnelData.FirstName = "Unknown";
				personnelData.LastName = "Check Profile";
				personnelData.IdentificationNumber = "";
				personnelData.MobilePhone = "";
			}

			personnelData.DepartmentId = department.DepartmentId.ToString();
			personnelData.UserId = user.UserId.ToString();

			if (canViewPII)
				personnelData.EmailAddress = user.Email;

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

			if (userState == null)
			{
				personnelData.Staffing = "Available";
				personnelData.StaffingColor = "#000";
				personnelData.StaffingTimestamp = DateTime.UtcNow.TimeConverter(department);
			}
			else
			{
				var staffing = await CustomStatesHelper.GetCustomPersonnelStaffing(department.DepartmentId, userState);

				personnelData.StaffingId = userState.State.ToString();
				personnelData.StaffingTimestamp = userState.Timestamp.TimeConverter(department);
				personnelData.Staffing = staffing.ButtonText;
				personnelData.StaffingColor = staffing.ButtonClassToColor();
			}

			if (action == null)
			{
				personnelData.StatusTimestamp = DateTime.UtcNow.TimeConverter(department);
				personnelData.Status = "Standing By";
				personnelData.StatusColor = "#000";
			}
			else
			{
				var status = await CustomStatesHelper.GetCustomPersonnelStatus(department.DepartmentId, action);

				if (status != null)
				{
					personnelData.Status = status.ButtonText;
					personnelData.StatusColor = status.ButtonClassToColor();
				}
				else
				{
					personnelData.Status = "Standing By";
					personnelData.StatusColor = "#000";
				}

				personnelData.StatusId = action.ActionTypeId.ToString();
				personnelData.StatusTimestamp = action.Timestamp.TimeConverter(department);
				personnelData.Location = action.GeoLocationData;

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

		private async Task<List<FilterResult>> GetFilterOptions()
		{
			var result = new List<FilterResult>();

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var roles = await _personnelRolesService.GetAllRolesForDepartmentAsync(DepartmentId);

			var customState = await _customStateService.GetActivePersonnelStateForDepartmentAsync(DepartmentId);
			List<CustomStateDetail> statuses = new List<CustomStateDetail>();

			if (customState == null || customState.GetActiveDetails() == null || customState.GetActiveDetails().Count() <= 0)
				statuses = _customStateService.GetDefaultPersonStatuses();
			else
				statuses = customState.GetActiveDetails();

			var customStateStaffing = await _customStateService.GetActiveStaffingLevelsForDepartmentAsync(DepartmentId);
			List<CustomStateDetail> staffing = new List<CustomStateDetail>();

			if (customStateStaffing == null || customStateStaffing.GetActiveDetails() == null || customStateStaffing.GetActiveDetails().Count() <= 0)
				staffing = _customStateService.GetDefaultPersonStaffings();
			else
				staffing = customStateStaffing.GetActiveDetails();

			var everyone = new FilterResult()
			{
				Id = "0",
				Type = "",
				Name = "Everyone"
			};
			result.Add(everyone);

			if (groups != null && groups.Any())
			{
				foreach(var group in groups)
				{
					result.Add(new FilterResult()
					{
						Id = $"G:{group.DepartmentGroupId}",
						Type = "Groups",
						Name = group.Name
					});
				}
			}

			if (roles != null && roles.Any())
			{
				foreach(var role in roles)
				{
					result.Add(new FilterResult()
					{
						Id = $"R:{role.PersonnelRoleId}",
						Type = "Roles",
						Name = role.Name
					});
				}
			}

			if (staffing != null && staffing.Any())
			{
				foreach(var staff in staffing)
				{
					result.Add(new FilterResult()
					{
						Id = $"U:{staff.CustomStateDetailId}",
						Type = "Staffing",
						Name = staff.ButtonText
					});
				}
			}

			if (statuses != null && statuses.Any())
			{
				foreach (var stat in statuses)
				{
					result.Add(new FilterResult()
					{
						Id = $"S:{stat.CustomStateDetailId}",
						Type = "Status",
						Name = stat.ButtonText
					});
				}
			}

			/*var status1 = new FilterResult();
			status1.Id = string.Format("U:{0}", (int)UserStateTypes.Available);
			status1.Type = "Staffing";
			status1.Name = UserStateTypes.Available.GetDisplayString();
			result.Add(status1);

			var status2 = new FilterResult();
			status2.Id = string.Format("U:{0}", (int)UserStateTypes.Delayed);
			status2.Type = "Staffing";
			status2.Name = UserStateTypes.Delayed.GetDisplayString();
			result.Add(status2);

			var status3 = new FilterResult();
			status3.Id = string.Format("U:{0}", (int)UserStateTypes.Committed);
			status3.Type = "Staffing";
			status3.Name = UserStateTypes.Committed.GetDisplayString();
			result.Add(status3);

			var status4 = new FilterResult();
			status4.Id = string.Format("U:{0}", (int)UserStateTypes.OnShift);
			status4.Type = "Staffing";
			status4.Name = UserStateTypes.OnShift.GetDisplayString();
			result.Add(status4);

			var status5 = new FilterResult();
			status5.Id = string.Format("U:{0}", (int)UserStateTypes.Unavailable);
			status5.Type = "Staffing";
			status5.Name = UserStateTypes.Unavailable.GetDisplayString();
			result.Add(status5);*/

			return result;
		}

		private string GetTextValue(string filter, List<FilterResult> filters)
		{
			return filters.Where(x => x.Id == filter).Select(y => y.Name).FirstOrDefault();
		}
	}
}
