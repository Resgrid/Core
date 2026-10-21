using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.Services.Models.v4;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>RMS-5 permits, plan review, conditions, expiration and the optional fee reference (RMS plan section 4.3).</summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class RecordPermitsController : RecordsPreventionApiControllerBase
	{
		private readonly IRecordsPermitsService _permits;

		public RecordPermitsController(IRecordsPermitsService permits, IRecordsCutoverService cutover) : base(cutover)
		{
			_permits = permits;
		}

		[HttpGet("Types")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<PermitTypesResult>> Types(bool includeInactive = false)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new PermitTypesResult { Data = (await _permits.GetTypesAsync(DepartmentId, UserId, includeInactive)).Select(RecordsRms5ApiMapper.ToPermitType).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("SaveType")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<PermitTypeResult>> SaveType([FromBody] PermitTypeData input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new PermitTypeResult { Data = RecordsRms5ApiMapper.ToPermitType(await _permits.SaveTypeAsync(DepartmentId, UserId, new RmsPermitType { RmsPermitTypeId = input.PermitTypeId, Name = input.Name, Code = input.Code, Description = input.Description, DefaultValidityDays = input.DefaultValidityDays, RequiresPlanReview = input.RequiresPlanReview, FeeAmount = input.FeeAmount, ConditionsTemplate = input.ConditionsTemplate, IsActive = input.IsActive }, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("List")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<PermitsResult>> List(string occupancyId = null, string permitTypeId = null, [FromQuery] List<int> states = null, DateTime? expiresBefore = null, int skip = 0, int take = 50)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var query = new RmsPermitQuery { OccupancyId = occupancyId, PermitTypeId = permitTypeId, States = states, ExpiresBefore = expiresBefore, Skip = skip, Take = take };
				var r = new PermitsResult { Data = (await _permits.ListAsync(DepartmentId, UserId, query)).Select(RecordsRms5ApiMapper.ToPermit).ToList(), TotalCount = await _permits.CountAsync(DepartmentId, UserId, query) };
				r.PageSize = r.Data.Count;
				return Ok(Done(r));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Get")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<PermitResult>> Get(string id)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var a = await _permits.GetAsync(DepartmentId, UserId, id); if (a == null) return NotFound(); return Ok(Done(new PermitResult { Data = RecordsRms5ApiMapper.ToPermitAggregate(a), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Apply")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<PermitSavedResult>> Apply([FromBody] PermitInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new PermitSavedResult { Data = RecordsRms5ApiMapper.ToPermit(await _permits.ApplyAsync(DepartmentId, UserId, RecordsRms5ApiMapper.FromPermit(input), cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Update")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<PermitSavedResult>> Update([FromBody] PermitInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new PermitSavedResult { Data = RecordsRms5ApiMapper.ToPermit(await _permits.UpdateAsync(DepartmentId, UserId, RecordsRms5ApiMapper.FromPermit(input), cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Transition")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<PermitSavedResult>> Transition([FromBody] PermitTransitionInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new PermitSavedResult { Data = RecordsRms5ApiMapper.ToPermit(await _permits.TransitionAsync(DepartmentId, UserId, input.PermitId, (RmsPermitState)input.TargetState, input.Reason, input.EffectiveOn, input.ExpiresOn, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("PlanReview")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<PlanReviewResult>> PlanReview([FromBody] PlanReviewInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new PlanReviewResult { Data = RecordsRms5ApiMapper.ToPlanReview(await _permits.RecordPlanReviewAsync(DepartmentId, UserId, input.PermitId, (RmsPlanReviewOutcome)input.Outcome, input.Comments, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("RecordFee")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<PermitSavedResult>> RecordFee([FromBody] PermitFeeInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new PermitSavedResult { Data = RecordsRms5ApiMapper.ToPermit(await _permits.RecordFeePaidAsync(DepartmentId, UserId, input.PermitId, input.Amount, input.InvoiceReference, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}
	}

	/// <summary>RMS-5 community risk reduction activities (RMS plan section 4.3).</summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class RecordCrrController : RecordsPreventionApiControllerBase
	{
		private readonly IRecordsCrrService _crr;

		public RecordCrrController(IRecordsCrrService crr, IRecordsCutoverService cutover) : base(cutover)
		{
			_crr = crr;
		}

		[HttpGet("List")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<CrrActivitiesResult>> List(DateTime? start = null, DateTime? end = null, int take = 200)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new CrrActivitiesResult { Data = (await _crr.ListAsync(DepartmentId, UserId, start ?? DateTime.UtcNow.AddDays(-90), end ?? DateTime.UtcNow.AddDays(1), take)).Select(RecordsRms5ApiMapper.ToCrr).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Every CRR activity at an occupancy, newest first, with its smoke / CO / combination alarm counts.</summary>
		[HttpGet("ForOccupancy")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<CrrActivitiesResult>> ForOccupancy(string occupancyId)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var r = new CrrActivitiesResult { Data = (await _crr.GetForOccupancyAsync(DepartmentId, UserId, occupancyId)).Select(RecordsRms5ApiMapper.ToCrr).ToList() }; r.PageSize = r.Data.Count; return Ok(Done(r)); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Get")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<CrrActivityResult>> Get(string id)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var a = await _crr.GetAsync(DepartmentId, UserId, id); if (a == null) return NotFound(); return Ok(Done(new CrrActivityResult { Data = RecordsRms5ApiMapper.ToCrr(a), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Save")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<CrrActivityResult>> Save([FromBody] CrrActivityInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new CrrActivityResult { Data = RecordsRms5ApiMapper.ToCrr(await _crr.SaveAsync(DepartmentId, UserId, RecordsRms5ApiMapper.FromCrr(input), cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpDelete("Delete")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<StandardApiResponseV4Base>> Delete(string id, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { await _crr.DeleteAsync(DepartmentId, UserId, id, cancellationToken); return Ok(Done(new StandardApiResponseV4Base())); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Summary")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<CrrSummaryResult>> Summary(DateTime? start = null, DateTime? end = null)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new CrrSummaryResult { Data = await _crr.GetSummaryAsync(DepartmentId, UserId, start ?? DateTime.UtcNow.AddDays(-90), end ?? DateTime.UtcNow.AddDays(1)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}
	}

	/// <summary>
	/// Smoke / CO alarm requests (M0284) in the CRR module: Open until completing one records the CRR activity for the units
	/// left, or it is declined. A double-submitted completion or an edit of a closed request answers 409.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class RecordAlarmRequestsController : RecordsPreventionApiControllerBase
	{
		private readonly IRecordsAlarmRequestsService _requests;

		public RecordAlarmRequestsController(IRecordsAlarmRequestsService requests, IRecordsCutoverService cutover) : base(cutover)
		{
			_requests = requests;
		}

		/// <summary>Requests by status (RmsAlarmRequestStatus values; none = all), occupancy, assignee and RequestedOn window.</summary>
		[HttpGet("List")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<AlarmRequestsResult>> List([FromQuery] List<int> statuses = null, string occupancyId = null, bool assignedToMe = false, DateTime? from = null, DateTime? to = null, bool oldestFirst = false, int skip = 0, int take = 50)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var query = new RmsAlarmRequestQuery { Statuses = statuses, OccupancyId = occupancyId, AssignedToUserId = assignedToMe ? UserId : null, RequestedFrom = from, RequestedBefore = to, OldestFirst = oldestFirst, Skip = skip, Take = take };
				var r = new AlarmRequestsResult { Data = (await _requests.ListAsync(DepartmentId, UserId, query)).Select(RecordsRms5ApiMapper.ToAlarmRequest).ToList(), TotalCount = await _requests.CountAsync(DepartmentId, UserId, query) };
				r.PageSize = r.Data.Count;
				return Ok(Done(r));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("Get")]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<AlarmRequestResult>> Get(string id)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { var a = await _requests.GetAsync(DepartmentId, UserId, id); if (a == null) return NotFound(); return Ok(Done(new AlarmRequestResult { Data = RecordsRms5ApiMapper.ToAlarmRequest(a), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Save")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<AlarmRequestResult>> Save([FromBody] AlarmRequestInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new AlarmRequestResult { Data = RecordsRms5ApiMapper.ToAlarmRequest(await _requests.SaveAsync(DepartmentId, UserId, RecordsRms5ApiMapper.FromAlarmRequest(input), cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Complete")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<AlarmRequestCompletionResult>> Complete([FromBody] AlarmRequestCompleteInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try
			{
				var done = await _requests.CompleteAsync(DepartmentId, UserId, new AlarmRequestCompletionInput
				{
					RequestId = input.RequestId, OccurredOn = input.OccurredOn, SmokeAlarmsInstalled = input.SmokeAlarmsInstalled, CoAlarmsInstalled = input.CoAlarmsInstalled,
					ComboAlarmsInstalled = input.ComboAlarmsInstalled, AlarmReason = input.AlarmReason, HoursSpent = input.HoursSpent, StaffUserIdsCsv = input.StaffUserIdsCsv, Outcome = input.Outcome
				}, cancellationToken);
				return Ok(Done(new AlarmRequestCompletionResult { Data = new AlarmRequestCompletionData { Request = RecordsRms5ApiMapper.ToAlarmRequest(done.Request), Activity = RecordsRms5ApiMapper.ToCrr(done.Activity) }, PageSize = 1 }));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Decline")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<AlarmRequestResult>> Decline([FromBody] AlarmRequestDeclineInput input, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			if (input == null) return BadRequest();
			try { return Ok(Done(new AlarmRequestResult { Data = RecordsRms5ApiMapper.ToAlarmRequest(await _requests.DeclineAsync(DepartmentId, UserId, input.RequestId, (RmsAlarmRequestDeclineReason)input.DeclineReason, input.Notes, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Reopen")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<AlarmRequestResult>> Reopen(string id, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { return Ok(Done(new AlarmRequestResult { Data = RecordsRms5ApiMapper.ToAlarmRequest(await _requests.ReopenAsync(DepartmentId, UserId, id, cancellationToken)), PageSize = 1 })); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpDelete("Delete")]
		[Authorize(Policy = ResgridResources.Record_PreventionAdmin)]
		public async Task<ActionResult<StandardApiResponseV4Base>> Delete(string id, CancellationToken cancellationToken)
		{
			if (!await FlagOnAsync()) return NotFound();
			try { await _requests.DeleteAsync(DepartmentId, UserId, id, cancellationToken); return Ok(Done(new StandardApiResponseV4Base())); }
			catch (Exception ex) { return Fail(ex); }
		}
	}
}
