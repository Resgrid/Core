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
using System.Net.Mime;
using System.Threading;
using System;

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
		private readonly IDepartmentsService _departmentsService;
		private readonly IPersonnelRolesService _personnelRolesService;

		public UnitRolesController(IUnitsService unitsService, IDepartmentsService departmentsService, IPersonnelRolesService personnelRolesService)
		{
			_unitsService = unitsService;
			_departmentsService = departmentsService;
			_personnelRolesService = personnelRolesService;
		}
		#endregion Members and Constructors

		private async Task<Dictionary<int, string>> GetPersonnelRoleNamesAsync()
		{
			var map = new Dictionary<int, string>();
			var roles = await _personnelRolesService.GetAllRolesForDepartmentAsync(DepartmentId);

			if (roles != null)
			{
				foreach (var role in roles)
				{
					if (role != null && !map.ContainsKey(role.PersonnelRoleId))
						map[role.PersonnelRoleId] = role.Name;
				}
			}

			return map;
		}

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
			int uId = 0;

			if (string.IsNullOrWhiteSpace(unitId) || !int.TryParse(unitId, out uId))
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);

				return Ok(result);
			}

			var unit = await _unitsService.GetUnitByIdAsync(uId);

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
				var personnelRoleNames = await GetPersonnelRoleNamesAsync();

				foreach (var role in roles)
				{
					result.Data.Add(ConvertUnitRoleData(role, personnelRoleNames));
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
		/// Gets the accountability roles and the current assignments for a unit
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetRoleAssignmentsForUnit")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<ActiveUnitRolesResult>> GetRoleAssignmentsForUnit(string unitId)
		{
			var result = new ActiveUnitRolesResult();

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

			var allActiveRoles = await _unitsService.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId);
			var roles = await _unitsService.GetRolesForUnitAsync(unit.UnitId);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			if (roles != null && roles.Any())
			{
				var personnelRoleNames = await GetPersonnelRoleNamesAsync();

				foreach (var role in roles)
				{
					var activeRole = allActiveRoles.FirstOrDefault(x => x.UnitId == role.UnitId && x.Role == role.Name);
					bool isQualified = activeRole == null || await _unitsService.IsUserQualifiedForUnitRoleAsync(role, activeRole.UserId, DepartmentId);
					result.Data.Add(ConvertActiveUnitRoleData(role, activeRole, personnelNames, personnelRoleNames, isQualified));
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
		/// Sets the accountability roles and the current assignments for a unit
		/// </summary>
		/// <returns></returns>
		[HttpPost("SetRoleAssignmentsForUnit")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<ActiveUnitRolesResult>> SetRoleAssignmentsForUnit(SetUnitRolesInput setRolesInput, CancellationToken cancellationToken)
		{
			var result = new ActiveUnitRolesResult();

			if (setRolesInput == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);

				return Ok(result);
			}

			var unit = await _unitsService.GetUnitByIdAsync(int.Parse(setRolesInput.UnitId));

			if (unit == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);

				return Ok(result);
			}

			if (unit.DepartmentId != DepartmentId)
				return Unauthorized();

			if (setRolesInput.Roles != null && setRolesInput.Roles.Any())
			{
				// Enforce "Required" personnel-role qualifications before mutating anything (the save path
				// deletes existing assignments first). A seat marked Required blocks an unqualified member;
				// "Preferred" seats are allowed and reported as degraded.
				foreach (var roleInput in setRolesInput.Roles)
				{
					if (string.IsNullOrWhiteSpace(roleInput.UserId) || string.IsNullOrWhiteSpace(roleInput.RoleId)
						|| !int.TryParse(roleInput.RoleId, out var roleIdToCheck))
						continue;

					var roleToCheck = await _unitsService.GetRoleByIdAsync(roleIdToCheck);

					if (roleToCheck != null && roleToCheck.PersonnelRoleId.HasValue && roleToCheck.PersonnelRoleRequired
						&& !await _unitsService.IsUserQualifiedForUnitRoleAsync(roleToCheck, roleInput.UserId, DepartmentId))
					{
						result.Status = ResponseHelper.Failure;
						ResponseHelper.PopulateV4ResponseData(result);

						return BadRequest(result);
					}
				}

				await _unitsService.DeleteActiveRolesForUnitAsync(unit.UnitId, cancellationToken);

				foreach (var unitRole in setRolesInput.Roles)
				{
					if (!string.IsNullOrWhiteSpace(unitRole.UserId) && !string.IsNullOrWhiteSpace(unitRole.RoleId))
					{
						var role = await _unitsService.GetRoleByIdAsync(int.Parse(unitRole.RoleId));

						if (role != null)
						{
							UnitActiveRole activeRole = new UnitActiveRole();
							activeRole.UnitId = role.UnitId;
							activeRole.Role = role.Name;
							activeRole.UserId = unitRole.UserId;
							activeRole.DepartmentId = DepartmentId;
							activeRole.UpdatedBy = UserId;
							activeRole.UpdatedOn = DateTime.UtcNow;

							await _unitsService.SaveActiveRoleAsync(activeRole, cancellationToken);
						}
					}
				}
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
				var personnelRoleNames = await GetPersonnelRoleNamesAsync();

				foreach (var unit in units)
				{
					if (unit.Roles != null && unit.Roles.Any())
					{
						foreach (var unitRole in unit.Roles)
						{
							var activeRole = activeRoles.FirstOrDefault(x => x.UnitId == unitRole.UnitId && x.Role == unitRole.Name);
							var role = new ActiveUnitRoleResultData(ConvertUnitRoleData(unitRole, personnelRoleNames));

							if (activeRole != null)
							{
								role.UserId = activeRole.UserId;
								role.UpdatedOn = activeRole.UpdatedOn.ToString();
								role.FullName = await UserHelper.GetFullNameForUser(activeRole.UserId); //TODO: Perf issue here most likely, temp add for Unit app Cap conversion. -SJ
								role.IsQualified = await _unitsService.IsUserQualifiedForUnitRoleAsync(unitRole, activeRole.UserId, DepartmentId);
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

		public static UnitRoleResultData ConvertUnitRoleData(UnitRole role, IReadOnlyDictionary<int, string> personnelRoleNames = null)
		{
			var data = new UnitRoleResultData();
			data.Name = role.Name;
			data.UnitId = role.UnitId.ToString();
			data.UnitRoleId = role.UnitRoleId.ToString();

			if (role.PersonnelRoleId.HasValue)
			{
				data.PersonnelRoleId = role.PersonnelRoleId.Value.ToString();
				data.PersonnelRoleRequired = role.PersonnelRoleRequired;

				if (personnelRoleNames != null && personnelRoleNames.TryGetValue(role.PersonnelRoleId.Value, out var name))
					data.PersonnelRoleName = name;
			}

			return data;
		}

		public static ActiveUnitRoleResultData ConvertActiveUnitRoleData(UnitRole role, UnitActiveRole activeRole, List<PersonName> names,
			IReadOnlyDictionary<int, string> personnelRoleNames = null, bool isQualified = true)
		{
			var data = new ActiveUnitRoleResultData(ConvertUnitRoleData(role, personnelRoleNames));
			data.IsQualified = isQualified;

			if (activeRole != null)
			{
				data.UpdatedOn = activeRole.UpdatedOn.ToString();
				data.UserId = activeRole.UserId;

				var name = names.FirstOrDefault(x => x.UserId == activeRole.UserId);

				if (name != null)
				{
					data.FullName = name.Name;
				}
			}

			return data;
		}
	}
}
