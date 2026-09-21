using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.ContractorBilling;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Service contracts and department compliance documents (Workforce &amp; Business Operations plan, Phase C6):
	/// contract list/editor with document requirements, the detail page with linked bids, deployments and invoices and
	/// the compliance checklist, and the Compliance Documents page (expiry badges, upload, alert lead days). Needs the
	/// Invoicing.ContractorBilling entitlement; ServiceContracts_View reads, ServiceContracts_Update (admins by
	/// default) edits. Contracts and compliance documents are customer-facing and not under Advanced Data Protection.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(32 * 1024 * 1024)]
	[Resgrid.Web.Helpers.DepartmentLocalTime]
	public sealed class ContractsController : SecureBaseController
	{
		private static readonly string[] AllowedExtensions = { "jpg", "jpeg", "png", "gif", "pdf", "doc", "docx", "txt", "xls", "xlsx", "csv", "heic" };
		// Requirement names are user text rendered inside a <script> block; EscapeHtml keeps "</script>" out of the page.
		private static readonly JsonSerializerSettings ScriptJson = new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.EscapeHtml };

		private readonly IServiceContractService _contracts;
		private readonly IRateScheduleService _rateSchedules;
		private readonly IBidsService _bids;
		private readonly IDeploymentService _deployments;
		private readonly IInvoicingService _invoicing;
		private readonly IContactsService _contactsService;
		private readonly IBusinessOperationsAccessService _access;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.ContractorBilling.ContractorBilling> _strings;

		public ContractsController(IServiceContractService contracts, IRateScheduleService rateSchedules, IBidsService bids, IDeploymentService deployments, IInvoicingService invoicing,
			IContactsService contactsService, IBusinessOperationsAccessService access, IStringLocalizer<Resgrid.Localization.Areas.User.ContractorBilling.ContractorBilling> strings)
		{
			_contracts = contracts;
			_rateSchedules = rateSchedules;
			_bids = bids;
			_deployments = deployments;
			_invoicing = invoicing;
			_contactsService = contactsService;
			_access = access;
			_strings = strings;
		}

		#region Plumbing

		private static bool IsAdmin => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
		private static bool CanManage => IsAdmin || ClaimsAuthorizationHelper.CanManageContracts();
		private static bool CanView => CanManage || ClaimsAuthorizationHelper.CanViewContracts();

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
			view.CanManageContracts = CanManage;
			view.CanManageBids = IsAdmin || ClaimsAuthorizationHelper.CanManageBids();
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
		private static bool IsDomainError(InvalidOperationException ex) => ex.Message.StartsWith("contracts_", StringComparison.Ordinal) || ex.Message.StartsWith("compliance_", StringComparison.Ordinal);

		private async Task<Dictionary<string, string>> ContactNamesAsync(IEnumerable<string> contactIds)
		{
			var wanted = new HashSet<string>(contactIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (wanted.Count == 0) return names;
			foreach (var contact in await _contactsService.GetAllContactsForDepartmentAsync(DepartmentId) ?? new List<Contact>())
				if (wanted.Contains(contact.ContactId)) names[contact.ContactId] = contact.Name;
			return names;
		}

		#endregion

		#region Contracts

		[HttpGet]
		public async Task<IActionResult> Index(int? status = null)
		{
			var view = Page(new ContractIndexView { StatusFilter = status });
			view.Contracts = await _contracts.GetContractsForDepartmentAsync(DepartmentId, status.HasValue && Enum.IsDefined(typeof(ServiceContractStatuses), status.Value) ? (ServiceContractStatuses?)status.Value : null);
			view.ContactNames = await ContactNamesAsync(view.Contracts.Select(c => c.ContactId));
			foreach (var schedule in await _rateSchedules.GetSchedulesForDepartmentAsync(DepartmentId, includeInactive: true)) view.ScheduleNames[schedule.RateScheduleId] = schedule.Name;
			var now = DateTime.UtcNow;
			view.ExpiringDocuments = (await _contracts.GetComplianceDocumentsAsync(DepartmentId)).Count(d => d.ExpiresOn.HasValue && d.ExpiresOn.Value.Date <= now.Date.AddDays(d.AlertLeadDays));
			return View(view);
		}

		[HttpGet]
		public async Task<IActionResult> New(string contactId = null)
		{
			if (!CanManage) return Unauthorized();
			var view = Page(new ContractEditView { Contract = new ServiceContract { DepartmentId = DepartmentId, ContactId = contactId, StartOn = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today, TermsNetDays = 30 } });
			await FillLookupsAsync(view);
			return View("Edit", view);
		}

		[HttpGet]
		public async Task<IActionResult> Edit(string id)
		{
			if (!CanManage) return Unauthorized();
			var contract = await _contracts.GetContractByIdAsync(id, DepartmentId);
			if (contract == null) return NotFound();
			var view = Page(new ContractEditView { Contract = contract, RequirementsJson = JsonConvert.SerializeObject(contract.Requirements.Select(r => new { r.ServiceContractDocumentRequirementId, r.Name, r.Stage, r.ComplianceDocumentType, r.IsMandatory, r.SortOrder }), ScriptJson) });
			await FillLookupsAsync(view);
			return View(view);
		}

		private async Task FillLookupsAsync(ContractEditView view)
		{
			view.Contacts = (await _contactsService.GetAllContactsForDepartmentAsync(DepartmentId) ?? new List<Contact>()).Where(c => !c.IsDeleted).OrderBy(c => c.Name)
				.Select(c => new SelectListItem(c.Name, c.ContactId, string.Equals(c.ContactId, view.Contract.ContactId, StringComparison.OrdinalIgnoreCase))).ToList();
			view.Schedules = (await _rateSchedules.GetSchedulesForDepartmentAsync(DepartmentId, includeInactive: true)).OrderBy(s => s.Name)
				.Select(s => new SelectListItem(s.Name + (s.IsActive ? string.Empty : " (" + _strings["Inactive"].Value + ")"), s.RateScheduleId, string.Equals(s.RateScheduleId, view.Contract.RateScheduleId, StringComparison.OrdinalIgnoreCase))).ToList();
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Save(ContractInput input, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			if (input == null) return BadRequest();
			List<ServiceContractDocumentRequirement> requirements = null;
			if (input.RequirementsJson != null)
			{
				try { requirements = JsonConvert.DeserializeObject<List<ServiceContractDocumentRequirement>>(input.RequirementsJson) ?? new List<ServiceContractDocumentRequirement>(); }
				catch (JsonException) { return Refused(400, "contracts_requirement_invalid", string.IsNullOrWhiteSpace(input.ServiceContractId) ? "New" : "Edit", new { id = input.ServiceContractId }); }
			}
			try
			{
				var saved = await _contracts.SaveContractAsync(new ServiceContract
				{
					ServiceContractId = input.ServiceContractId, DepartmentId = DepartmentId, ContactId = input.ContactId, ContractNumber = input.ContractNumber, Name = input.Name, ContractType = input.ContractType,
					StartOn = input.StartOn, EndOn = input.EndOn, RateScheduleId = input.RateScheduleId, DiscountPercent = input.DiscountPercent, TermsNetDays = input.TermsNetDays, InvoiceSubmissionEmail = input.InvoiceSubmissionEmail,
					MaxDeploymentDays = input.MaxDeploymentDays, ResponseTimeMinutes = input.ResponseTimeMinutes, PointOfHire = input.PointOfHire, DocumentTemplateKey = input.DocumentTemplateKey, Notes = input.Notes,
					Status = input.ActivateNow && string.IsNullOrWhiteSpace(input.ServiceContractId) ? (int)ServiceContractStatuses.Active : (int)ServiceContractStatuses.Draft
				}, UserId, Ip, Agent, cancellationToken);
				if (requirements != null) await _contracts.SaveRequirementsAsync(saved.ServiceContractId, DepartmentId, requirements, UserId, Ip, Agent, cancellationToken);
				return Saved("View", new { id = saved.ServiceContractId });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, string.IsNullOrWhiteSpace(input.ServiceContractId) ? "New" : "Edit", new { id = input.ServiceContractId }); }
		}

		[HttpGet]
		public async Task<IActionResult> View(string id)
		{
			var contract = await _contracts.GetContractByIdAsync(id, DepartmentId);
			if (contract == null) return NotFound();
			var view = Page(new ContractDetailView { Contract = contract });
			view.ContactName = (await ContactNamesAsync(new[] { contract.ContactId })).TryGetValue(contract.ContactId, out var name) ? name : contract.ContactId;
			if (!string.IsNullOrWhiteSpace(contract.RateScheduleId)) view.ScheduleName = (await _rateSchedules.GetScheduleByIdAsync(contract.RateScheduleId, DepartmentId, includeInactive: true))?.Name;
			// Contract-scoped queries: filtering a department-wide page after the 500/200 caps would drop this contract's rows once unrelated ones fill the page.
			view.Bids = await _bids.GetBidsForContractAsync(id, DepartmentId);
			view.Deployments = await _deployments.GetDeploymentsForContractAsync(id, DepartmentId);
			try { view.Invoices = await _invoicing.GetInvoicesForDepartmentAsync(DepartmentId, new InvoiceListFilter { ContactId = contract.ContactId, ServiceContractId = id, Take = 200 }); }
			catch (Exception ex) { Resgrid.Framework.Logging.LogException(ex, "Contract detail: invoices unavailable."); }
			view.Compliance = await _contracts.GetContractComplianceForContractAsync(id, DepartmentId);
			foreach (ServiceContractStatuses candidate in Enum.GetValues(typeof(ServiceContractStatuses)))
				if (Resgrid.Services.Invoicing.ServiceContractService.IsValidTransition((ServiceContractStatuses)contract.Status, candidate)) view.NextStatuses.Add(candidate);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SetStatus(string id, int status, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			if (!Enum.IsDefined(typeof(ServiceContractStatuses), status)) return BadRequest();
			try
			{
				await _contracts.SetContractStatusAsync(id, DepartmentId, (ServiceContractStatuses)status, UserId, Ip, Agent, cancellationToken);
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
				if (!await _contracts.DeleteContractAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken)) return NotFound();
				return Saved("Index");
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(409, ex.Message, "View", new { id }); }
		}

		#endregion

		#region Compliance documents

		[HttpGet]
		public async Task<IActionResult> Compliance(int? edit = null)
		{
			var view = Page(new ComplianceView { EditingId = edit });
			view.Documents = await _contracts.GetComplianceDocumentsAsync(DepartmentId);
			if (edit.HasValue) view.Editing = view.Documents.FirstOrDefault(d => d.DepartmentComplianceDocumentId == edit.Value);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveComplianceDocument(ComplianceDocumentInput input, IFormFile file, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			if (input == null) return BadRequest();
			byte[] data = null; string fileName = null, fileType = null;
			if (file != null && file.Length > 0)
			{
				var extension = FileHelper.GetFileExtensionWithoutDot(file.FileName);
				if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return Refused(400, "compliance_file_type", "Compliance");
				if (file.Length > Resgrid.Services.Invoicing.DeploymentService.MaxAttachmentBytes) return Refused(400, "compliance_file_too_large", "Compliance");
				using var stream = file.OpenReadStream();
				data = await FileHelper.ReadAllBytesAsync(stream, cancellationToken);
				fileName = FileHelper.GetSafeFileName(file.FileName);
				fileType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
			}
			try
			{
				await _contracts.SaveComplianceDocumentAsync(new DepartmentComplianceDocument
				{
					DepartmentComplianceDocumentId = input.DepartmentComplianceDocumentId, DepartmentId = DepartmentId, DocumentType = input.DocumentType, Name = input.Name, DocumentNumber = input.DocumentNumber,
					Issuer = input.Issuer, EffectiveOn = input.EffectiveOn, ExpiresOn = input.ExpiresOn, AlertLeadDays = input.AlertLeadDays
				}, data, fileName, fileType, UserId, Ip, Agent, cancellationToken);
				return Saved("Compliance");
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "Compliance"); }
		}

		[HttpGet]
		public async Task<IActionResult> ComplianceFile(int id)
		{
			var document = await _contracts.GetComplianceDocumentAsync(id, DepartmentId, includeData: true);
			if (document == null) return NotFound();
			if (document.Data == null || document.Data.Length == 0) return NotFound();
			return File(document.Data, document.FileType ?? "application/octet-stream", string.IsNullOrWhiteSpace(document.FileName) ? $"compliance-{id}.bin" : document.FileName);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteComplianceDocument(int id, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			if (!await _contracts.DeleteComplianceDocumentAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken)) return NotFound();
			return Saved("Compliance");
		}

		#endregion
	}
}
