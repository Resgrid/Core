using System.Collections.Generic;
using System.Linq;
using System.Web;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Personnel;
using System;
using System.Threading.Tasks;
using Amazon.Runtime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;


namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against personnel in a department
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class PersonnelController : V3AuthenticatedApiControllerbase
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
		/// Get's all the personnel in a department and their current status and staffing information with a filter
		/// </summary>
		/// <example>
		/// $ curl https://api.resgrid.com/api/v2/Personnel/GetPersonnelStatuses -u VXNlck5hbWV8MXxBQkNE:
		/// </example>
		/// <returns>List of PersonnelStatusResult objects, with status and staffing information for each user.</returns>
		[HttpGet("GetPersonnelStatuses")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<PersonnelStatusResult>>> GetPersonnelStatuses(string activeFilter)
		{
			var results = new List<PersonnelStatusResult>();

			string[] activeFilters = null;
			if (!String.IsNullOrWhiteSpace(activeFilter))
			{
				var filter = HttpUtility.UrlDecode(activeFilter);
				activeFilters = filter.Split(char.Parse("|"));
			}

			var filters = await GetFilterOptions();
			var actionLogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(DepartmentId);
			var userStates = await _userStateService.GetLatestStatesForDepartmentAsync(DepartmentId);
			//var users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);

			var users = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(DepartmentId, false, false, false);


			Department department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			//var allGroups = await _departmentGroupsService.GetAllDepartmentGroupsForDepartmentAsync(DepartmentId);
			//var allRoles = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(DepartmentId);

			var personnelSortOrder = await _departmentSettingsService.GetDepartmentPersonnelSortOrderAsync(DepartmentId);
			var personnelStatusSortOrder = await _departmentSettingsService.GetDepartmentPersonnelListStatusSortOrderAsync(DepartmentId);

			foreach (var u in users)
			{
				var log = (from l in actionLogs
						   where l.UserId == u.UserId
						   select l).FirstOrDefault();

				var state = (from l in userStates
							 where l.UserId == u.UserId
							 select l).FirstOrDefault();

				var s = new PersonnelStatusResult();
				s.Uid = u.UserId.ToString();

				if (log != null)
				{
					s.Atp = log.ActionTypeId;
					s.Atm = log.Timestamp.TimeConverter(department);

					if (log.DestinationId.HasValue)
					{
						if (log.ActionTypeId == (int)ActionTypes.RespondingToScene)
							s.Did = log.DestinationId.Value.ToString();
						else if (log.ActionTypeId == (int)ActionTypes.RespondingToStation)
							s.Did = log.DestinationId.Value.ToString();
						else if (log.ActionTypeId == (int)ActionTypes.AvailableStation)
							s.Did = log.DestinationId.Value.ToString();
					}
				}
				else
				{
					s.Atp = (int)ActionTypes.StandingBy;
					s.Atm = DateTime.UtcNow.TimeConverter(department);
				}

				if (state != null)
				{
					s.Ste = state.State;
					s.Stm = state.Timestamp.TimeConverter(department);
				}
				else
				{
					s.Ste = (int)UserStateTypes.Available;
					s.Stm = DateTime.UtcNow.TimeConverter(department);
				}

				//DepartmentGroup userGroup = null;
				//if (allGroups.ContainsKey(u.UserId))
				//	userGroup = allGroups[u.UserId];

				//var roles = new List<PersonnelRole>();
				//if (allRoles.ContainsKey(u.UserId))
				//	roles = allRoles[u.UserId];

				if (u.DepartmentGroupId.HasValue)
					s.Gid = u.DepartmentGroupId.Value;
				
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
							if (u.DepartmentGroupName != null && text == u.DepartmentGroupName)
							{
								results.Add(s);
								break;
							}
						}
						else if (afilter.Substring(0, 2) == "R:")
						{
							if (u.RoleNamesList.Any(x => x == text))
							{
								results.Add(s);
								break;
							}
						}
						else if (afilter.Substring(0, 2) == "U:")
						{
							if (s.Ste.ToString() == text || s.Ste.ToString() == text.Replace(" ", ""))
							{
								results.Add(s);
								break;
							}
						}

					}
				}
				else
				{
					results.Add(s);
				}
			}

			switch (personnelSortOrder)
			{
				case PersonnelSortOrders.Default:
					results = results.OrderBy(x => x.Weight).ToList();
					break;
				case PersonnelSortOrders.FirstName:
					results = results.OrderBy(x => x.Weight).ThenBy(x => users.First(y => y.UserId == x.Uid).FirstName).ToList();
					break;
				case PersonnelSortOrders.LastName:
					results = results.OrderBy(x => x.Weight).ThenBy(x => users.First(y => y.UserId == x.Uid).LastName).ToList();
					break;
				case PersonnelSortOrders.Group:
					results = results.OrderBy(x => x.Weight).ThenBy(x => x.Gid).ToList();
					break;
				default:
					results = results.OrderBy(x => x.Weight).ToList();
					break;
			}

			return Ok(results);
		}

		/// <summary>
		/// Gets information about a specific person
		/// </summary>
		/// <param name="userId">UserId of the person to get info for</param>
		/// <returns>PersonnelInfoResult with information pertaining to that user</returns>
		[HttpGet("GetPersonnelInfo")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<PersonnelInfoResult>> GetPersonnelInfo(string userId)
		{
			var result = new PersonnelInfoResult();
			var user = _usersService.GetUserById(userId);


			if (user == null)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByUserIdAsync(user.UserId);

			if (department == null)
				return NotFound();

			if (department.DepartmentId != DepartmentId)
				return Unauthorized();

			var profile = await _userProfileService.GetProfileByUserIdAsync(user.UserId);
			var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, DepartmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(user.UserId, DepartmentId);

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
			result.Eml = user.Email;
			result.Did = department.DepartmentId;
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
					result.Roles.Add(role.Name);
				}
			}

			var action = await _actionLogsService.GetLastActionLogForUserAsync(user.UserId, DepartmentId);
			var userState = await _userStateService.GetLastUserStateByUserIdAsync(user.UserId);

			result.Ats = (int)ActionTypes.StandingBy;
			result.Stf = userState.State;
			result.Stm = userState.Timestamp.TimeConverter(department);

			if (action == null)
			{
				result.Stm = DateTime.UtcNow.TimeConverter(department);
			}
			else
			{
				result.Ats = action.ActionTypeId;
				result.Atm = action.Timestamp.TimeConverter(department);

				if (action.DestinationId.HasValue)
				{
					if (action.ActionTypeId == (int)ActionTypes.RespondingToScene)
						result.Did = action.DestinationId.Value;
					else if (action.ActionTypeId == (int)ActionTypes.RespondingToStation)
						result.Did = action.DestinationId.Value;
					else if (action.ActionTypeId == (int)ActionTypes.AvailableStation)
						result.Did = action.DestinationId.Value;
				}
			}

			return Ok(result);
		}

		private string GetTextValue(string filter, List<FilterResult> filters)
		{
			return filters.Where(x => x.Id == filter).Select(y => y.Name).FirstOrDefault();
		}

		private async Task<List<FilterResult>> GetFilterOptions()
		{
			var result = new List<FilterResult>();

			var stations = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var roles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);

			foreach (var s in stations)
			{
				var respondingTo = new FilterResult();
				respondingTo.Id = string.Format("G:{0}", s.DepartmentGroupId);
				respondingTo.Type = "Group";
				respondingTo.Name = s.Name;

				result.Add(respondingTo);
			}

			foreach (var r in roles)
			{
				var respondingTo = new FilterResult();
				respondingTo.Id = string.Format("R:{0}", r.PersonnelRoleId);
				respondingTo.Type = "Role";
				respondingTo.Name = r.Name;

				result.Add(respondingTo);
			}

			var status1 = new FilterResult();
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
			result.Add(status5);

			return result;
		}
	}
}
