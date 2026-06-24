using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using System.Threading.Tasks;
using ICModels = Resgrid.Web.Services.Models.v4.IncidentCommand;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Mutual-aid resource aggregation for incident command: own-department + linked-department units/personnel,
	/// color-coded, for the IC resource picker (§3.9).
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class MutualAidController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IMutualAidService _mutualAidService;

		public MutualAidController(IMutualAidService mutualAidService)
		{
			_mutualAidService = mutualAidService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all resources the commander can assign to lanes: own-department units/personnel plus those
		/// shared toward this department by accepted mutual-aid links, color-coded by link.
		/// </summary>
		[HttpGet("GetAssignableResources")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.MutualAidResourcesResult>> GetAssignableResources()
		{
			var result = new ICModels.MutualAidResourcesResult();
			result.Data = await _mutualAidService.GetAssignableResourcesForIncidentAsync(DepartmentId);
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}
	}
}
