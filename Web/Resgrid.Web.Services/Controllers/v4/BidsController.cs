using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
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
	/// Contractor bids and the deployment charge run (Workforce &amp; Business Operations plan, C5). Gated by the
	/// Invoicing.ContractorBilling entitlement; Bids_View reads, Bids_Create/Update/Delete write, the charge run and
	/// invoice generation need Invoicing_Update. Conversion mirrors the MVC wizard's last step.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class BidsController : V4AuthenticatedApiControllerbase
	{
		private readonly IBidsService _bids;
		private readonly IContractorBillingEngine _engine;
		private readonly IBusinessOperationsAccessService _access;

		public BidsController(IBidsService bids, IContractorBillingEngine engine, IBusinessOperationsAccessService access)
		{
			_bids = bids;
			_engine = engine;
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

		/// <summary>Whether the contractor path is available to this department and what the caller may do. Always answers.</summary>
		[HttpGet("GetAccess")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ContractorAccessResult>> GetAccess()
		{
			var admin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
			var result = new ContractorAccessResult
			{
				Data = new ContractorAccessData
				{
					Enabled = await EnabledAsync(),
					CanManageBids = admin || ClaimsAuthorizationHelper.CanManageBids(),
					CanManageContracts = admin || ClaimsAuthorizationHelper.CanManageContracts(),
					CanManageRateSchedules = admin || ClaimsAuthorizationHelper.CanManageInvoicing()
				},
				PageSize = 1, Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetBids")]
		[Authorize(Policy = ResgridResources.Bids_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<BidsResult>> GetBids(int? status = null, string contactId = null, int skip = 0, int take = 100)
		{
			if (!await EnabledAsync()) return Failed<BidsResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			var bids = string.IsNullOrWhiteSpace(contactId)
				? await _bids.GetBidsForDepartmentAsync(DepartmentId, status.HasValue && Enum.IsDefined(typeof(BidStatuses), status.Value) ? (BidStatuses?)status.Value : null, skip, take)
				: await _bids.GetBidsByContactIdAsync(contactId, DepartmentId, skip, take);
			var result = new BidsResult { Data = bids.Select(b => Map(b, false)).ToList(), PageSize = bids.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetBid")]
		[Authorize(Policy = ResgridResources.Bids_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<BidResult>> GetBid(string id)
		{
			if (!await EnabledAsync()) return Failed<BidResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			var bid = await _bids.GetBidByIdAsync(id, DepartmentId);
			return bid == null ? NotFound() : Ok(bid);
		}

		[HttpPost("NewBid")]
		[Authorize(Policy = ResgridResources.Bids_Create)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<BidResult>> NewBid([FromBody] NewBidInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<BidResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try { return Ok(await _bids.CreateDraftBidAsync(DepartmentId, input.ContactId, input.ServiceContractId, input.Title, UserId, Ip, Agent, cancellationToken)); }
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("bids_", StringComparison.Ordinal)) { return Failed<BidResult>(ex.Message); }
		}

		[HttpPost("UpdateBid")]
		[Authorize(Policy = ResgridResources.Bids_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<BidResult>> UpdateBid([FromBody] SaveBidInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<BidResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null || string.IsNullOrWhiteSpace(input.Id)) return BadRequest();
			try
			{
				var bid = await _bids.SaveBidAsync(new Bid
				{
					BidId = input.Id, DepartmentId = DepartmentId, ServiceContractId = input.ServiceContractId, RateScheduleId = input.RateScheduleId, Title = input.Title, Description = input.Description,
					ValidUntil = input.ValidUntil, RequestedStartOn = input.RequestedStartOn, RequestedEndOn = input.RequestedEndOn, IncidentNumber = input.IncidentNumber, DeliveryLocation = input.DeliveryLocation,
					DiscountPercent = input.DiscountPercent, Notes = input.Notes, TermsText = input.TermsText
				}, UserId, Ip, Agent, cancellationToken);
				if (input.LineItems != null)
					bid = await _bids.SaveBidLineItemsAsync(input.Id, DepartmentId, input.LineItems.Select(l => new BidLineItem
					{
						BidLineItemId = l.Id, RateScheduleEntryId = l.RateScheduleEntryId, LineType = l.LineType, Description = l.Description, CrewSize = l.CrewSize, Quantity = l.Quantity,
						EstimatedHoursPerDay = l.EstimatedHoursPerDay, EstimatedDays = l.EstimatedDays, UnitRate = l.UnitRate, PremiumIdsJson = l.PremiumIds == null || l.PremiumIds.Count == 0 ? null : JsonConvert.SerializeObject(l.PremiumIds),
						Taxable = l.Taxable, SortOrder = l.SortOrder
					}).ToList(), UserId, Ip, Agent, cancellationToken);
				return Ok(bid);
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("bids_", StringComparison.Ordinal)) { return Failed<BidResult>(ex.Message); }
		}

		[HttpPost("SetBidStatus")]
		[Authorize(Policy = ResgridResources.Bids_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<BidResult>> SetBidStatus([FromBody] SetBidStatusInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<BidResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null || !Enum.IsDefined(typeof(BidStatuses), input.Status)) return BadRequest();
			try
			{
				var bid = (BidStatuses)input.Status switch
				{
					BidStatuses.Submitted => await _bids.SubmitBidAsync(input.Id, DepartmentId, UserId, Ip, Agent, cancellationToken),
					BidStatuses.Accepted => await _bids.AcceptBidAsync(input.Id, DepartmentId, UserId, Ip, Agent, cancellationToken),
					BidStatuses.Declined => await _bids.DeclineBidAsync(input.Id, DepartmentId, input.Reason, UserId, Ip, Agent, cancellationToken),
					BidStatuses.Withdrawn => await _bids.WithdrawBidAsync(input.Id, DepartmentId, UserId, Ip, Agent, cancellationToken),
					_ => throw new InvalidOperationException("bids_status_transition_invalid")
				};
				return Ok(bid);
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("bids_", StringComparison.Ordinal)) { return Failed<BidResult>(ex.Message); }
		}

		[HttpPost("SendBid")]
		[Authorize(Policy = ResgridResources.Bids_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<BidResult>> SendBid([FromBody] SendBidInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<BidResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try { return Ok(await _bids.SendBidAsync(input.Id, DepartmentId, input.ToEmail, UserId, Ip, Agent, cancellationToken)); }
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("bids_", StringComparison.Ordinal)) { return Failed<BidResult>(ex.Message); }
		}

		[HttpDelete("DeleteBid")]
		[Authorize(Policy = ResgridResources.Bids_Delete)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StandardApiResponseV4Base>> DeleteBid(string id, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<StandardApiResponseV4Base>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			try
			{
				if (!await _bids.DeleteBidAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken)) return NotFound();
				var result = new StandardApiResponseV4Base { PageSize = 0, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("bids_", StringComparison.Ordinal)) { return Failed<StandardApiResponseV4Base>(ex.Message); }
		}

		[HttpGet("GetBidPdf")]
		[Authorize(Policy = ResgridResources.Bids_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> GetBidPdf(string id)
		{
			if (!await EnabledAsync()) return Forbid();
			var bid = await _bids.GetBidByIdAsync(id, DepartmentId);
			if (bid == null) return NotFound();
			var pdf = await _bids.GetBidPdfAsync(id, DepartmentId);
			if (pdf == null || pdf.Length == 0) return NoContent();
			return File(pdf, "application/pdf", $"bid-{bid.BidNumber}.pdf");
		}

		[HttpGet("GetBidConversionContext")]
		[Authorize(Policy = ResgridResources.Bids_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<BidConversionContextResult>> GetBidConversionContext(string id)
		{
			if (!await EnabledAsync()) return Failed<BidConversionContextResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			var context = await _bids.GetBidConversionContextAsync(id, DepartmentId);
			if (context == null) return NotFound();
			var result = new BidConversionContextResult
			{
				Data = new BidConversionContextData
				{
					Bid = Map(context.Bid, true), Contract = context.Contract == null ? null : ServiceContractsController.Map(context.Contract, true), Schedule = context.Schedule == null ? null : RateSchedulesController.Map(context.Schedule, true),
					ContactName = context.ContactName, EffectiveDiscountPercent = context.EffectiveDiscountPercent, Currency = context.Currency, AlreadyConverted = context.AlreadyConverted
				},
				PageSize = 1, Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("ConvertBidToDeployment")]
		[Authorize(Policy = ResgridResources.Deployments_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<BidConversionResultResult>> ConvertBidToDeployment([FromBody] ConvertBidInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<BidConversionResultResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			if (!ClaimsAuthorizationHelper.CanManageBids() && !ClaimsAuthorizationHelper.IsUserDepartmentAdmin()) return Unauthorized();
			try
			{
				var conversion = await _bids.ConvertBidToDeploymentAsync(new BidConversionRequest
				{
					BidId = input.BidId, CallName = input.CallName, CallNature = input.CallNature, CallPriority = input.CallPriority, CallTypeId = input.CallTypeId, Address = input.Address, GeoLocation = input.GeoLocation,
					StartOn = input.StartOn, EndOn = input.EndOn, MaxDays = input.MaxDays, IncidentNumber = input.IncidentNumber, ServiceRequestNumber = input.ServiceRequestNumber, PointOfHire = input.PointOfHire,
					OutOfProvince = input.OutOfProvince, TravelViaAir = input.TravelViaAir, LocalTimeZoneId = input.LocalTimeZoneId, Notes = input.Notes, CreateCalendarItem = input.CreateCalendarItem,
					UnassignedPersonnel = (input.UnassignedPersonnel ?? new List<ConvertBidSeatInput>()).Select(MapSeat).ToList(),
					Units = (input.Units ?? new List<ConvertBidUnitInput>()).Select(u => new BidConversionUnit
					{
						UnitId = u.UnitId, CallSign = u.CallSign, BidLineItemId = u.BidLineItemId, RateScheduleEntryId = u.RateScheduleEntryId,
						Seats = (u.Seats ?? new List<ConvertBidSeatInput>()).Select(MapSeat).ToList(),
						Equipment = (u.Equipment ?? new List<ConvertBidEquipmentInput>()).Select(e => new BidConversionEquipment { InventoryAssetId = e.InventoryAssetId, InventoryItemId = e.InventoryItemId, FreeTextName = e.FreeTextName, RateScheduleEntryId = e.RateScheduleEntryId, BidLineItemId = e.BidLineItemId }).ToList()
					}).ToList()
				}, DepartmentId, UserId, Ip, Agent, cancellationToken);
				var result = new BidConversionResultResult
				{
					Data = new BidConversionResultData { BidId = conversion.Bid?.BidId, CallId = conversion.CallId, DeploymentId = conversion.Deployment?.DeploymentId, CalendarItemId = conversion.CalendarItemId, Warnings = conversion.Warnings.Select(w => w.Code).Distinct().ToList() },
					PageSize = 1, Status = ResponseHelper.Success
				};
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("bids_", StringComparison.Ordinal) || ex.Message.StartsWith("deployments_", StringComparison.Ordinal)) { return Failed<BidConversionResultResult>(ex.Message); }
		}

		#region Charges

		/// <summary>Dry run of the contractor billing engine over the deployment's approved, unbilled DTRs.</summary>
		[HttpGet("GetDeploymentCharges")]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ContractorChargesResult>> GetDeploymentCharges(string deploymentId, DateTime? throughDate = null)
		{
			if (!await EnabledAsync()) return Failed<ContractorChargesResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			var charges = await _engine.CalculateDeploymentChargesAsync(deploymentId, DepartmentId, throughDate);
			if (charges == null) return NotFound();
			var result = new ContractorChargesResult
			{
				Data = new ContractorChargesData
				{
					DeploymentId = charges.DeploymentId, Currency = charges.Currency, SubTotal = charges.SubTotal, DiscountPercent = charges.DiscountPercent, DiscountAmount = charges.DiscountAmount, ReportIds = charges.ReportIds,
					Lines = charges.Lines.Select(l => new ContractorChargeLineData { Date = l.Date, TimeReportId = l.DeploymentTimeReportId, ReportNumber = l.ReportNumber, SubjectType = l.SubjectType, SubjectId = l.SubjectId, SubjectName = l.SubjectName, EntryName = l.EntryName, Kind = (int)l.Kind, Description = l.Description, Quantity = l.Quantity, UnitRate = l.UnitRate, Amount = l.Amount, Taxable = l.Taxable }).ToList(),
					Warnings = charges.Warnings.Select(w => new ContractorChargeWarningData { Code = w.Code, Message = w.Message, TimeReportId = w.DeploymentTimeReportId, SubjectId = w.SubjectId }).ToList()
				},
				PageSize = 1, Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("GenerateDeploymentInvoice")]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DeploymentInvoiceResult>> GenerateDeploymentInvoice([FromBody] GenerateDeploymentInvoiceInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<DeploymentInvoiceResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try
			{
				var invoice = await _engine.GenerateInvoiceFromDeploymentAsync(input.DeploymentId, DepartmentId, input.ThroughDate, UserId, Ip, Agent, cancellationToken);
				var result = new DeploymentInvoiceResult { Data = new DeploymentInvoiceData { InvoiceId = invoice.InvoiceId, InvoiceNumber = invoice.InvoiceNumber, Total = invoice.Total, Currency = invoice.Currency, LineCount = invoice.LineItems?.Count ?? 0 }, PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("contractor_", StringComparison.Ordinal) || ex.Message.StartsWith("deployments_", StringComparison.Ordinal) || ex.Message.StartsWith("invoicing_", StringComparison.Ordinal)) { return Failed<DeploymentInvoiceResult>(ex.Message); }
		}

		#endregion

		#region Mapping

		private static BidConversionSeat MapSeat(ConvertBidSeatInput s) => new BidConversionSeat { UserId = s.UserId, UnitRoleId = s.UnitRoleId, RateScheduleEntryId = s.RateScheduleEntryId, CertificationCode = s.CertificationCode, PremiumIds = s.PremiumIds ?? new List<string>(), CallSign = s.CallSign };

		private ActionResult<BidResult> Ok(Bid bid)
		{
			var result = new BidResult { Data = Map(bid, true), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		internal static BidData Map(Bid b, bool graph) => new BidData
		{
			Id = b.BidId, BidNumber = b.BidNumber, ContactId = b.ContactId, CustomerBillingProfileId = b.CustomerBillingProfileId, ServiceContractId = b.ServiceContractId, RateScheduleId = b.RateScheduleId, Title = b.Title,
			Description = b.Description, Status = b.Status, ValidUntil = b.ValidUntil, RequestedStartOn = b.RequestedStartOn, RequestedEndOn = b.RequestedEndOn, IncidentNumber = b.IncidentNumber, DeliveryLocation = b.DeliveryLocation,
			DiscountPercent = b.DiscountPercent, EstimatedSubTotal = b.EstimatedSubTotal, EstimatedDiscountAmount = b.EstimatedDiscountAmount, EstimatedTaxAmount = b.EstimatedTaxAmount, EstimatedTotal = b.EstimatedTotal,
			Notes = b.Notes, TermsText = b.TermsText, SentOn = b.SentOn, SentToEmail = b.SentToEmail, AcceptedOn = b.AcceptedOn, DeclinedOn = b.DeclinedOn, DeclineReason = b.DeclineReason, ConvertedCallId = b.ConvertedCallId,
			ConvertedDeploymentId = b.ConvertedDeploymentId, AddedOn = b.AddedOn, UpdatedOn = b.EditedOn ?? b.AddedOn,
			LineItems = graph ? (b.LineItems ?? new List<BidLineItem>()).Select(l => new BidLineData
			{
				Id = l.BidLineItemId, RateScheduleEntryId = l.RateScheduleEntryId, LineType = l.LineType, Description = l.Description, CrewSize = l.CrewSize, Quantity = l.Quantity, EstimatedHoursPerDay = l.EstimatedHoursPerDay,
				EstimatedDays = l.EstimatedDays, UnitRate = l.UnitRate, PremiumIds = l.PremiumIds, EstimatedAmount = l.EstimatedAmount, Taxable = l.Taxable, SortOrder = l.SortOrder
			}).ToList() : new List<BidLineData>()
		};

		#endregion
	}
}
