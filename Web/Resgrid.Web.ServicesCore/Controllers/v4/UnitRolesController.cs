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
using Resgrid.Web.Services.Models.v4.UnitRoles;
using Resgrid.Model;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Unit roles
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class UnitRolesController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IUnitsService _unitsService;

		public UnitRolesController(IUnitsService unitsService)
		{
			_unitsService = unitsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets the accountability roles for a unit
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetRolesForUnit")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<UnitRolesResult>> GetRolesForUnit(string unitId)
		{
			var result = new UnitRolesResult();

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

			var roles = await _unitsService.GetRolesForUnitAsync(unit.UnitId);

			if (roles != null && roles.Any())
			{

				foreach (var role in roles)
				{
					result.Data.Add(ConvertUnitRoleData(role));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}	

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets all the roles for every unit in a department plus who is currently assigned to that unit role (accountability)
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllUnitRolesAndAssignmentsForDepartment")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<ActiveUnitRolesResult>> GetAllUnitRolesAndAssignmentsForDepartment()
		{
			var result = new ActiveUnitRolesResult();

			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var activeRoles = await _unitsService.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId);

			if (units != null && units.Any())
			{
				foreach (var unit in units)
				{
					if (unit.Roles != null && unit.Roles.Any())
					{
						foreach (var unitRole in unit.Roles)
						{
							var activeRole = activeRoles.FirstOrDefault(x => x.UnitId == unitRole.UnitId && x.Role == unitRole.Name);
							var role = new ActiveUnitRoleResultData(ConvertUnitRoleData(unitRole));

							if (activeRole != null)
							{
								role.UserId = activeRole.UserId;
								role.UpdatedOn = activeRole.UpdatedOn.ToString();
								role.FullName = await UserHelper.GetFullNameForUser(activeRole.UserId); //TODO: Perf issue here most likely, temp add for Unit app Cap conversion. -SJ
							}

							result.Data.Add(role);
						}
					}
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		public static UnitRoleResultData ConvertUnitRoleData(UnitRole role)
		{
			var data = new UnitRoleResultData();
			data.Name = role.Name;
			data.UnitId = role.UnitId.ToString();
			data.UnitRoleId = role.UnitRoleId.ToString();

			return data;
		}
	}
}
