using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.ContractorBilling;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Contractor bids (Workforce &amp; Business Operations plan, Phase C6): list with status filter, New (contact →
	/// contract context), Edit (line editor fed by the rate schedule with rate snapshots, crew-size selector, premium
	/// picker, discount cascade display, estimate totals), View (PDF, send, accept/decline/withdraw) and "Schedule
	/// Deployment Call" hand-off to the wizard. Needs the Invoicing.ContractorBilling entitlement; Bids_View reads,
	/// Bids_Create/Update/Delete (admins by default) edit. Bids are customer-facing and not under Advanced Data Protection.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Resgrid.Web.Helpers.DepartmentLocalTime]
	public sealed class BidsController : SecureBaseController
	{
		// Line descriptions, entry and premium names are user text rendered inside a <script> block; EscapeHtml keeps "</script>" out of the page.
		private static readonly JsonSerializerSettings ScriptJson = new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.EscapeHtml };

		private readonly IBidsService _bids;
		private readonly IServiceContractService _contracts;
		private readonly IRateScheduleService _rateSchedules;
		private readonly IInvoicingService _invoicing;
		private readonly IDeploymentService _deployments;
		private readonly IContactsService _contactsService;
		private readonly IBusinessOperationsAccessService _access;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.ContractorBilling.ContractorBilling> _strings;
		private readonly Lazy<IFieldCostingService> _costing;

		public BidsController(IBidsService bids, IServiceContractService contracts, IRateScheduleService rateSchedules, IInvoicingService invoicing, IDeploymentService deployments,
			IContactsService contactsService, IBusinessOperationsAccessService access, IStringLocalizer<Resgrid.Localization.Areas.User.ContractorBilling.ContractorBilling> strings, Lazy<IFieldCostingService> costing = null)
		{
			_costing = costing;
			_bids = bids;
			_contracts = contracts;
			_rateSchedules = rateSchedules;
			_invoicing = invoicing;
			_deployments = deployments;
			_contactsService = contactsService;
			_access = access;
			_strings = strings;
		}

		#region Plumbing

		private static bool IsAdmin => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
		private static bool CanManage => IsAdmin || ClaimsAuthorizationHelper.CanManageBids();
		private static bool CanView => CanManage || ClaimsAuthorizationHelper.CanViewBids();

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			Response.Headers["Cache-Control"] = "no-store";
			if (!CanView || !await _access.CanUseContractorBillingAsync(DepartmentId))
			{
				context.Result = Unauthorized();
				return;
			}
			await next();
		}

		private T Page<T>(T view) where T : ContractorPageView
		{
			view.CanManageBids = CanManage;
			view.CanManageContracts = IsAdmin || ClaimsAuthorizationHelper.CanManageContracts();
			view.CanManageRates = IsAdmin || ClaimsAuthorizationHelper.CanManageInvoicing();
			view.CanManageDeployments = IsAdmin || ClaimsAuthorizationHelper.CanManageDeployments();
			if (TempData["ContractorMessage"] is string message) view.Message = message;
			if (TempData["ContractorSaved"] is bool saved) view.SaveSuccess = saved;
			return view;
		}

		private bool IsAjax() => string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

		private string ErrorText(string code)
		{
			var text = _strings[code];
			return text.ResourceNotFound ? _strings["SaveFailed"].Value : text.Value;
		}

		private IActionResult Refused(int statusCode, string code, string redirectAction, object routeValues = null)
		{
			if (IsAjax()) return StatusCode(statusCode, new { message = ErrorText(code), code });
			TempData["ContractorMessage"] = ErrorText(code);
			return RedirectToAction(redirectAction, routeValues);
		}

		private IActionResult Saved(string redirectAction, object routeValues = null)
		{
			if (IsAjax()) return Json(new { success = true });
			TempData["ContractorSaved"] = true;
			return RedirectToAction(redirectAction, routeValues);
		}

		private string Ip => IpAddressHelper.GetRequestIP(Request, true);
		private string Agent => $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
		private static bool IsDomainError(InvalidOperationException ex) => ex.Message.StartsWith("bids_", StringComparison.Ordinal);

		private async Task<Contact> ContactAsync(string contactId)
		{
			if (string.IsNullOrWhiteSpace(contactId)) return null;
			try { return await _contactsService.GetContactByIdAsync(contactId); }
			catch (Exception ex) { Resgrid.Framework.Logging.LogException(ex, "Bid page: contact unavailable."); return null; }
		}

		#endregion

		[HttpGet]
		public async Task<IActionResult> Index(int? status = null, int page = 1)
		{
			var view = Page(new BidIndexView { StatusFilter = status, Page = Math.Max(1, page) });
			var filter = status.HasValue && Enum.IsDefined(typeof(BidStatuses), status.Value) ? (BidStatuses?)status.Value : null;
			view.Total = await _bids.CountBidsForDepartmentAsync(DepartmentId, filter);
			view.Bids = await _bids.GetBidsForDepartmentAsync(DepartmentId, filter, (view.Page - 1) * view.PageSize, view.PageSize);
			var wanted = new HashSet<string>(view.Bids.Select(b => b.ContactId), StringComparer.OrdinalIgnoreCase);
			if (wanted.Count > 0)
				foreach (var contact in await _contactsService.GetAllContactsForDepartmentAsync(DepartmentId) ?? new List<Contact>())
					if (wanted.Contains(contact.ContactId)) view.ContactNames[contact.ContactId] = contact.Name;
			return View(view);
		}

		[HttpGet]
		public async Task<IActionResult> New(string contactId = null, string contractId = null)
		{
			if (!CanManage) return Unauthorized();
			var view = Page(new BidNewView { ContactId = contactId, ServiceContractId = contractId });
			view.Contacts = (await _contactsService.GetAllContactsForDepartmentAsync(DepartmentId) ?? new List<Contact>()).Where(c => !c.IsDeleted).OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.ContactId, string.Equals(c.ContactId, contactId, StringComparison.OrdinalIgnoreCase))).ToList();
			view.Contracts = (await _contracts.GetContractsForDepartmentAsync(DepartmentId)).Where(c => c.Status is (int)ServiceContractStatuses.Active or (int)ServiceContractStatuses.Draft).ToList();
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Create(string contactId, string serviceContractId, string title, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			try
			{
				var bid = await _bids.CreateDraftBidAsync(DepartmentId, contactId, serviceContractId, title, UserId, Ip, Agent, cancellationToken);
				return Saved("Edit", new { id = bid.BidId });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "New", new { contactId, contractId = serviceContractId }); }
		}

		[HttpGet]
		public async Task<IActionResult> Edit(string id)
		{
			if (!CanManage) return Unauthorized();
			var bid = await _bids.GetBidByIdAsync(id, DepartmentId);
			if (bid == null) return NotFound();
			if (!bid.IsEditable) return RedirectToAction("View", new { id });
			var view = Page(new BidEditView { Bid = bid });
			view.ContactName = (await ContactAsync(bid.ContactId))?.Name ?? bid.ContactId;
			view.Contracts = (await _contracts.GetContractsByContactIdAsync(bid.ContactId, DepartmentId)).Where(c => c.Status is (int)ServiceContractStatuses.Active or (int)ServiceContractStatuses.Draft || string.Equals(c.ServiceContractId, bid.ServiceContractId, StringComparison.OrdinalIgnoreCase)).ToList();
			view.ContractDiscountPercent = view.Contracts.FirstOrDefault(c => string.Equals(c.ServiceContractId, bid.ServiceContractId, StringComparison.OrdinalIgnoreCase))?.DiscountPercent;
			try { view.ProfileDiscountPercent = (await _invoicing.GetBillingProfileByContactIdAsync(bid.ContactId, DepartmentId))?.DefaultDiscountPercent; } catch (Exception ex) { Resgrid.Framework.Logging.LogException(ex, "Bid editor: billing profile unavailable."); }
			var schedules = await _rateSchedules.GetSchedulesForDepartmentAsync(DepartmentId, includeInactive: true);
			view.Schedules = schedules.OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name + (s.IsActive ? string.Empty : " (" + _strings["Inactive"].Value + ")"), s.RateScheduleId, string.Equals(s.RateScheduleId, bid.RateScheduleId, StringComparison.OrdinalIgnoreCase))).ToList();
			view.Schedule = string.IsNullOrWhiteSpace(bid.RateScheduleId) ? null : await _rateSchedules.GetScheduleByIdAsync(bid.RateScheduleId, DepartmentId);
			view.Currency = view.Schedule?.Currency ?? "USD";
			view.LinesJson = JsonConvert.SerializeObject(bid.LineItems.Select(l => new { id = l.BidLineItemId, entryId = l.RateScheduleEntryId, lineType = l.LineType, description = l.Description, crewSize = l.CrewSize, quantity = l.Quantity, hoursPerDay = l.EstimatedHoursPerDay, days = l.EstimatedDays, unitRate = l.UnitRate, premiumIds = l.PremiumIds, taxable = l.Taxable, amount = l.EstimatedAmount }), ScriptJson);
			view.EntriesJson = JsonConvert.SerializeObject((view.Schedule?.Entries ?? new List<RateScheduleEntry>()).Select(e => new
			{
				id = e.RateScheduleEntryId, name = e.Name, entryType = e.EntryType, basis = e.BillingBasis, groupKey = e.GroupKey, crewSize = e.CrewSize, code = e.Code,
				rate = Resgrid.Services.Invoicing.BidsService.SnapshotRate(view.Schedule, new BidLineItem { RateScheduleEntryId = e.RateScheduleEntryId })
			}), ScriptJson);
			view.PremiumsJson = JsonConvert.SerializeObject((view.Schedule?.Premiums ?? new List<RatePremium>()).Select(p => new { id = p.RatePremiumId, name = p.Name, deploymentAdder = p.DeploymentAdder }), ScriptJson);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Save(BidInput input, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			if (input == null || string.IsNullOrWhiteSpace(input.BidId)) return BadRequest();
			List<BidLineItem> lines = null;
			if (input.LinesJson != null)
			{
				try
				{
					var rows = JsonConvert.DeserializeObject<List<LineRow>>(input.LinesJson) ?? new List<LineRow>();
					lines = rows.Select((r, i) => new BidLineItem
					{
						BidLineItemId = r.Id, RateScheduleEntryId = r.EntryId, LineType = r.LineType, Description = r.Description, CrewSize = r.CrewSize, Quantity = r.Quantity, EstimatedHoursPerDay = r.HoursPerDay,
						EstimatedDays = r.Days, UnitRate = r.UnitRate, PremiumIdsJson = r.PremiumIds == null || r.PremiumIds.Count == 0 ? null : JsonConvert.SerializeObject(r.PremiumIds), Taxable = r.Taxable, SortOrder = i
					}).ToList();
				}
				catch (JsonException) { return Refused(400, "bids_line_invalid", "Edit", new { id = input.BidId }); }
			}
			try
			{
				await _bids.SaveBidAsync(new Bid
				{
					BidId = input.BidId, DepartmentId = DepartmentId, ServiceContractId = input.ServiceContractId, RateScheduleId = input.RateScheduleId, Title = input.Title, Description = input.Description, ValidUntil = input.ValidUntil,
					RequestedStartOn = input.RequestedStartOn, RequestedEndOn = input.RequestedEndOn, IncidentNumber = input.IncidentNumber, DeliveryLocation = input.DeliveryLocation, DiscountPercent = input.DiscountPercent,
					Notes = input.Notes, TermsText = input.TermsText
				}, UserId, Ip, Agent, cancellationToken);
				if (lines != null) await _bids.SaveBidLineItemsAsync(input.BidId, DepartmentId, lines, UserId, Ip, Agent, cancellationToken);
				return Saved("Edit", new { id = input.BidId });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "Edit", new { id = input.BidId }); }
		}

		private sealed class LineRow
		{
			public string Id { get; set; }
			public string EntryId { get; set; }
			public int LineType { get; set; }
			public string Description { get; set; }
			public int? CrewSize { get; set; }
			public decimal Quantity { get; set; } = 1;
			public decimal? HoursPerDay { get; set; }
			public decimal? Days { get; set; }
			public decimal UnitRate { get; set; }
			public List<string> PremiumIds { get; set; }
			public bool Taxable { get; set; } = true;
		}

		[HttpGet]
		public async Task<IActionResult> View(string id)
		{
			var bid = await _bids.GetBidByIdAsync(id, DepartmentId);
			if (bid == null) return NotFound();
			var view = Page(new BidDetailView { Bid = bid });
			var contact = await ContactAsync(bid.ContactId);
			view.ContactName = contact?.Name ?? bid.ContactId;
			view.ContactEmail = ProtectedDataEnvelope.SafeDisplay(contact?.Email);
			if (!string.IsNullOrWhiteSpace(bid.ServiceContractId)) view.ContractName = (await _contracts.GetContractByIdAsync(bid.ServiceContractId, DepartmentId))?.Name;
			if (!string.IsNullOrWhiteSpace(bid.RateScheduleId))
			{
				var schedule = await _rateSchedules.GetScheduleByIdAsync(bid.RateScheduleId, DepartmentId, includeInactive: true);
				view.ScheduleName = schedule?.Name;
				view.Currency = schedule?.Currency ?? "USD";
			}
			if (bid.IsConverted) view.ConvertedDeployment = await _deployments.GetDeploymentByIdAsync(bid.ConvertedDeploymentId, DepartmentId);
			// Phase E: the estimated contribution margin card — aggregate only, never on the customer PDF (plan decision 33).
			if (_costing?.Value != null && (IsAdmin || ClaimsAuthorizationHelper.CanViewInternalCosts()) && await _access.CanUseWorkforceAsync(DepartmentId))
			{
				var runs = await _costing.Value.GetRunsForBidAsync(bid.BidId, DepartmentId);
				view.CostCard = new Resgrid.Web.Areas.User.Models.Workforce.FieldCostCardView { BidId = bid.BidId, Latest = runs.OrderByDescending(r => r.AddedOn).FirstOrDefault(), CanRun = true };
			}
			return View(view);
		}

		[HttpGet]
		public async Task<IActionResult> Pdf(string id)
		{
			var bid = await _bids.GetBidByIdAsync(id, DepartmentId);
			if (bid == null) return NotFound();
			var pdf = await _bids.GetBidPdfAsync(id, DepartmentId);
			if (pdf == null || pdf.Length == 0) return Refused(500, "bids_pdf_unavailable", "View", new { id });
			return File(pdf, "application/pdf", $"bid-{bid.BidNumber}.pdf");
		}

		[HttpGet]
		public async Task<IActionResult> Preview(string id)
		{
			var html = await _bids.RenderBidHtmlAsync(id, DepartmentId);
			return html == null ? NotFound() : Content(html, "text/html");
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Send(string id, string toEmail, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			try { await _bids.SendBidAsync(id, DepartmentId, toEmail, UserId, Ip, Agent, cancellationToken); return Saved("View", new { id }); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "View", new { id }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SetStatus(string id, int status, string reason, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			if (!Enum.IsDefined(typeof(BidStatuses), status)) return BadRequest();
			try
			{
				switch ((BidStatuses)status)
				{
					case BidStatuses.Submitted: await _bids.SubmitBidAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken); break;
					case BidStatuses.Accepted: await _bids.AcceptBidAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken); break;
					case BidStatuses.Declined: await _bids.DeclineBidAsync(id, DepartmentId, reason, UserId, Ip, Agent, cancellationToken); break;
					case BidStatuses.Withdrawn: await _bids.WithdrawBidAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken); break;
					default: return Refused(400, "bids_status_transition_invalid", "View", new { id });
				}
				return Saved("View", new { id });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(409, ex.Message, "View", new { id }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			try
			{
				if (!await _bids.DeleteBidAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken)) return NotFound();
				return Saved("Index");
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(409, ex.Message, "View", new { id }); }
		}
	}
}
