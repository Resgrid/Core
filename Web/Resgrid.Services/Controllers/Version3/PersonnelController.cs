using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Services.CoreWeb;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Controllers.Version3.Models.Personnel;
using System;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Cors;
using System.Net.Http;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against personnel in a department
	/// </summary>
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
		private IWebEventPublisher _webEventPublisher;

		public PersonnelController(
			IUsersService usersService,
			IActionLogsService actionLogsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IWebEventPublisher webEventPublisher,
			IUserStateService userStateService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService
			)
		{
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_webEventPublisher = webEventPublisher;
			_userStateService = userStateService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Get's all the personnel in a department and their current status and staffing information
		/// </summary>
		/// <example>
		/// $ curl https://api.resgrid.com/api/v2/Personnel/GetPersonnelStatuses -u VXNlck5hbWV8MXxBQkNE:
		/// </example>
		/// <returns>List of PersonnelStatusResult objects, with status and staffing information for each user.</returns>
		[AcceptVerbs("GET")]
		public List<PersonnelStatusResult> GetPersonnelStatuses()
		{
			var results = new List<PersonnelStatusResult>();

			var actionLogs = _actionLogsService.GetActionLogsForDepartment(DepartmentId);
			var userStates = _userStateService.GetLatestStatesForDepartment(DepartmentId);
			var users = _departmentsService.GetAllUsersForDepartment(DepartmentId);
			Department department = _departmentsService.GetDepartmentById(DepartmentId, false);

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
					s.AUtc = log.Timestamp;

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
					s.AUtc = DateTime.UtcNow;
				}

				if (state != null)
				{
					s.Ste = state.State;
					s.Stm = state.Timestamp.TimeConverter(department);
					s.SUtc = state.Timestamp;
				}
				else
				{
					s.Ste = (int)UserStateTypes.Available;
					s.Stm = DateTime.UtcNow.TimeConverter(department);
					s.SUtc = DateTime.UtcNow;
				}
				results.Add(s);
			}


			return results;
		}

		/// <summary>
		/// Get's all the personnel in a department and their current status and staffing information with a filter
		/// </summary>
		/// <example>
		/// $ curl https://api.resgrid.com/api/v2/Personnel/GetPersonnelStatuses -u VXNlck5hbWV8MXxBQkNE:
		/// </example>
		/// <returns>List of PersonnelStatusResult objects, with status and staffing information for each user.</returns>
		[AcceptVerbs("GET")]
		public List<PersonnelStatusResult> GetPersonnelStatuses(string activeFilter)
		{
			var results = new List<PersonnelStatusResult>();
			var filter = HttpUtility.UrlDecode(activeFilter);
			var activeFilters = filter.Split(char.Parse("|"));
			var filters = GetFilterOptions();
			var actionLogs = _actionLogsService.GetActionLogsForDepartment(DepartmentId);
			var userStates = _userStateService.GetLatestStatesForDepartment(DepartmentId);
			var users = _departmentsService.GetAllUsersForDepartment(DepartmentId);
			Department department = _departmentsService.GetDepartmentById(DepartmentId, false);
			var allGroups = _departmentGroupsService.GetAllDepartmentGroupsForDepartment(DepartmentId);
			var allRoles = _personnelRolesService.GetAllRolesForUsersInDepartment(DepartmentId);

			Parallel.ForEach(users, u =>
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

				DepartmentGroup userGroup = null;
				if (allGroups.ContainsKey(u.UserId))
					userGroup = allGroups[u.UserId];

				var roles = new List<PersonnelRole>();
				if (allRoles.ContainsKey(u.UserId))
					roles = allRoles[u.UserId];

				foreach (var afilter in activeFilters)
				{
					var text = GetTextValue(afilter, filters);

					if (afilter.Substring(0, 2) == "G:")
					{
						if (userGroup != null && text == userGroup.Name)
						{
							results.Add(s);
							break;
						}
					}
					else if (afilter.Substring(0, 2) == "R:")
					{
						if (roles.Any(x => x.Name == text))
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
			});

			return results;
		}

		/// <summary>
		/// Gets information about a specific person
		/// </summary>
		/// <param name="userId">UserId of the person to get info for</param>
		/// <returns>PersonnelInfoResult with information pertaining to that user</returns>
		[AcceptVerbs("GET")]
		public PersonnelInfoResult GetPersonnelInfo(string userId)
		{
			var result = new PersonnelInfoResult();
			var user = _usersService.GetUserById(userId);


			if (user == null)
				throw HttpStatusCode.NotFound.AsException();

			var department = _departmentsService.GetDepartmentByUserId(user.UserId);

			if (department == null)
				throw HttpStatusCode.NotFound.AsException();

			if (department.DepartmentId != DepartmentId)
				throw HttpStatusCode.Unauthorized.AsException();

			var profile = _userProfileService.GetProfileByUserId(user.UserId);
			var group = _departmentGroupsService.GetGroupForUser(user.UserId, DepartmentId);
			var roles = _personnelRolesService.GetRolesForUser(user.UserId, DepartmentId);

			if (profile != null)
			{
				result.Fnm = profile.FirstName;
				result.Lnm = profile.LastName;
				result.Id = profile.IdentificationNumber;
				result.Mnu = profile.MobileNumber;
			}
			else
			{
				result.Fnm = "Unknwon";
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

			var action = _actionLogsService.GetLastActionLogForUser(user.UserId, DepartmentId);
			var userState = _userStateService.GetLastUserStateByUserId(user.UserId);

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

			return result;
		}

		public string GetTextValue(string filter, List<FilterResult> filters)
		{
			return filters.Where(x => x.Id == filter).Select(y => y.Name).FirstOrDefault();
		}

		private List<FilterResult> GetFilterOptions()
		{
			var result = new List<FilterResult>();

			var stations = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);
			var roles = _personnelRolesService.GetRolesForDepartment(DepartmentId);

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

		public HttpResponseMessage Options()
		{
			var response = new HttpResponseMessage();
			response.StatusCode = HttpStatusCode.OK;
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			response.Headers.Add("Access-Control-Request-Headers", "*");
			response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

			return response;
		}
	}
}
