using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Stations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations that can be performed against resgrid recipients, usually for sending messages
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class StationsController : V3AuthenticatedApiControllerbase
	{
		private ICallsService _callsService;
		private IDepartmentsService _departmentsService;
		private IUserProfileService _userProfileService;
		private IGeoLocationProvider _geoLocationProvider;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IUnitsService _unitsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUserStateService _userStateService;

		public StationsController(
			ICallsService callsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IGeoLocationProvider geoLocationProvider,
			IAuthorizationService authorizationService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			IUnitsService unitsService,
			IActionLogsService actionLogsService,
			IUserStateService userStateService)
		{
			_callsService = callsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_geoLocationProvider = geoLocationProvider;
			_authorizationService = authorizationService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_unitsService = unitsService;
			_actionLogsService = actionLogsService;
			_userStateService = userStateService;
		}

		/// <summary>
		/// Returns all the available responding options (Calls/Stations) for the department
		/// </summary>
		/// <returns>Array of RecipientResult objects for each responding option in the department</returns>
		[HttpGet("GetStationResources")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<StationResult>>> GetStationResources()
		{
			var result = new List<StationResult>();

			var unitStatuses = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);
			var actionLogs = await _actionLogsService.GetAllActionLogsForDepartmentAsync(DepartmentId);
			var userStates = await _userStateService.GetLatestStatesForDepartmentAsync(DepartmentId);
			var stations = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var userGroups = await _departmentGroupsService.GetAllDepartmentGroupsForDepartmentAsync(DepartmentId);
			var users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			Department department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			Parallel.ForEach(users, u =>
			{
				var log = (from l in actionLogs
									 where l.UserId == u.UserId
									 select l).FirstOrDefault();

				var state = (from l in userStates
										 where l.UserId == u.UserId
										 select l).FirstOrDefault();

				var s = new StationResult();
				s.Id = u.UserId.ToString();
				s.Typ = 1;

				if (log != null)
				{
					s.Sts = log.ActionTypeId;
					s.Stm = log.Timestamp.TimeConverter(department);

					if (log.DestinationId.HasValue)
					{
						if (log.ActionTypeId == (int)ActionTypes.RespondingToStation)
						{
							s.Did = log.DestinationId.GetValueOrDefault();

							var group = stations.First(x => x.DepartmentGroupId == log.DestinationId.Value);
							s.Dnm = group.Name;
						}
						else if (log.ActionTypeId == (int)ActionTypes.AvailableStation)
						{
							s.Did = log.DestinationId.GetValueOrDefault();

							var group = stations.First(x => x.DepartmentGroupId == log.DestinationId.Value);
							s.Dnm = group.Name;
						}
					}
				}
				else
				{
					s.Sts = (int)ActionTypes.StandingBy;
					s.Stm = DateTime.UtcNow.TimeConverter(department);
				}

				if (s.Did == 0)
				{
					if (userGroups.ContainsKey(u.UserId))
					{
						var homeGroup = userGroups[u.UserId];
						if (homeGroup != null && homeGroup.Type.HasValue &&
								((DepartmentGroupTypes)homeGroup.Type) == DepartmentGroupTypes.Station)
						{
							s.Did = homeGroup.DepartmentGroupId;
							s.Dnm = homeGroup.Name;
						}
					}
				}

				if (state != null)
				{
					s.Ste = state.State;
					s.Stt = state.Timestamp.TimeConverter(department);
				}
				else
				{
					s.Ste = (int)UserStateTypes.Available;
					s.Stt = DateTime.UtcNow.TimeConverter(department);
				}

				if (!String.IsNullOrWhiteSpace(s.Dnm))
					result.Add(s);
			});

			Parallel.ForEach(unitStatuses, unit =>
			{
				var unitResult = new StationResult();
				var savedUnit = units.FirstOrDefault(x => x.UnitId == unit.UnitId);

				if (savedUnit != null)
				{
					unitResult.Id = savedUnit.UnitId.ToString();
					//unitResult.Nme = savedUnit.Name;
					unitResult.Typ = 2;
					unitResult.Sts = unit.State;
					unitResult.Stm = unit.Timestamp.TimeConverter(department);

					if (savedUnit.StationGroupId.HasValue)
					{
						unitResult.Did = savedUnit.StationGroupId.Value;
						unitResult.Dnm = stations.First(x => x.DepartmentGroupId == savedUnit.StationGroupId.Value).Name;

						result.Add(unitResult);
					}
				}
			});

			return Ok(result);
		}
	}
}
