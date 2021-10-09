using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Units;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Web.Services.Controllers.Version3.Models.Personnel;


namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against units in a department
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class UnitsController : V3AuthenticatedApiControllerbase
	{
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUserStateService _userStateService;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public UnitsController(
			IUsersService usersService,
			IActionLogsService actionLogsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IUserStateService userStateService,
			IUnitsService unitsService,
			IDepartmentGroupsService departmentGroupsService
			)
		{
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_userStateService = userStateService;
			_unitsService = unitsService;
			_departmentGroupsService = departmentGroupsService;
		}

		///// <summary>
		///// Get's all the units in a department and their current status information
		///// </summary>
		///// <returns>List of UnitStatusResult objects, with status information for each unit.</returns>
		//[HttpGet("GetUnitStatuses")]
		//[ProducesResponseType(StatusCodes.Status200OK)]
		//public async Task<ActionResult<List<UnitStatusResult>>> GetUnitStatuses()
		//{
		//	var results = new List<UnitStatusResult>();

		//	var units = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);
		//	var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

		//	foreach (var u in units)
		//	{
		//		var unitStatus = new UnitStatusResult();
		//		unitStatus.Uid = u.UnitId;
		//		unitStatus.Typ = u.State;
		//		unitStatus.Tmp = u.Timestamp.TimeConverter(department);

		//		if (u.DestinationId.HasValue)
		//			unitStatus.Did = u.DestinationId.Value;

		//		results.Add(unitStatus);
		//	}

		//	return Ok(results);
		//}

		/// <summary>
		/// Get's all the units in a department and their current status information
		/// </summary>
		/// <returns>List of UnitStatusResult objects, with status information for each unit.</returns>
		[HttpGet("GetUnitStatuses")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<UnitStatusResult>>> GetUnitStatuses(string activeFilter)
		{
			var results = new List<UnitStatusResult>();

			string filter = null;
			string[] activeFilters = null;
			if (!String.IsNullOrWhiteSpace(activeFilter))
			{
				filter = HttpUtility.UrlDecode(activeFilter);
				activeFilters = filter.Split(char.Parse("|"));
			}
			var filters = await GetFilterOptions();

			var units = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var u in units)
			{
				var unitStatus = new UnitStatusResult();
				unitStatus.Uid = u.UnitId;
				unitStatus.Typ = u.State;
				unitStatus.Tmp = u.Timestamp.TimeConverter(department);

				if (u.DestinationId.HasValue)
					unitStatus.Did = u.DestinationId.Value;

				if (activeFilters != null)
				{
					foreach (var afilter in activeFilters)
					{
						var text = GetTextValue(afilter, filters);

						if (afilter.Substring(0, 2) == "G:")
						{
							if (u.Unit != null && u.Unit.StationGroup != null && text == u.Unit.StationGroup.Name)
							{
								results.Add(unitStatus);
								break;
							}
						}
					}
				}
				else
				{
					results.Add(unitStatus);
				}
			}

			return Ok(results);
		}

		/// <summary>
		/// Get's all the units in a department and their basic info
		/// </summary>
		/// <returns>List of UnitResult objects, with basic information for each unit.</returns>
		[HttpGet("GetUnitsForDepartment")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<UnitResult>>> GetUnitsForDepartment(int departmentId)
		{
			var results = new List<UnitResult>();

			if (departmentId != DepartmentId && !IsSystem)
				return Unauthorized();


			if (departmentId == 0 && IsSystem)
			{
				// Get All
				var departments = await _departmentsService.GetAllAsync();

				foreach (var department in departments)
				{
					var units = await _unitsService.GetUnitsForDepartmentAsync(departmentId);

					foreach (var u in units)
					{
						var unitResult = new UnitResult();
						unitResult.Id = u.UnitId;
						unitResult.DepartmentId = u.DepartmentId;
						unitResult.Name = u.Name;
						unitResult.Type = u.Type;
						unitResult.StationId = u.StationGroupId;
						unitResult.VIN = u.VIN;
						unitResult.PlateNumber = u.PlateNumber;
						unitResult.FourWheel = u.FourWheel;
						unitResult.SpecialPermit = u.SpecialPermit;

						results.Add(unitResult);
					}
				}

				return results;
			}
			else
			{
				var units = await _unitsService.GetUnitsForDepartmentAsync(departmentId);

				foreach (var u in units)
				{
					var unitResult = new UnitResult();
					unitResult.Id = u.UnitId;
					unitResult.DepartmentId = u.DepartmentId;
					unitResult.Name = u.Name;
					unitResult.Type = u.Type;
					unitResult.StationId = u.StationGroupId;
					unitResult.VIN = u.VIN;
					unitResult.PlateNumber = u.PlateNumber;
					unitResult.FourWheel = u.FourWheel;
					unitResult.SpecialPermit = u.SpecialPermit;

					results.Add(unitResult);
				}
			}

			return Ok(results);
		}

		[HttpGet("GetUnitDetail")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<UnitDetailResult>>> GetUnitDetail()
		{
			List<UnitDetailResult> result = new List<UnitDetailResult>();
			var units = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);
			var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			foreach (var unit in units)
			{
				var unitResult = new UnitDetailResult();
				unitResult.Roles = new List<UnitDetailRoleResult>();
				unitResult.Id = unit.UnitId;
				unitResult.Name = unit.Unit.Name;
				unitResult.Type = unit.Unit.Type;
				unitResult.GroupId = unit.Unit.StationGroupId.GetValueOrDefault();
				unitResult.VIN = unit.Unit.VIN;
				unitResult.PlateNumber = unit.Unit.PlateNumber;
				unitResult.Offroad = unit.Unit.FourWheel.GetValueOrDefault();
				unitResult.SpecialPermit = unit.Unit.SpecialPermit.GetValueOrDefault();
				unitResult.StatusId = unit.UnitStateId;

				if (unit.Roles != null && unit.Roles.Count() > 0)
				{
					foreach (var role in unit.Roles)
					{
						var roleResult = new UnitDetailRoleResult();
						roleResult.RoleName = role.Role;
						roleResult.RoleId = role.UnitStateRoleId;
						roleResult.UserId = role.UserId;

						var name = names.FirstOrDefault(x => x.UserId == role.UserId);

						if (name != null)
							roleResult.Name = name.Name;
						else
							roleResult.Name = "Unknown";

						unitResult.Roles.Add(roleResult);
					}
				}

				result.Add(unitResult);
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

			Parallel.ForEach(stations, s =>
			{
				var respondingTo = new FilterResult();
				respondingTo.Id = string.Format("G:{0}", s.DepartmentGroupId);
				respondingTo.Type = "Group";
				respondingTo.Name = s.Name;

				result.Add(respondingTo);
			});

			return result;
		}
	}
}
