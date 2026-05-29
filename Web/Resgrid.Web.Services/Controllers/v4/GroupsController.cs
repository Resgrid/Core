using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Groups;
using System;
using Resgrid.Model;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// User generated forms that are dispayed to get custom information for New Calls, Unit Checks, etc
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class GroupsController : V4AuthenticatedApiControllerbaseSystemAuth
	{
		#region Members and Constructors
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public GroupsController(IDepartmentGroupsService departmentGroupsService)
		{
			_departmentGroupsService = departmentGroupsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets the Department Group by it's id
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetGroup")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Group_View)]
		public async Task<ActionResult<GroupResult>> GetGroup(string groupId)
		{
			var result = new GroupResult();

			if (String.IsNullOrWhiteSpace(groupId))
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);

				return result;
			}

			var group = await _departmentGroupsService.GetGroupByIdAsync(int.Parse(groupId));

			if (group == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);

				return result;
			}

			if (group.DepartmentId != DepartmentId)
				return Unauthorized();

			result.Data = ConvertGroupData(group);

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets all deparment groups for a department
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllGroups")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Group_View)]
		public async Task<ActionResult<GroupResults>> GetAllGroups()
		{
			var result = new GroupResults();

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			if (groups == null || groups.Count <= 0)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);

				return result;
			}

			foreach (var group in groups)
			{
				result.Data.Add(ConvertGroupData(group));
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Resolves a group dispatch email code to the corresponding group.
		/// Used by the SMTP relay to resolve random dispatch codes (e.g. "XK7M2N") to numeric group IDs for DispatchList.
		/// In SystemApiKey (hosted multi-department) mode, departmentId is optional — the code alone resolves to the owning group.
		/// In OAuth mode, the group's department is validated against the token's department.
		/// </summary>
		/// <param name="code">The dispatch email code (local part of the email address, e.g. a 6-char random string).</param>
		/// <param name="departmentId">Optional department scope. In SystemApiKey mode: if provided, validates the group belongs to this department. If omitted, returns any group matching the code. In OAuth mode: ignored, always validates against the token.</param>
		/// <returns>GroupResult with the matching group (including its DepartmentId), or a not-found response.</returns>
		[HttpGet("GetGroupByDispatchCode")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Group_View)]
		public async Task<ActionResult<GroupResult>> GetGroupByDispatchCode(string code, [FromQuery] string departmentId = null)
		{
			var result = new GroupResult();

			if (String.IsNullOrWhiteSpace(code))
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			var group = await _departmentGroupsService.GetGroupByDispatchEmailCodeAsync(code);

			if (group == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			// In SystemApiKey mode: if departmentId param is provided, validate the group belongs to it.
			// If no departmentId is provided (code-only lookup), allow cross-department resolution.
			// In OAuth mode: always validate against the token's department.
			if (!IsSystemApiKeyRequest)
			{
				if (group.DepartmentId != DepartmentId)
					return Unauthorized();
			}
			else if (!string.IsNullOrWhiteSpace(departmentId))
			{
				// DepartmentId was provided explicitly — must be valid and match
				if (!int.TryParse(departmentId, out var deptId) || deptId <= 0)
				{
					ResponseHelper.PopulateV4ResponseNotFound(result);
					return result;
				}
				if (group.DepartmentId != deptId)
				{
					ResponseHelper.PopulateV4ResponseNotFound(result);
					return result;
				}
			}

			result.Data = ConvertGroupData(group);
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Resolves a group message email code to the corresponding group.
		/// Used by the SMTP relay to resolve random message codes (e.g. "XK7M2N") to numeric group IDs for Message creation.
		/// In SystemApiKey (hosted multi-department) mode, departmentId is optional — the code alone resolves to the owning group.
		/// In OAuth mode, the group's department is validated against the token's department.
		/// </summary>
		/// <param name="code">The message email code (local part of the email address, e.g. a 6-char random string).</param>
		/// <param name="departmentId">Optional department scope. In SystemApiKey mode: if provided, validates the group belongs to this department. If omitted, returns any group matching the code. In OAuth mode: ignored, always validates against the token.</param>
		/// <returns>GroupResult with the matching group (including its DepartmentId), or a not-found response.</returns>
		[HttpGet("GetGroupByMessageCode")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Group_View)]
		public async Task<ActionResult<GroupResult>> GetGroupByMessageCode(string code, [FromQuery] string departmentId = null)
		{
			var result = new GroupResult();

			if (String.IsNullOrWhiteSpace(code))
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			var group = await _departmentGroupsService.GetGroupByMessageEmailCodeAsync(code);

			if (group == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			// In SystemApiKey mode: if departmentId param is provided, validate the group belongs to it.
			// If no departmentId is provided (code-only lookup), allow cross-department resolution.
			// In OAuth mode: always validate against the token's department.
			if (!IsSystemApiKeyRequest)
			{
				if (group.DepartmentId != DepartmentId)
					return Unauthorized();
			}
			else if (!string.IsNullOrWhiteSpace(departmentId))
			{
				// DepartmentId was provided explicitly — must be valid and match
				if (!int.TryParse(departmentId, out var deptId) || deptId <= 0)
				{
					ResponseHelper.PopulateV4ResponseNotFound(result);
					return result;
				}
				if (group.DepartmentId != deptId)
				{
					ResponseHelper.PopulateV4ResponseNotFound(result);
					return result;
				}
			}

			result.Data = ConvertGroupData(group);
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		public static GroupResultData ConvertGroupData(DepartmentGroup group)
		{
			var result = new GroupResultData();

			result.GroupId = group.DepartmentGroupId.ToString();

			if (group.Type.HasValue)
				result.TypeId = group.Type.Value.ToString();
			else
				result.TypeId = "0";

			result.Name = group.Name;

			if (group.Address != null)
				result.Address = group.Address.FormatAddress();

			return result;
		}
	}
}
