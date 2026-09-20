using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.ContractorBilling;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Contractor rate schedules (Workforce &amp; Business Operations plan, C5): schedules, entries with bands, premiums,
	/// clone and JSON import/export. Gated by the Invoicing.ContractorBilling entitlement; Invoicing_View reads,
	/// Invoicing_Update writes.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class RateSchedulesController : V4AuthenticatedApiControllerbase
	{
		private readonly IRateScheduleService _rateSchedules;
		private readonly IBusinessOperationsAccessService _access;

		public RateSchedulesController(IRateScheduleService rateSchedules, IBusinessOperationsAccessService access)
		{
			_rateSchedules = rateSchedules;
			_access = access;
		}

		private Task<bool> EnabledAsync() => _access.CanUseContractorBillingAsync(DepartmentId);
		private string Ip => IpAddressHelper.GetRequestIP(Request, true);
		private string Agent => $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

		private ActionResult<T> Failed<T>(string reason, int status = StatusCodes.Status400BadRequest) where T : StandardApiResponseV4Base, new()
		{
			var failed = new T { PageSize = 0, Status = ResponseHelper.Failure };
			ResponseHelper.PopulateV4ResponseData(failed);
			Response.Headers["X-Resgrid-Reason"] = reason;
			return StatusCode(status, failed);
		}

		[HttpGet("GetRateSchedules")]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RateSchedulesResult>> GetRateSchedules(bool includeInactive = false)
		{
			if (!await EnabledAsync()) return Failed<RateSchedulesResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			var schedules = await _rateSchedules.GetSchedulesForDepartmentAsync(DepartmentId, includeInactive);
			var result = new RateSchedulesResult { Data = schedules.Select(s => Map(s, false)).ToList(), PageSize = schedules.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetRateSchedule")]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RateScheduleResult>> GetRateSchedule(string id, bool includeInactive = false)
		{
			if (!await EnabledAsync()) return Failed<RateScheduleResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			var schedule = await _rateSchedules.GetScheduleByIdAsync(id, DepartmentId, includeInactive);
			return schedule == null ? NotFound() : Ok(schedule);
		}

		[HttpPost("SaveRateSchedule")]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RateScheduleResult>> SaveRateSchedule([FromBody] SaveRateScheduleInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<RateScheduleResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try
			{
				var saved = await _rateSchedules.SaveScheduleAsync(new RateSchedule
				{
					RateScheduleId = input.Id, DepartmentId = DepartmentId, Name = input.Name, Description = input.Description, Currency = input.Currency,
					EffectiveOn = input.EffectiveOn, ExpiresOn = input.ExpiresOn, PolicyJson = input.PolicyJson, IsActive = input.IsActive
				}, UserId, Ip, Agent, cancellationToken);
				return Ok(saved);
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("rateschedules_", StringComparison.Ordinal)) { return Failed<RateScheduleResult>(ex.Message); }
		}

		[HttpDelete("DeleteRateSchedule")]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StandardApiResponseV4Base>> DeleteRateSchedule(string id, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<StandardApiResponseV4Base>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			try
			{
				if (!await _rateSchedules.DeleteScheduleAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken)) return NotFound();
				return Empty();
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("rateschedules_", StringComparison.Ordinal)) { return Failed<StandardApiResponseV4Base>(ex.Message); }
		}

		[HttpPost("CloneRateSchedule")]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RateScheduleResult>> CloneRateSchedule([FromBody] CloneRateScheduleInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<RateScheduleResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try { return Ok(await _rateSchedules.CloneScheduleAsync(input.Id, DepartmentId, input.Name, UserId, Ip, Agent, cancellationToken)); }
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("rateschedules_", StringComparison.Ordinal)) { return Failed<RateScheduleResult>(ex.Message); }
		}

		[HttpPost("SaveEntry")]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RateScheduleResult>> SaveEntry([FromBody] SaveRateScheduleEntryInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<RateScheduleResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try
			{
				await _rateSchedules.SaveEntryAsync(new RateScheduleEntry
				{
					RateScheduleEntryId = input.Id, RateScheduleId = input.RateScheduleId, DepartmentId = DepartmentId, EntryType = input.EntryType, Name = input.Name, Code = input.Code, GroupKey = input.GroupKey,
					CrewSize = input.CrewSize, CertificationCode = input.CertificationCode, UnitTypeId = input.UnitTypeId, InventoryItemId = input.InventoryItemId, InventoryCategoryId = input.InventoryCategoryId,
					BillingBasis = input.BillingBasis, RequiredCertificationsJson = input.RequiredCertificationsJson, SortOrder = input.SortOrder, IsActive = input.IsActive,
					Bands = (input.Bands ?? new List<RateScheduleBandData>()).Select(b => new RateScheduleEntryBand
					{
						RateScheduleEntryBandId = b.Id, BandType = b.BandType, Rate = b.Rate, ThresholdStartHours = b.ThresholdStartHours, ThresholdEndHours = b.ThresholdEndHours, DailyTierMinHours = b.DailyTierMinHours,
						DailyTierMaxHours = b.DailyTierMaxHours, FreeUnitsPerDay = b.FreeUnitsPerDay, RequiresAirTravel = b.RequiresAirTravel, MealCode = b.MealCode, Label = b.Label, SortOrder = b.SortOrder
					}).ToList()
				}, UserId, Ip, Agent, cancellationToken);
				return Ok(await _rateSchedules.GetScheduleByIdAsync(input.RateScheduleId, DepartmentId, includeInactive: true));
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("rateschedules_", StringComparison.Ordinal)) { return Failed<RateScheduleResult>(ex.Message); }
		}

		[HttpDelete("DeleteEntry")]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StandardApiResponseV4Base>> DeleteEntry(string id, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<StandardApiResponseV4Base>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (!await _rateSchedules.DeleteEntryAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken)) return NotFound();
			return Empty();
		}

		[HttpPost("SavePremium")]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RateScheduleResult>> SavePremium([FromBody] SaveRatePremiumInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<RateScheduleResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try
			{
				await _rateSchedules.SavePremiumAsync(new RatePremium
				{
					RatePremiumId = input.Id, RateScheduleId = input.RateScheduleId, DepartmentId = DepartmentId, Name = input.Name, Code = input.Code,
					StandbyAdder = input.StandbyAdder, DeploymentAdder = input.DeploymentAdder, Overtime1Adder = input.Overtime1Adder, Overtime2Adder = input.Overtime2Adder, IsActive = input.IsActive
				}, UserId, Ip, Agent, cancellationToken);
				return Ok(await _rateSchedules.GetScheduleByIdAsync(input.RateScheduleId, DepartmentId, includeInactive: true));
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("rateschedules_", StringComparison.Ordinal)) { return Failed<RateScheduleResult>(ex.Message); }
		}

		[HttpDelete("DeletePremium")]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StandardApiResponseV4Base>> DeletePremium(string id, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<StandardApiResponseV4Base>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (!await _rateSchedules.DeletePremiumAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken)) return NotFound();
			return Empty();
		}

		[HttpGet("ExportRateSchedule")]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RateScheduleExportResult>> ExportRateSchedule(string id)
		{
			if (!await EnabledAsync()) return Failed<RateScheduleExportResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			var json = await _rateSchedules.ExportScheduleJsonAsync(id, DepartmentId);
			if (json == null) return NotFound();
			var result = new RateScheduleExportResult { Data = json, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("ImportRateSchedule")]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RateScheduleResult>> ImportRateSchedule([FromBody] ImportRateScheduleInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<RateScheduleResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try { return Ok(await _rateSchedules.ImportScheduleJsonAsync(DepartmentId, input.Json, UserId, Ip, Agent, cancellationToken)); }
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("rateschedules_", StringComparison.Ordinal)) { return Failed<RateScheduleResult>(ex.Message); }
		}

		#region Mapping

		private ActionResult<RateScheduleResult> Ok(RateSchedule schedule)
		{
			var result = new RateScheduleResult { Data = Map(schedule, true), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private ActionResult<StandardApiResponseV4Base> Empty()
		{
			var result = new StandardApiResponseV4Base { PageSize = 0, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		internal static RateScheduleData Map(RateSchedule s, bool graph) => new RateScheduleData
		{
			Id = s.RateScheduleId, Name = s.Name, Description = s.Description, Currency = s.Currency, EffectiveOn = s.EffectiveOn, ExpiresOn = s.ExpiresOn, PolicyJson = s.PolicyJson, IsActive = s.IsActive,
			AddedOn = s.AddedOn, UpdatedOn = s.EditedOn ?? s.AddedOn,
			Entries = graph ? (s.Entries ?? new List<RateScheduleEntry>()).Select(Map).ToList() : new List<RateScheduleEntryData>(),
			Premiums = graph ? (s.Premiums ?? new List<RatePremium>()).Select(Map).ToList() : new List<RatePremiumData>()
		};

		internal static RateScheduleEntryData Map(RateScheduleEntry e) => new RateScheduleEntryData
		{
			Id = e.RateScheduleEntryId, RateScheduleId = e.RateScheduleId, EntryType = e.EntryType, Name = e.Name, Code = e.Code, GroupKey = e.GroupKey, CrewSize = e.CrewSize, CertificationCode = e.CertificationCode,
			UnitTypeId = e.UnitTypeId, InventoryItemId = e.InventoryItemId, InventoryCategoryId = e.InventoryCategoryId, BillingBasis = e.BillingBasis, RequiredCertificationsJson = e.RequiredCertificationsJson,
			SortOrder = e.SortOrder, IsActive = e.IsActive, UpdatedOn = e.EditedOn ?? e.AddedOn,
			Bands = (e.Bands ?? new List<RateScheduleEntryBand>()).Select(b => new RateScheduleBandData
			{
				Id = b.RateScheduleEntryBandId, BandType = b.BandType, Rate = b.Rate, ThresholdStartHours = b.ThresholdStartHours, ThresholdEndHours = b.ThresholdEndHours, DailyTierMinHours = b.DailyTierMinHours,
				DailyTierMaxHours = b.DailyTierMaxHours, FreeUnitsPerDay = b.FreeUnitsPerDay, RequiresAirTravel = b.RequiresAirTravel, MealCode = b.MealCode, Label = b.Label, SortOrder = b.SortOrder
			}).ToList()
		};

		internal static RatePremiumData Map(RatePremium p) => new RatePremiumData
		{
			Id = p.RatePremiumId, RateScheduleId = p.RateScheduleId, Name = p.Name, Code = p.Code, StandbyAdder = p.StandbyAdder, DeploymentAdder = p.DeploymentAdder, Overtime1Adder = p.Overtime1Adder, Overtime2Adder = p.Overtime2Adder,
			IsActive = p.IsActive, UpdatedOn = p.EditedOn ?? p.AddedOn
		};

		#endregion
	}
}
