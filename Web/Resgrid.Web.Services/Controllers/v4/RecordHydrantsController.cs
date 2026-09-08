using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.Services.Models.v4;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>RMS-5 hydrants and water sources (RMS plan section 4.3): flow tests, maintenance, service state, CSV import and the response-map layer.</summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class RecordHydrantsController : RecordsPreventionApiControllerBase
	{
		private readonly IRecordsHydrantsService _hydrants;

		public RecordHydrantsController(IRecordsHydrantsService hydrants, IRecordsCutoverService cutover) : base(cutover)
		{
			_hydrants = hydrants;
		}

		[HttpGet("List")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<HydrantsResult>> List()
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new HydrantsResult { Data = (await _hydrants.ListAsync(DepartmentId, UserId)).Select(RecordsRms5ApiMapper.ToHydrant).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Get")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<HydrantResult>> Get(string id)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var a = await _hydrants.GetAsync(DepartmentId, UserId, id); if (a == null) return NotFound(); return Ok(Done(new HydrantResult { Data = RecordsRms5ApiMapper.ToHydrantAggregate(a), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Save")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<HydrantSavedResult>> Save([FromBody] HydrantInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try
			{
				var hydrant = await _hydrants.SaveAsync(DepartmentId, UserId, new RmsHydrant { RmsHydrantId = input.HydrantId, HydrantNumber = input.HydrantNumber, Type = input.Type, Latitude = input.Latitude, Longitude = input.Longitude, AddressText = input.AddressText, OwnerKind = input.OwnerKind, OwnerName = input.OwnerName, MainSizeInches = input.MainSizeInches, FlowGpm = input.FlowGpm, StaticPressurePsi = input.StaticPressurePsi, ResidualPressurePsi = input.ResidualPressurePsi, Notes = input.Notes, PoiId = input.PoiId }, cancellationToken);
				return Ok(Done(new HydrantSavedResult { Data = RecordsRms5ApiMapper.ToHydrant(hydrant), PageSize = 1 }));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpDelete("Delete")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<StandardApiResponseV4Base>> Delete(string id, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { await _hydrants.DeleteAsync(DepartmentId, UserId, id, cancellationToken); return Ok(Done(new StandardApiResponseV4Base())); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("SetServiceState")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<HydrantSavedResult>> SetServiceState([FromBody] HydrantServiceStateInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new HydrantSavedResult { Data = RecordsRms5ApiMapper.ToHydrant(await _hydrants.SetServiceStateAsync(DepartmentId, UserId, input.HydrantId, input.InService, input.Reason, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("RecordFlowTest")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<HydrantFlowTestResult>> RecordFlowTest([FromBody] HydrantFlowTestInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try
			{
				var test = await _hydrants.RecordFlowTestAsync(DepartmentId, UserId, new RmsHydrantFlowTest { RmsHydrantId = input.HydrantId, TestedOn = input.TestedOn ?? default, StaticPressurePsi = input.StaticPressurePsi, ResidualPressurePsi = input.ResidualPressurePsi, PitotPressurePsi = input.PitotPressurePsi, OutletDiameterInches = input.OutletDiameterInches, Coefficient = input.Coefficient, Notes = input.Notes }, cancellationToken);
				return Ok(Done(new HydrantFlowTestResult { Data = RecordsRms5ApiMapper.ToFlowTest(test), PageSize = 1 }));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("RecordMaintenance")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<HydrantMaintenanceResult>> RecordMaintenance([FromBody] HydrantMaintenanceInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try
			{
				var row = await _hydrants.RecordMaintenanceAsync(DepartmentId, UserId, new RmsHydrantMaintenance { RmsHydrantId = input.HydrantId, PerformedOn = input.PerformedOn ?? default, Kind = input.Kind, Notes = input.Notes, ReturnedToService = input.ReturnedToService }, cancellationToken);
				return Ok(Done(new HydrantMaintenanceResult { Data = RecordsRms5ApiMapper.ToMaintenance(row), PageSize = 1 }));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>CSV import (number, latitude, longitude, type, address, main_size, flow_gpm, owner).</summary>
		[HttpPost("Import")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<HydrantImportResultData>> Import([FromBody] HydrantImportInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new HydrantImportResultData { Data = await _hydrants.ImportCsvAsync(DepartmentId, UserId, input?.Csv, cancellationToken), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Map layer for the response map and the apps; bounded to a viewport when bounds are given.</summary>
		[HttpGet("MapLayer")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<HydrantMapLayerResult>> MapLayer(decimal? minLat = null, decimal? maxLat = null, decimal? minLon = null, decimal? maxLon = null)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new HydrantMapLayerResult { Data = await _hydrants.GetMapLayerAsync(DepartmentId, UserId, minLat, maxLat, minLon, maxLon) }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Nearest")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<HydrantsResult>> Nearest(decimal latitude, decimal longitude, int take = 5, double maxMeters = 1600)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				if (!await _hydrants.IsModuleEnabledAsync(DepartmentId)) return NotFound();
				var r = new HydrantsResult { Data = (await _hydrants.GetNearestAsync(DepartmentId, latitude, longitude, take, maxMeters)).Select(RecordsRms5ApiMapper.ToHydrant).ToList() }; r.PageSize = r.Data.Count;
				return Ok(Done(r));
			}
			catch (Exception ex) { return Fail(ex); }
		}
	}
}
