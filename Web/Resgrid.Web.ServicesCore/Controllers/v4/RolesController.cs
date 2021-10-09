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

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// User generated forms that are dispayed to get custom information for New Calls, Unit Checks, etc
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class RolesController : V4AuthenticatedApiControllerbase
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

		public static RoleResultData ConvertRoleData(PersonnelRole role)
		{
			var result = new RoleResultData();

			result.RoleId = role.PersonnelRoleId.ToString();
			result.Name = role.Name;

			return result;
		}
	}
}
