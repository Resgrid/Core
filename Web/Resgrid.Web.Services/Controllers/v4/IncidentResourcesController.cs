using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Filters;
using Resgrid.Web.Services.Helpers;
using System.Threading;
using System.Threading.Tasks;
using ICModels = Resgrid.Web.Services.Models.v4.IncidentCommand;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Incident-scoped ad-hoc resources: create units/personnel on the fly for non-Resgrid resources, build
	/// rosters, and form a unit from on-scene personnel (§3.10).
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class IncidentResourcesController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IIncidentResourcesService _incidentResourcesService;

		public IncidentResourcesController(IIncidentResourcesService incidentResourcesService)
		{
			_incidentResourcesService = incidentResourcesService;
		}
		#endregion Members and Constructors

		#region Ad-hoc units

		/// <summary>Creates an ad-hoc unit for a non-Resgrid resource.</summary>
		[HttpPost("CreateAdHocUnit")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageResources)]
		public async Task<ActionResult<ICModels.AdHocUnitResult>> CreateAdHocUnit([FromBody] IncidentAdHocUnit unit)
		{
			if (unit == null || unit.CallId <= 0)
				return BadRequest();

			unit.DepartmentId = DepartmentId;

			var result = new ICModels.AdHocUnitResult();
			var saved = await _incidentResourcesService.CreateAdHocUnitAsync(unit, UserId, CancellationToken.None);

			if (saved == null)
			{
				// No active command owned by the caller's department for this call.
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = saved;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Gets the active ad-hoc units for a call.</summary>
		[HttpGet("GetAdHocUnits/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.AdHocUnitsResult>> GetAdHocUnits(int callId)
		{
			var result = new ICModels.AdHocUnitsResult();
			result.Data = await _incidentResourcesService.GetAdHocUnitsForCallAsync(DepartmentId, callId);
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Releases an ad-hoc unit.</summary>
		[HttpPost("ReleaseAdHocUnit/{incidentAdHocUnitId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentCommandActionResult>> ReleaseAdHocUnit(string incidentAdHocUnitId)
		{
			var result = new ICModels.IncidentCommandActionResult();
			result.Data = await _incidentResourcesService.ReleaseAdHocUnitAsync(DepartmentId, incidentAdHocUnitId, UserId, CancellationToken.None);
			result.Status = result.Data ? ResponseHelper.Success : ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Ad-hoc units

		#region Ad-hoc personnel

		/// <summary>Creates an ad-hoc person for a non-Resgrid resource.</summary>
		[HttpPost("CreateAdHocPersonnel")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageResources)]
		public async Task<ActionResult<ICModels.AdHocPersonnelResult>> CreateAdHocPersonnel([FromBody] IncidentAdHocPersonnel personnel)
		{
			if (personnel == null || personnel.CallId <= 0)
				return BadRequest();

			personnel.DepartmentId = DepartmentId;

			var result = new ICModels.AdHocPersonnelResult();
			var saved = await _incidentResourcesService.CreateAdHocPersonnelAsync(personnel, UserId, CancellationToken.None);

			if (saved == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = saved;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Gets the active ad-hoc personnel for a call.</summary>
		[HttpGet("GetAdHocPersonnel/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.AdHocPersonnelListResult>> GetAdHocPersonnel(int callId)
		{
			var result = new ICModels.AdHocPersonnelListResult();
			result.Data = await _incidentResourcesService.GetAdHocPersonnelForCallAsync(DepartmentId, callId);
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Releases an ad-hoc person.</summary>
		[HttpPost("ReleaseAdHocPersonnel/{incidentAdHocPersonnelId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentCommandActionResult>> ReleaseAdHocPersonnel(string incidentAdHocPersonnelId)
		{
			var result = new ICModels.IncidentCommandActionResult();
			result.Data = await _incidentResourcesService.ReleaseAdHocPersonnelAsync(DepartmentId, incidentAdHocPersonnelId, UserId, CancellationToken.None);
			result.Status = result.Data ? ResponseHelper.Success : ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Ad-hoc personnel

		#region Roster building

		/// <summary>Adds an ad-hoc person to a unit roster for accountability.</summary>
		[HttpPost("AssignPersonnelToUnit")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.AdHocPersonnelResult>> AssignPersonnelToUnit([FromBody] ICModels.AssignPersonnelToUnitInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentAdHocPersonnelId))
				return BadRequest();

			var result = new ICModels.AdHocPersonnelResult();
			var personnel = await _incidentResourcesService.AssignPersonnelToUnitAsync(DepartmentId, input.IncidentAdHocPersonnelId, input.RidingResourceKind, input.RidingResourceId, UserId, CancellationToken.None);

			if (personnel == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = personnel;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Forms a new ad-hoc unit from on-scene ad-hoc personnel.</summary>
		[HttpPost("FormUnit")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageResources)]
		public async Task<ActionResult<ICModels.AdHocUnitResult>> FormUnit([FromBody] ICModels.FormUnitInput input)
		{
			if (input == null || input.CallId <= 0 || string.IsNullOrWhiteSpace(input.Name))
				return BadRequest();

			var unit = new IncidentAdHocUnit
			{
				DepartmentId = DepartmentId,
				CallId = input.CallId,
				Name = input.Name,
				Type = input.Type,
				UnitTypeId = input.UnitTypeId,
				ExternalAgencyName = input.ExternalAgencyName
			};

			var result = new ICModels.AdHocUnitResult();
			var saved = await _incidentResourcesService.FormUnitFromPersonnelAsync(unit, input.AdHocPersonnelIds, UserId, CancellationToken.None);

			if (saved == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = saved;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Roster building
	}
}
