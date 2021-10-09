using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.CustomStatuses;
using Resgrid.Model;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Custom statuses
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CustomStatusesController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly ICustomStateService _customStateService;

		public CustomStatusesController(ICustomStateService customStateService)
		{
			_customStateService = customStateService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// All custom statuses for a department
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllCustomStatuses")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<CustomStatusesResult>> GetAllCustomStatuses()
		{
			var result = new CustomStatusesResult();
			var customStates = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(DepartmentId);

			if (customStates != null && customStates.Any())
			{
				foreach (var customState in customStates)
				{
					if (customState.IsDeleted)
						continue;

					foreach (var stateDetail in customState.GetActiveDetails())
					{
						if (stateDetail.IsDeleted)
							continue;

						result.Data.Add(ConvertCustomStatusData(customState, stateDetail));
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

		public static CustomStatusResultData ConvertCustomStatusData(CustomState customState, CustomStateDetail stateDetail)
		{
			var customStateResult = new CustomStatusResultData();
			customStateResult.Id = stateDetail.CustomStateDetailId.ToString();
			customStateResult.Type = customState.Type;
			customStateResult.StateId = stateDetail.CustomStateId.ToString();
			customStateResult.Text = stateDetail.ButtonText;
			customStateResult.BColor = stateDetail.ButtonColor;
			customStateResult.Color = stateDetail.TextColor;
			customStateResult.Gps = stateDetail.GpsRequired;
			customStateResult.Note = stateDetail.NoteType;
			customStateResult.Detail = stateDetail.DetailType;

			return customStateResult;
		}
	}
}
