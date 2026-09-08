using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.Services.Models.v4;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// RMS-5 occupancy/property master (RMS plan section 4.3): the structure the department pre-plans, inspects and
	/// permits, the ContactPreplan/Contact/POI crosswalk, the structure-write ownership switch and
	/// OccupancyDispatchProjectionV1. Reads need Record_View; changes need Record_PreventionAdmin.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class RecordOccupanciesController : RecordsPreventionApiControllerBase
	{
		private readonly IRecordsOccupancyService _occupancies;

		public RecordOccupanciesController(IRecordsOccupancyService occupancies, IRecordsCutoverService cutover) : base(cutover)
		{
			_occupancies = occupancies;
		}

		[HttpGet("List")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<OccupanciesResult>> List(string search = null, int? status = null, int? occupancyType = null, bool? reviewOverdue = null, bool? hazmat = null, int skip = 0, int take = 50)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var query = new RmsOccupancyQuery { Search = search, Status = status, OccupancyType = occupancyType, ReviewOverdue = reviewOverdue, HazmatOnSite = hazmat, Skip = skip, Take = take };
				var rows = await _occupancies.ListAsync(DepartmentId, UserId, query);
				var result = new OccupanciesResult { Data = rows.Select(RecordsRms5ApiMapper.ToOccupancy).ToList(), TotalCount = await _occupancies.CountAsync(DepartmentId, UserId, query) };
				result.PageSize = result.Data.Count;
				return Ok(Done(result));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Get")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<OccupancyResult>> Get(string id)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var aggregate = await _occupancies.GetAsync(DepartmentId, UserId, id);
				if (aggregate == null) return NotFound();
				return Ok(Done(new OccupancyResult { Data = RecordsRms5ApiMapper.ToOccupancyAggregate(aggregate), PageSize = 1 }));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Save")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<OccupancySavedResult>> Save([FromBody] OccupancyInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try
			{
				var saved = await _occupancies.SaveAsync(DepartmentId, UserId, RecordsRms5ApiMapper.FromOccupancy(input), cancellationToken);
				return Ok(Done(new OccupancySavedResult { Data = RecordsRms5ApiMapper.ToOccupancy(saved), PageSize = 1 }));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpDelete("Delete")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<StandardApiResponseV4Base>> Delete(string id, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { await _occupancies.DeleteAsync(DepartmentId, UserId, id, cancellationToken); return Ok(Done(new StandardApiResponseV4Base())); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("MarkReviewed")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<OccupancySavedResult>> MarkReviewed(string id, int nextReviewMonths = 12, CancellationToken cancellationToken = default)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new OccupancySavedResult { Data = RecordsRms5ApiMapper.ToOccupancy(await _occupancies.MarkReviewedAsync(DepartmentId, UserId, id, nextReviewMonths, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("SaveHazard")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<OccupancyHazardResult>> SaveHazard([FromBody] OccupancyHazardInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try
			{
				var hazard = await _occupancies.SaveHazardAsync(DepartmentId, UserId, new RmsOccupancyHazard { RmsOccupancyHazardId = input.HazardId, RmsOccupancyId = input.OccupancyId, HazardType = input.HazardType, Severity = input.Severity, Title = input.Title, Description = input.Description, LocationDescription = input.LocationDescription, GpsCoordinates = input.GpsCoordinates, ShouldAlert = input.ShouldAlert }, cancellationToken);
				return Ok(Done(new OccupancyHazardResult { Data = RecordsRms5ApiMapper.ToHazard(hazard), PageSize = 1 }));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpDelete("DeleteHazard")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<StandardApiResponseV4Base>> DeleteHazard(string id, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { await _occupancies.DeleteHazardAsync(DepartmentId, UserId, id, cancellationToken); return Ok(Done(new StandardApiResponseV4Base())); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("LinkContact")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<OccupancyContactLinkResult>> LinkContact([FromBody] OccupancyContactLinkInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try
			{
				var link = await _occupancies.LinkContactAsync(DepartmentId, UserId, input.OccupancyId, input.ContactId, (RmsOccupancyContactRole)input.Role, input.IsPrimary, cancellationToken);
				return Ok(Done(new OccupancyContactLinkResult { Data = RecordsRms5ApiMapper.ToLink(link), PageSize = 1 }));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpDelete("UnlinkContact")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<StandardApiResponseV4Base>> UnlinkContact(string linkId, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { await _occupancies.UnlinkContactAsync(DepartmentId, UserId, linkId, cancellationToken); return Ok(Done(new StandardApiResponseV4Base())); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Runs the ContactPreplan/Contact/POI inventory into crosswalk candidates.</summary>
		[HttpPost("Inventory")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<OccupancyInventoryResult>> Inventory(CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new OccupancyInventoryResult { Data = await _occupancies.InventoryCandidatesAsync(DepartmentId, UserId, cancellationToken), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Candidates")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<OccupancyCrosswalksResult>> Candidates(int state = (int)RmsOccupancyCrosswalkState.Candidate, int skip = 0, int take = 100)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var rows = await _occupancies.GetCandidatesAsync(DepartmentId, UserId, (RmsOccupancyCrosswalkState)state, skip, take);
				var result = new OccupancyCrosswalksResult { Data = rows.Select(RecordsRms5ApiMapper.ToCrosswalk).ToList() };
				result.PageSize = result.Data.Count;
				return Ok(Done(result));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Binds a candidate to an occupancy, or creates one from the source when occupancyId is empty.</summary>
		[HttpPost("LinkCandidate")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<OccupancySavedResult>> LinkCandidate(string crosswalkId, string occupancyId = null, CancellationToken cancellationToken = default)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new OccupancySavedResult { Data = RecordsRms5ApiMapper.ToOccupancy(await _occupancies.LinkCandidateAsync(DepartmentId, UserId, crosswalkId, occupancyId, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("RejectCandidate")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<StandardApiResponseV4Base>> RejectCandidate(string crosswalkId, string reason = null, CancellationToken cancellationToken = default)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { await _occupancies.RejectCandidateAsync(DepartmentId, UserId, crosswalkId, reason, cancellationToken); return Ok(Done(new StandardApiResponseV4Base())); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Merge")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<OccupancySavedResult>> Merge(string sourceOccupancyId, string targetOccupancyId, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new OccupancySavedResult { Data = RecordsRms5ApiMapper.ToOccupancy(await _occupancies.MergeAsync(DepartmentId, UserId, sourceOccupancyId, targetOccupancyId, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Reconciliation")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<OccupancyReconciliationResult>> Reconciliation()
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new OccupancyReconciliationResult { Data = RecordsRms5ApiMapper.ToReconciliation(await _occupancies.GetReconciliationStatusAsync(DepartmentId)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Switches structure writes from Contacts pre-plans to RMS. Refused until every candidate and pre-plan is decided.</summary>
		[HttpPost("SwitchOwnership")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<OccupancyReconciliationResult>> SwitchOwnership(string reason, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				await _occupancies.SwitchWriteOwnershipAsync(DepartmentId, UserId, reason, cancellationToken);
				return Ok(Done(new OccupancyReconciliationResult { Data = RecordsRms5ApiMapper.ToReconciliation(await _occupancies.GetReconciliationStatusAsync(DepartmentId)), PageSize = 1 }));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>OccupancyDispatchProjectionV1 for an occupancy, or for the occupancy a contact is linked to.</summary>
		[HttpGet("DispatchProjection")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<OccupancyProjectionResult>> DispatchProjection(string occupancyId = null, string contactId = null, CancellationToken cancellationToken = default)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				if (!await _occupancies.IsModuleEnabledAsync(DepartmentId)) return NotFound();
				var projection = !string.IsNullOrWhiteSpace(occupancyId)
					? await _occupancies.GetDispatchProjectionAsync(DepartmentId, occupancyId, cancellationToken)
					: await _occupancies.GetDispatchProjectionForContactAsync(DepartmentId, contactId, cancellationToken);
				if (projection == null) return NotFound();
				return Ok(Done(new OccupancyProjectionResult { Data = projection, PageSize = 1 }));
			}
			catch (Exception ex) { return Fail(ex); }
		}
	}
}
