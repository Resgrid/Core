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
	public class GroupsController : V4AuthenticatedApiControllerbase
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
