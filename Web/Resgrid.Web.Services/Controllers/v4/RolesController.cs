using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Model;
using Resgrid.Web.Services.Models.v4.Roles;
using System.Linq;
using System;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// User generated forms that are dispayed to get custom information for New Calls, Unit Checks, etc
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class RolesController : V4AuthenticatedApiControllerbaseSystemAuth
	{
		#region Members and Constructors
		private readonly IPersonnelRolesService _personnelRolesService;

		public RolesController(IPersonnelRolesService personnelRolesService)
		{
			_personnelRolesService = personnelRolesService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets the Department Form that can be used for the new call process (i.e. call intake/triage form)
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetGroup")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Role_View)]
		public async Task<ActionResult<RolesResult>> GetAllRoles()
		{
			var result = new RolesResult();
			var allRoles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);

			if (allRoles != null && allRoles.Any())
			{
				foreach (var role in allRoles)
				{
					result.Data.Add(ConvertRoleData(role));
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
		/// Resolves a role name to the corresponding role.
		/// Used by the SMTP relay to resolve role dispatch codes like "commander" to numeric role IDs for DispatchList.
		/// </summary>
		/// <param name="name">The role name to look up.</param>
		/// <param name="departmentId">Optional department override for SystemApiKey (hosted multi-department) mode.</param>
		/// <returns>RoleResult with the matching role, or a not-found response.</returns>
		[HttpGet("GetRoleByName")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Role_View)]
		public async Task<ActionResult<RoleResult>> GetRoleByName(string name, [FromQuery] string departmentId = null)
		{
			var result = new RoleResult();

			if (String.IsNullOrWhiteSpace(name))
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			var effectiveDepartmentId = GetEffectiveDepartmentId(departmentId);

			if (effectiveDepartmentId <= 0)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			var role = await _personnelRolesService.GetRoleByDepartmentAndNameAsync(effectiveDepartmentId, name);

			if (role == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			result.Data = ConvertRoleData(role);
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		public static RoleResultData ConvertRoleData(PersonnelRole role)
		{
			var result = new RoleResultData();

			result.RoleId = role.PersonnelRoleId.ToString();
			result.Name = role.Name;

			return result;
		}
	}
}
