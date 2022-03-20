using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Statuses;
using Resgrid.Model;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// The options for Personnel Statuses, Staffing and Unit Statuses that can be used to submit their status to Resgrid.
	/// Do not use Deleted versions for submittion, they should only be used for display of previous used values.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class StatusesController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly ICustomStateService _customStateService;
		private readonly IUnitsService _unitsService;

		public StatusesController(ICustomStateService customStateService, IUnitsService unitsService)
		{
			_customStateService = customStateService;
			_unitsService = unitsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all available statuses for Personnel for the department
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllStatusesForPersonnel")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<ActionResult<StatusesResult>> GetAllStatusesForPersonnel()
		{
			var result = new StatusesResult();

			var statuses = await _customStateService.GetCustomPersonnelStatusesOrDefaultsAsync(DepartmentId);

			if (statuses != null && statuses.Any())
			{
				foreach (var customState in statuses)
				{
					if (customState.IsDeleted)
						continue;

					result.Data.Add(ConvertCustomStatusData((int)CustomStateTypes.Personnel, customState));
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

			return result;
		}

		/// <summary>
		/// Gets all available staffing levels for Personnel for the department
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllStaffingsForPersonnel")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<ActionResult<StatusesResult>> GetAllStaffingsForPersonnel()
		{
			var result = new StatusesResult();

			var statuses = await _customStateService.GetCustomPersonnelStaffingsOrDefaultsAsync(DepartmentId);

			if (statuses != null && statuses.Any())
			{
				foreach (var customState in statuses)
				{
					if (customState.IsDeleted)
						continue;

					result.Data.Add(ConvertCustomStatusData((int)CustomStateTypes.Staffing, customState));
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

			return result;
		}

		/// <summary>
		/// Gets all active unit statuses for each unit type
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllUnitStatuses")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<UnitStatusesResult>> GetAllUnitStatuses()
		{
			var result = new UnitStatusesResult();

			var types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);
			var statuses = await _customStateService.GetAllCustomStatesForDepartmentAsync(DepartmentId);
			var defaultUnitStats = _customStateService.GetDefaultUnitStatuses();

			var defaultUnitStatuses = new UnitTypeStatusResultData();
			defaultUnitStatuses.UnitType = "0";

			foreach (var state in defaultUnitStats)
			{
				defaultUnitStatuses.Statuses.Add(ConvertCustomStatusData((int)CustomStateTypes.Unit, state));
			}
			result.Data.Add(defaultUnitStatuses);

			if (types != null && types.Any())
			{
				foreach (var type in types)
				{
					if (type.CustomStatesId.HasValue && type.CustomStatesId.Value > 0)
					{
						var customStatuses = statuses.FirstOrDefault(x => x.CustomStateId == type.CustomStatesId.Value);

						if (customStatuses != null && customStatuses.IsDeleted == false)
						{
							var unitStatusResult = new UnitTypeStatusResultData();
							unitStatusResult.UnitType = type.Type;
							unitStatusResult.StatusId = customStatuses.CustomStateId.ToString();

							foreach (var state in customStatuses.GetActiveDetails())
							{
								if (state.IsDeleted)
									continue;

								unitStatusResult.Statuses.Add(ConvertCustomStatusData((int)CustomStateTypes.Unit, state));
							}

							result.Data.Add(unitStatusResult);
						}
					}
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 1;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		public static StatusResultData ConvertCustomStatusData(int type, CustomStateDetail stateDetail)
		{
			var customStateResult = new StatusResultData();
			customStateResult.Id = stateDetail.CustomStateDetailId;
			customStateResult.Type = type;
			customStateResult.StateId = stateDetail.CustomStateId;
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
