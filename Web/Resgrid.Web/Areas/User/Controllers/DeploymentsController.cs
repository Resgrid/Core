using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Deployments;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Deployment finance (Workforce &amp; Business Operations plan, Phase C6, deployment core): the deployment list with
	/// "Create from external order", the detail page (roster, equipment, DTRs, expenses, attachments, manifest), the
	/// DTR editor and expense entry. Free behind Operations.Deployments; nothing here needs the Business Ops add-on.
	/// Managers (Deployments_Update, admins by default) see and edit everything; a rostered member sees the deployments
	/// they are seated on and files time and expenses there. Approval and void need TimeReports_Approve.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(32 * 1024 * 1024)]
	[Resgrid.Web.Helpers.DepartmentLocalTime]
	public sealed class DeploymentsController : SecureBaseController
	{
		private static readonly string[] AllowedExtensions = { "jpg", "jpeg", "png", "gif", "pdf", "doc", "docx", "txt", "xls", "xlsx", "csv", "heic" };

		private readonly IDeploymentService _deployments;
		private readonly ITimeTrackingService _timeTracking;
		private readonly IFeatureToggleService _flags;
		private readonly IDepartmentsService _departments;
		private readonly IUnitsService _units;
		private readonly IContactsService _contacts;
		private readonly ICallsService _calls;
		private readonly IUserProfileService _profiles;
		private readonly IRecordDeploymentsService _recordDeployments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Deployments.Deployments> _strings;
		private readonly IContractorBillingEngine _engine;
		private readonly IServiceContractService _contracts;
		private readonly IInvoicingService _invoicing;
		private readonly IBusinessOperationsAccessService _access;

		public DeploymentsController(IDeploymentService deployments, ITimeTrackingService timeTracking, IFeatureToggleService flags, IDepartmentsService departments, IUnitsService units,
			IContactsService contacts, ICallsService calls, IUserProfileService profiles, IRecordDeploymentsService recordDeployments,
			IStringLocalizer<Resgrid.Localization.Areas.User.Deployments.Deployments> strings,
			IContractorBillingEngine engine, IServiceContractService contracts, IInvoicingService invoicing, IBusinessOperationsAccessService access, Lazy<IFieldCostingService> costing = null)
		{
			_costing = costing;
			_engine = engine;
			_contracts = contracts;
			_invoicing = invoicing;
			_access = access;
			_deployments = deployments;
			_timeTracking = timeTracking;
			_flags = flags;
			_departments = departments;
			_units = units;
			_contacts = contacts;
			_calls = calls;
			_profiles = profiles;
			_recordDeployments = recordDeployments;
			_strings = strings;
		}

		private readonly Lazy<IFieldCostingService> _costing;

		#region Plumbing

		private static bool IsAdmin => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
		private static bool CanManage => IsAdmin || ClaimsAuthorizationHelper.CanManageDeployments();
		private static bool CanView => CanManage || ClaimsAuthorizationHelper.CanViewDeployments();
		private static bool CanApprove => IsAdmin || ClaimsAuthorizationHelper.CanApproveTimeReports();

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			Response.Headers["Cache-Control"] = "no-store";
			if (!await _flags.IsEnabledAsync(FeatureFlagKeys.Deployments, DepartmentId))
			{
				context.Result = Unauthorized();
				return;
			}
			ViewData["DepCanView"] = CanView;
			ViewData["DepCanManage"] = CanManage;
			await next();
		}

		private T Page<T>(T view) where T : DeploymentPageView
		{
			view.CanView = CanView;
			view.CanManage = CanManage;
			view.CanApprove = CanApprove;
			if (TempData["DeploymentsMessage"] is string message) view.Message = message;
			if (TempData["DeploymentsSaved"] is bool saved) view.SaveSuccess = saved;
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
			TempData["DeploymentsMessage"] = ErrorText(code);
			return RedirectToAction(redirectAction, routeValues);
		}

		private IActionResult Saved(string redirectAction, object routeValues = null)
		{
			if (IsAjax()) return Json(new { success = true });
			TempData["DeploymentsSaved"] = true;
			return RedirectToAction(redirectAction, routeValues);
		}

		private string Ip => IpAddressHelper.GetRequestIP(Request, true);
		private string Agent => $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

		private static bool IsDomainError(InvalidOperationException ex) => ex.Message.StartsWith("deployments_", StringComparison.Ordinal) || ex.Message.StartsWith("timereports_", StringComparison.Ordinal) || ex.Message.StartsWith("expenses_", StringComparison.Ordinal);

		private async Task<Deployment> AccessibleAsync(string deploymentId)
		{
			var deployment = await _deployments.GetDeploymentByIdAsync(deploymentId, DepartmentId);
			if (deployment == null) return null;
			if (CanView || (await TimeAccessAsync(deployment)).CanRead) return deployment;
			return null;
		}

		/// <summary>The caller's time scope on the deployment (M0227): own row, crewed units, writable subjects. Managers write everything.</summary>
		private Task<DeploymentTimeAccess> TimeAccessAsync(Deployment deployment) => _deployments.GetTimeAccessAsync(deployment, UserId, CanManage);

		private async Task<Dictionary<string, string>> PersonnelNamesAsync()
		{
			var names = await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
			return (names ?? new List<PersonName>()).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>
		/// ADP reveal endpoint (plan 7.2) for the deployment and edit pages: the wrapper's internal notes (catalog 27). The
		/// grant rides the X-Resgrid-Protected-Grant header and the deployment service resolves the value for a grant
		/// holder. Time reports, expenses and attachments are customer-facing and never protected.
		/// </summary>
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Reveal([FromForm] string kind, [FromForm] string id)
		{
			if (string.IsNullOrWhiteSpace(id)) return BadRequest();
			var deployment = await AccessibleAsync(id);
			if (deployment == null) return NotFound();
			var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var accessor in DeploymentProtectedFields.DeploymentFields) fields[accessor.Key] = accessor.Value.Get(deployment);
			return AdpRevealHelper.Answer(this, fields);
		}

		private async Task<(byte[] Data, string FileName, string FileType, string Error)> ReadUploadAsync(IFormFile file, CancellationToken cancellationToken)
		{
			if (file == null || file.Length == 0) return (null, null, null, null);
			var extension = FileHelper.GetFileExtensionWithoutDot(file.FileName);
			if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return (null, null, null, "deployments_file_type");
			if (file.Length > Resgrid.Services.Invoicing.DeploymentService.MaxAttachmentBytes) return (null, null, null, "deployments_attachment_too_large");
			using var stream = file.OpenReadStream();
			var bytes = await FileHelper.ReadAllBytesAsync(stream, cancellationToken);
			return (bytes, FileHelper.GetSafeFileName(file.FileName), string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType, null);
		}

		#endregion

		#region List and edit

		[HttpGet]
		public async Task<IActionResult> Index(bool all = false, int page = 1)
		{
			var view = Page(new DeploymentIndexView { OpenOnly = !all, Page = Math.Max(1, page), MineOnly = !CanView });
			if (CanView)
			{
				view.Total = await _deployments.CountDeploymentsForDepartmentAsync(DepartmentId, view.OpenOnly);
				view.Deployments = await _deployments.GetDeploymentsForDepartmentAsync(DepartmentId, view.OpenOnly, (view.Page - 1) * view.PageSize, view.PageSize);
			}
			else
			{
				view.Deployments = await _deployments.GetDeploymentsForUserAsync(DepartmentId, UserId, view.OpenOnly);
				view.Total = view.Deployments.Count;
			}
			var contactIds = view.Deployments.Where(d => !string.IsNullOrWhiteSpace(d.ContactId)).Select(d => d.ContactId).Distinct().ToList();
			if (contactIds.Count > 0)
			{
				var contacts = await _contacts.GetAllContactsForDepartmentAsync(DepartmentId) ?? new List<Contact>();
				foreach (var contact in contacts.Where(c => contactIds.Contains(c.ContactId, StringComparer.OrdinalIgnoreCase))) view.ContactNames[contact.ContactId] = contact.Name;
			}
			return View(view);
		}

		[HttpGet]
		public async Task<IActionResult> New()
		{
			if (!CanManage) return Unauthorized();
			var view = Page(new DeploymentEditView { Contacts = await _contacts.GetAllContactsForDepartmentAsync(DepartmentId) ?? new List<Contact>() });
			var department = await _departments.GetDepartmentByIdAsync(DepartmentId);
			view.Deployment.LocalTimeZoneId = department?.TimeZone;
			return View("Edit", view);
		}

		[HttpGet]
		public async Task<IActionResult> Edit(string id)
		{
			if (!CanManage) return Unauthorized();
			var deployment = await _deployments.GetDeploymentByIdAsync(id, DepartmentId);
			if (deployment == null) return NotFound();
			var department = await _departments.GetDepartmentByIdAsync(DepartmentId);
			var view = Page(new DeploymentEditView { Contacts = await _contacts.GetAllContactsForDepartmentAsync(DepartmentId) ?? new List<Contact>(), Deployment = ToInput(deployment, department?.TimeZone), IsProtected = deployment.IsProtected });
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Edit(DeploymentInput input, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			if (input == null) return BadRequest();
			try
			{
				var deployment = string.IsNullOrWhiteSpace(input.DeploymentId) ? new Deployment { DepartmentId = DepartmentId } : await _deployments.GetDeploymentByIdAsync(input.DeploymentId, DepartmentId);
				if (deployment == null) return NotFound();
				var department = await _departments.GetDepartmentByIdAsync(DepartmentId);
				Apply(input, deployment, department?.TimeZone);
				var saved = await _deployments.SaveDeploymentAsync(deployment, UserId, Ip, Agent, cancellationToken);
				return Saved("View", new { id = saved.DeploymentId });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex))
			{
				var view = Page(new DeploymentEditView { Contacts = await _contacts.GetAllContactsForDepartmentAsync(DepartmentId) ?? new List<Contact>(), Deployment = input });
				view.Message = ErrorText(ex.Message);
				return View(view);
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SetStatus(string id, int status, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			if (!Enum.IsDefined(typeof(DeploymentStatuses), status)) return BadRequest();
			try
			{
				await _deployments.SetDeploymentStatusAsync(id, DepartmentId, (DeploymentStatuses)status, UserId, Ip, Agent, cancellationToken);
				return Saved("View", new { id });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "View", new { id }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			try
			{
				await _deployments.DeleteDeploymentAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken);
				return Saved("Index");
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "View", new { id }); }
		}

		// UI windows use the department time zone; deployment accounting retains its own zone. Storage is UTC.
		private static string WindowTimeZone(string deploymentTimeZone, string departmentTimeZone) => string.IsNullOrWhiteSpace(departmentTimeZone) ? "Pacific Standard Time" : departmentTimeZone;

		private static DateTime? ToLocal(DateTime? utc, string timeZone)
		{
			if (!utc.HasValue) return null;
			try { return new DepartmentTime(new Department { TimeZone = timeZone }).Local(utc.Value); }
			catch (Exception) { return utc; }
		}

		// A zone that cannot be resolved is a validation error (deployments_timezone_invalid, re-rendering the submitted form);
		// storing the local clock value as UTC would shift the window by the zone offset without anyone noticing.
		private static DateTime? ToUtc(DateTime? local, string timeZone)
		{
			if (!local.HasValue) return null;
			try { return new DepartmentTime(new Department { TimeZone = timeZone }).ToUtc(local.Value); }
			catch (Exception) { throw new InvalidOperationException("deployments_timezone_invalid"); }
		}

		private static DeploymentInput ToInput(Deployment d, string departmentTimeZone) => new DeploymentInput
		{
			DeploymentId = d.DeploymentId, Name = d.Name, FinanceMode = d.FinanceMode, CallId = d.CallId, ContactId = d.ContactId, IncidentNumber = d.IncidentNumber, ServiceRequestNumber = d.ServiceRequestNumber,
			ResourceOrderNumber = d.ResourceOrderNumber, RequestNumber = d.RequestNumber, CostCode = d.CostCode, PointOfHire = d.PointOfHire,
			StartOn = ToLocal(d.StartOn, WindowTimeZone(d.LocalTimeZoneId, departmentTimeZone)), EndOn = ToLocal(d.EndOn, WindowTimeZone(d.LocalTimeZoneId, departmentTimeZone)), MaxDays = d.MaxDays,
			OutOfProvince = d.OutOfProvince, TravelViaAir = d.TravelViaAir, HomeCountry = d.HomeCountry, HostCountry = d.HostCountry, HomeSubdivision = d.HomeSubdivision, HostSubdivision = d.HostSubdivision,
			LocalTimeZoneId = d.LocalTimeZoneId, Currency = d.Currency, Notes = d.Notes
		};

		private static void Apply(DeploymentInput input, Deployment d, string departmentTimeZone)
		{
			var timeZone = WindowTimeZone(input.LocalTimeZoneId, departmentTimeZone);
			d.Name = input.Name; d.FinanceMode = input.FinanceMode; d.CallId = input.CallId; d.ContactId = string.IsNullOrWhiteSpace(input.ContactId) ? null : input.ContactId;
			d.IncidentNumber = input.IncidentNumber; d.ServiceRequestNumber = input.ServiceRequestNumber; d.ResourceOrderNumber = input.ResourceOrderNumber; d.RequestNumber = input.RequestNumber; d.CostCode = input.CostCode; d.PointOfHire = input.PointOfHire;
			d.StartOn = ToUtc(input.StartOn, timeZone); d.EndOn = ToUtc(input.EndOn, timeZone); d.MaxDays = input.MaxDays;
			d.OutOfProvince = input.OutOfProvince; d.TravelViaAir = input.TravelViaAir; d.HomeCountry = input.HomeCountry; d.HostCountry = input.HostCountry; d.HomeSubdivision = input.HomeSubdivision; d.HostSubdivision = input.HostSubdivision;
			d.LocalTimeZoneId = input.LocalTimeZoneId; d.Currency = input.Currency; d.Notes = input.Notes;
		}

		#endregion

		#region External orders (decision 39)

		[HttpGet]
		public async Task<IActionResult> FromExternalOrder()
		{
			if (!CanManage) return Unauthorized();
			var view = Page(new ExternalOrderPickerView { RecordsEnabled = await _flags.IsEnabledAsync(FeatureFlagKeys.RecordsSystem, DepartmentId) });
			if (view.RecordsEnabled)
			{
				try { view.Orders = await _recordDeployments.ListAsync(DepartmentId, UserId, false) ?? new List<RmsExternalOrder>(); }
				catch (Exception ex) { Logging.LogException(ex, "External orders could not be listed for the deployment picker."); }
				foreach (var order in view.Orders)
					if (await _deployments.GetDeploymentByExternalOrderIdAsync(order.RmsExternalOrderId, DepartmentId) != null) view.Linked.Add(order.RmsExternalOrderId);
			}
			view.Contacts = await _contacts.GetAllContactsForDepartmentAsync(DepartmentId) ?? new List<Contact>();
			view.CallTypes = await _calls.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();
			view.Priorities = await _calls.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> FromExternalOrder(string rmsExternalOrderId, int financeMode, bool createCall, string callType, int callPriority, bool prefillRoster, string name, string contactId, string notes, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			try
			{
				var saved = await _deployments.CreateFromExternalOrderAsync(DepartmentId, new ExternalOrderDeploymentInput
				{
					RmsExternalOrderId = rmsExternalOrderId, FinanceMode = Enum.IsDefined(typeof(DeploymentFinanceModes), financeMode) ? (DeploymentFinanceModes)financeMode : DeploymentFinanceModes.OperationalOnly,
					CreateCall = createCall, CallType = callType, CallPriority = callPriority, PrefillRoster = prefillRoster, Name = name, ContactId = string.IsNullOrWhiteSpace(contactId) ? null : contactId, Notes = notes
				}, UserId, Ip, Agent, cancellationToken);
				return Saved("View", new { id = saved.DeploymentId });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "FromExternalOrder"); }
			catch (UnauthorizedAccessException) { return Unauthorized(); }
		}

		#endregion

		#region Detail and roster

		[HttpGet]
		public async Task<IActionResult> View(string id, string tab = "roster")
		{
			var deployment = await AccessibleAsync(id);
			if (deployment == null) return NotFound();
			var access = await TimeAccessAsync(deployment);
			var view = Page(new DeploymentDetailView { Deployment = deployment, Tab = tab ?? "roster", IsRostered = access.IsRostered, TimeAccess = access });
			view.Department = await _departments.GetDepartmentByIdAsync(DepartmentId);
			view.TimeReports = await _timeTracking.GetTimeReportsAsync(id, DepartmentId);
			view.Expenses = await _timeTracking.GetExpensesAsync(id, DepartmentId);
			view.Attachments = await _deployments.GetAttachmentsAsync(id, DepartmentId);
			view.TotalExpenses = view.Expenses.Sum(e => e.Amount);
			// RecordDeploymentsController admits Record_View holders while the Records flag is on.
			view.ReportsAvailable = ClaimsAuthorizationHelper.CanViewRecords() && await _flags.IsEnabledAsync(FeatureFlagKeys.RecordsSystem, DepartmentId);
			view.UserNames = await PersonnelNamesAsync();
			if (deployment.CallId.HasValue) view.Call = await _calls.GetCallByIdAsync(deployment.CallId.Value);
			if (!string.IsNullOrWhiteSpace(deployment.ContactId)) view.ContactName = (await _contacts.GetContactByIdAsync(deployment.ContactId))?.Name;
			if (!string.IsNullOrWhiteSpace(deployment.RmsExternalOrderId))
			{
				try { view.External = await _deployments.GetExternalContextAsync(id, DepartmentId, UserId); }
				catch (Exception ex) { Logging.LogException(ex, "External order context could not be read for the deployment page."); }
			}
			if (CanManage)
			{
				view.Units = (await _units.GetUnitsForDepartmentUnlimitedAsync(DepartmentId) ?? new List<Unit>()).OrderBy(u => u.Name).ToList();
				// The add-to-roster picker offers active members only; the roster itself is labelled from UserNames.
				view.Personnel = (await _departments.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>()).OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ToList();
				foreach (var unit in deployment.Units.Where(u => u.IsActive))
				{
					try { view.UnitRoles[unit.UnitId] = await _units.GetRolesForUnitAsync(unit.UnitId) ?? new List<UnitRole>(); }
					catch (Exception ex) { Logging.LogException(ex, "Unit roles could not be read for the deployment roster."); }
				}
			}
			view.TotalHours = await _timeTracking.GetPersonnelHoursAsync(id, DepartmentId);
			if (TempData["DeploymentsWarnings"] is string warnings)
				view.Warnings = warnings.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Split('|')).Where(p => p.Length >= 2).Select(p => new DeploymentRosterWarning { Code = p[0], SubjectId = p[1], Detail = p.Length > 2 ? p[2] : null }).ToList();

			// Contractor billing (C-M2): the Billing tab previews the charge run, the contract compliance checklist and the invoices already generated.
			view.ContractorBilling = CanManage && deployment.FinanceMode == (int)DeploymentFinanceModes.Billable && await _access.CanUseContractorBillingAsync(DepartmentId);
			// Phase E: the actual contribution margin tab — aggregate categories only; lines live on the Workforce cost run page.
			if (_costing?.Value != null && (IsAdmin || ClaimsAuthorizationHelper.CanViewInternalCosts()) && await _access.CanUseWorkforceAsync(DepartmentId))
			{
				view.CostRuns = await _costing.Value.GetRunsForDeploymentAsync(deployment.DeploymentId, DepartmentId);
				var defaultRevenue = deployment.FinanceMode == (int)DeploymentFinanceModes.CostRecovery ? Resgrid.Model.Workforce.RevenueSources.CalOesMarsExpected : !string.IsNullOrWhiteSpace(deployment.BidId) ? Resgrid.Model.Workforce.RevenueSources.BidEstimate : Resgrid.Model.Workforce.RevenueSources.CustomerInvoice;
				view.CostCard = new Resgrid.Web.Areas.User.Models.Workforce.FieldCostCardView { DeploymentId = deployment.DeploymentId, Latest = view.CostRuns.OrderByDescending(r => r.AddedOn).FirstOrDefault(), CanRun = true, DefaultRevenueSource = defaultRevenue };
				if (view.Tab == "costs") view.CostComparison = await _costing.Value.CompareEstimateToActualAsync(deployment.DeploymentId, DepartmentId);
			}
			if (view.ContractorBilling && view.Tab == "billing")
			{
				try
				{
					view.Charges = await _engine.CalculateDeploymentChargesAsync(id, DepartmentId);
					view.Compliance = await _contracts.GetContractComplianceAsync(id, DepartmentId);
					if (!string.IsNullOrWhiteSpace(deployment.ContactId))
						view.Invoices = (await _invoicing.GetInvoicesForDepartmentAsync(DepartmentId, new Resgrid.Model.Repositories.InvoiceListFilter { ContactId = deployment.ContactId, Take = 200 })).Where(i => string.Equals(i.DeploymentId, id, StringComparison.OrdinalIgnoreCase)).ToList();
				}
				catch (Exception ex) { Logging.LogException(ex, "Deployment billing tab could not be prepared."); }
			}
			return View(view);
		}

		/// <summary>Contractor billing (C-M2): charge run → draft invoice with DTR provenance; the billed DTRs move to Billed.</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> GenerateInvoice(string id, DateTime? throughDate, CancellationToken cancellationToken)
		{
			if (!CanManage || !await _access.CanUseContractorBillingAsync(DepartmentId)) return Unauthorized();
			try
			{
				var invoice = await _engine.GenerateInvoiceFromDeploymentAsync(id, DepartmentId, throughDate, UserId, Ip, Agent, cancellationToken);
				return RedirectToAction("View", "Invoicing", new { area = "User", id = invoice.InvoiceId });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex) || ex.Message.StartsWith("contractor_", StringComparison.Ordinal) || ex.Message.StartsWith("invoicing_", StringComparison.Ordinal)) { return Refused(400, ex.Message, "View", new { id, tab = "billing" }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> AddUnit(string id, int unitId, string callSign, string notes, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			try
			{
				var result = await _deployments.AddUnitAsync(id, DepartmentId, unitId, callSign, notes, UserId, Ip, Agent, cancellationToken);
				RememberWarnings(result.Warnings);
				return Saved("View", new { id, tab = "roster" });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "View", new { id, tab = "roster" }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> RemoveUnit(string id, string deploymentUnitId, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			try { await _deployments.RemoveUnitAsync(deploymentUnitId, DepartmentId, UserId, Ip, Agent, cancellationToken); return Saved("View", new { id, tab = "roster" }); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "View", new { id, tab = "roster" }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> AddPersonnel(string id, string userId, string deploymentUnitId, int? unitRoleId, string certificationCode, string callSign, bool force, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			try
			{
				var result = await _deployments.AddPersonnelAsync(id, DepartmentId, new DeploymentPersonnelInput
				{
					UserId = userId, DeploymentUnitId = string.IsNullOrWhiteSpace(deploymentUnitId) ? null : deploymentUnitId, UnitRoleId = unitRoleId, CertificationCode = certificationCode, CallSign = callSign, Force = force
				}, UserId, Ip, Agent, cancellationToken);
				RememberWarnings(result.Warnings);
				if (result.Personnel == null)
				{
					TempData["DeploymentsMessage"] = _strings["SeatBlocked"].Value;
					return RedirectToAction("View", new { id, tab = "roster" });
				}
				return Saved("View", new { id, tab = "roster" });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "View", new { id, tab = "roster" }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> RemovePersonnel(string id, string deploymentPersonnelId, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			try { await _deployments.RemovePersonnelAsync(deploymentPersonnelId, DepartmentId, UserId, Ip, Agent, cancellationToken); return Saved("View", new { id, tab = "roster" }); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "View", new { id, tab = "roster" }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> AddEquipment(string id, string deploymentUnitId, string freeTextName, string inventoryAssetId, string notes, bool issueFromInventory, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			try
			{
				var result = await _deployments.AddEquipmentAsync(id, DepartmentId, new DeploymentEquipmentInput
				{
					DeploymentUnitId = string.IsNullOrWhiteSpace(deploymentUnitId) ? null : deploymentUnitId, FreeTextName = freeTextName, InventoryAssetId = inventoryAssetId, Notes = notes, IssueFromInventory = issueFromInventory
				}, UserId, Ip, Agent, cancellationToken);
				RememberWarnings(result.Warnings);
				return Saved("View", new { id, tab = "roster" });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "View", new { id, tab = "roster" }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ReturnEquipment(string id, string deploymentEquipmentId, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			await _deployments.ReturnEquipmentAsync(deploymentEquipmentId, DepartmentId, UserId, Ip, Agent, cancellationToken);
			return Saved("View", new { id, tab = "roster" });
		}

		private void RememberWarnings(List<DeploymentRosterWarning> warnings)
		{
			if (warnings == null || warnings.Count == 0) return;
			TempData["DeploymentsWarnings"] = string.Join("\n", warnings.Select(w => $"{w.Code}|{w.SubjectId}|{w.Detail}"));
		}

		#endregion

		#region Attachments and manifest

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> UploadAttachment(string id, int attachmentType, string name, IFormFile file, CancellationToken cancellationToken)
		{
			var deployment = await AccessibleAsync(id);
			if (deployment == null) return NotFound();
			if (!(await TimeAccessAsync(deployment)).CanWrite) return Unauthorized();
			var upload = await ReadUploadAsync(file, cancellationToken);
			if (upload.Error != null) return Refused(400, upload.Error, "View", new { id, tab = "files" });
			if (upload.Data == null) return Refused(400, "deployments_attachment_empty", "View", new { id, tab = "files" });
			try
			{
				await _deployments.SaveAttachmentAsync(new DeploymentAttachment { DeploymentId = id, DepartmentId = DepartmentId, AttachmentType = attachmentType, Name = name, FileName = upload.FileName, FileType = upload.FileType, Data = upload.Data }, UserId, Ip, Agent, cancellationToken);
				return Saved("View", new { id, tab = "files" });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "View", new { id, tab = "files" }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteAttachment(string id, int deploymentAttachmentId, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			await _deployments.DeleteAttachmentAsync(deploymentAttachmentId, DepartmentId, UserId, Ip, Agent, cancellationToken);
			return Saved("View", new { id, tab = "files" });
		}

		[HttpGet]
		public async Task<IActionResult> GetAttachment(int id)
		{
			var attachment = await _deployments.GetAttachmentAsync(id, DepartmentId, true);
			if (attachment == null) return NotFound();
			if (await AccessibleAsync(attachment.DeploymentId) == null) return Unauthorized();
			if (attachment.Data == null || attachment.Data.Length == 0) return NotFound();
			var contentType = string.IsNullOrWhiteSpace(attachment.FileType) ? FileHelper.GetContentTypeByExtension(System.IO.Path.GetExtension(attachment.FileName ?? string.Empty)) ?? "application/octet-stream" : attachment.FileType;
			return new FileContentResult(attachment.Data, contentType) { FileDownloadName = attachment.FileName ?? $"attachment-{attachment.DeploymentAttachmentId}" };
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> GenerateManifest(string id, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			try { await _deployments.GenerateManifestAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken); return Saved("View", new { id, tab = "files" }); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "View", new { id, tab = "files" }); }
		}

		[HttpGet]
		public async Task<IActionResult> Manifest(string id)
		{
			if (await AccessibleAsync(id) == null) return NotFound();
			try { return Content(await _deployments.RenderManifestHtmlAsync(id, DepartmentId), "text/html", Encoding.UTF8); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return NotFound(); }
		}

		[HttpGet]
		public async Task<IActionResult> ExportTimeEntries(string id)
		{
			if (await AccessibleAsync(id) == null) return NotFound();
			var csv = await _timeTracking.ExportTimeEntriesCsvAsync(id, DepartmentId);
			return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", $"deployment-time-{id}.csv");
		}

		#endregion

		#region Time reports

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> NewTimeReport(string id, DateTime reportDate, string scope, CancellationToken cancellationToken)
		{
			var deployment = await AccessibleAsync(id);
			if (deployment == null) return NotFound();
			var access = await TimeAccessAsync(deployment);
			// "crew:{deploymentUnitId}" is that unit's Crew Time Report, "person:{deploymentPersonnelId}" one person's report and an
			// empty scope the deployment-wide DTR, which only a manager opens. A member without a choice files their own time.
			string unitId = null, personnelId = null;
			if (!string.IsNullOrWhiteSpace(scope) && scope.StartsWith("crew:", StringComparison.Ordinal)) unitId = scope.Substring(5);
			else if (!string.IsNullOrWhiteSpace(scope) && scope.StartsWith("person:", StringComparison.Ordinal)) personnelId = scope.Substring(7);
			else if (!access.CanManage) { personnelId = access.PersonnelId; if (personnelId == null) unitId = access.CrewUnitIds.FirstOrDefault(); }
			var allowed = access.CanManage || (unitId != null && access.CrewUnitIds.Contains(unitId, StringComparer.OrdinalIgnoreCase)) || (personnelId != null && access.CanWriteSubject(personnelId));
			if (!allowed) return Unauthorized();
			try
			{
				var report = await _timeTracking.CreateTimeReportAsync(id, DepartmentId, reportDate, unitId, personnelId, UserId, Ip, Agent, cancellationToken);
				return RedirectToAction("TimeReport", new { id = report.DeploymentTimeReportId });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "View", new { id, tab = "time" }); }
		}

		[HttpGet]
		public async Task<IActionResult> TimeReport(string id)
		{
			var report = await _timeTracking.GetTimeReportByIdAsync(id, DepartmentId);
			if (report == null) return NotFound();
			var deployment = await AccessibleAsync(report.DeploymentId);
			if (deployment == null) return Unauthorized();
			var view = await BuildTimeReportViewAsync(report, deployment);
			if (TempData["TimeReportValidation"] is string issues)
				foreach (var issue in issues.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(i => i.Split('|')))
					(issue[0] == "E" ? view.Validation.Errors : view.Validation.Warnings).Add(new TimeReportIssue { Code = issue.Length > 1 ? issue[1] : null, SubjectId = issue.Length > 2 ? issue[2] : null, Detail = issue.Length > 3 ? issue[3] : null });
			return View(view);
		}

		private async Task<TimeReportEditView> BuildTimeReportViewAsync(DeploymentTimeReport report, Deployment deployment)
		{
			var view = Page(new TimeReportEditView { Report = report, Deployment = deployment, IsRostered = deployment.Personnel.Any(p => p.UserId == UserId), Access = await TimeAccessAsync(deployment) });
			view.Department = await _departments.GetDepartmentByIdAsync(DepartmentId);
			view.TimeZone = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ZoneId;
			foreach (var p in deployment.Personnel) { view.SubjectNames[p.DeploymentPersonnelId] = p.DisplayName ?? p.UserId; if (p.IsActive) view.Subjects.Add((p.DeploymentPersonnelId, (int)DeploymentTimeSubjectTypes.Personnel, p.DisplayName ?? p.UserId)); }
			foreach (var u in deployment.Units) { view.SubjectNames[u.DeploymentUnitId] = u.UnitName ?? u.UnitId.ToString(); if (u.IsActive) view.Subjects.Add((u.DeploymentUnitId, (int)DeploymentTimeSubjectTypes.Unit, u.UnitName ?? u.UnitId.ToString())); }
			foreach (var e in deployment.Equipment) { var n = e.FreeTextName ?? e.InventoryAssetId ?? e.InventoryItemId; view.SubjectNames[e.DeploymentEquipmentId] = n; if (e.IsActive) view.Subjects.Add((e.DeploymentEquipmentId, (int)DeploymentTimeSubjectTypes.Equipment, n)); }
			// A crew report offers its unit, crew and equipment; an individual report its one person; and a member only the subjects they may write.
			bool InScope(string subjectId) => report.Scope switch
			{
				DeploymentTimeReportScopes.Individual => string.Equals(subjectId, report.DeploymentPersonnelId, StringComparison.OrdinalIgnoreCase),
				DeploymentTimeReportScopes.Crew => string.Equals(subjectId, report.DeploymentUnitId, StringComparison.OrdinalIgnoreCase)
					|| deployment.Personnel.Any(p => p.DeploymentPersonnelId == subjectId && string.Equals(p.DeploymentUnitId, report.DeploymentUnitId, StringComparison.OrdinalIgnoreCase))
					|| deployment.Equipment.Any(e => e.DeploymentEquipmentId == subjectId && string.Equals(e.DeploymentUnitId, report.DeploymentUnitId, StringComparison.OrdinalIgnoreCase)),
				_ => true
			};
			view.Subjects = view.Subjects.Where(s => InScope(s.Id) && view.CanWriteSubject(s.Id)).ToList();
			view.ScopeName = report.Scope switch
			{
				DeploymentTimeReportScopes.Crew => string.Format(_strings["ScopeCrew"].Value, view.SubjectNames.TryGetValue(report.DeploymentUnitId, out var unitName) ? unitName : report.DeploymentUnitId),
				DeploymentTimeReportScopes.Individual => string.Format(_strings["ScopeIndividual"].Value, view.SubjectNames.TryGetValue(report.DeploymentPersonnelId, out var personName) ? personName : report.DeploymentPersonnelId),
				_ => _strings["ScopeDeployment"].Value
			};
			view.Expenses = (await _timeTracking.GetExpensesAsync(deployment.DeploymentId, DepartmentId)).Where(e => e.DeploymentTimeReportId == report.DeploymentTimeReportId).ToList();
			if (!string.IsNullOrWhiteSpace(report.ContractorSignedByUserId)) view.ContractorSignerName = (await _profiles.GetProfileByUserIdAsync(report.ContractorSignedByUserId))?.FullName.AsFirstNameLastName;
			return view;
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveTimeReport(string id, string incidentNumber, string resourceOrderNumber, string requestNumber, string costCode, string pointOfHire, bool noClear8, bool unsafeConditionsStandDown, string notes, List<TimeEntryInput> entries, string action, CancellationToken cancellationToken)
		{
			var report = await _timeTracking.GetTimeReportByIdAsync(id, DepartmentId);
			if (report == null) return NotFound();
			var deployment = await AccessibleAsync(report.DeploymentId);
			if (deployment == null) return Unauthorized();
			var access = await TimeAccessAsync(deployment);
			var actsOnReport = access.CanActOn(report);
			// A scoped report is its crew's or person's alone; on a deployment-wide report a member writes only their own subjects.
			if (!access.CanWrite || (report.Scope != DeploymentTimeReportScopes.Deployment && !actsOnReport)) return Unauthorized();
			var timeZone = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ZoneId;
			try
			{
				if (actsOnReport)
				{
					report.IncidentNumber = incidentNumber; report.ResourceOrderNumber = resourceOrderNumber; report.RequestNumber = requestNumber; report.CostCode = costCode; report.PointOfHire = pointOfHire;
					report.NoClear8 = noClear8; report.UnsafeConditionsStandDown = unsafeConditionsStandDown; report.Notes = notes;
					await _timeTracking.UpdateTimeReportAsync(report, UserId, Ip, Agent, cancellationToken);
				}

				var rows = (entries ?? new List<TimeEntryInput>()).Where(e => e != null && !string.IsNullOrWhiteSpace(e.SubjectId) && !string.IsNullOrWhiteSpace(e.Start) && !string.IsNullOrWhiteSpace(e.End)).ToList();
				var mapped = new List<DeploymentTimeEntry>();
				var sort = 0;
				foreach (var row in rows)
				{
					if (!TryParseLocal(report.ReportDate, row.Start, timeZone, out var start) || !TryParseLocal(report.ReportDate, row.End, timeZone, out var end)) return Refused(400, "timereports_time_invalid", "TimeReport", new { id });
					if (end <= start && !row.End.Contains('T')) end = end.AddDays(1);
					var entry = new DeploymentTimeEntry
					{
						DeploymentTimeEntryId = string.IsNullOrWhiteSpace(row.Id) ? null : row.Id,
						EntryType = row.EntryType, StartTime = start, EndTime = end, PaidBreakMinutes = row.PaidBreakMinutes, UnpaidBreakMinutes = row.UnpaidBreakMinutes, MileageKm = row.MileageKm, FuelDeductionLitres = row.FuelDeductionLitres,
						AgencySuppliedMeals = row.AgencySuppliedMeals, AgencySuppliedAccommodation = row.AgencySuppliedAccommodation, CertificationCode = row.CertificationCode, Notes = row.Notes, SortOrder = sort++
					};
					if (deployment.Personnel.Any(p => p.DeploymentPersonnelId == row.SubjectId)) entry.DeploymentPersonnelId = row.SubjectId;
					else if (deployment.Units.Any(u => u.DeploymentUnitId == row.SubjectId)) entry.DeploymentUnitId = row.SubjectId;
					else entry.DeploymentEquipmentId = row.SubjectId;
					mapped.Add(entry);
				}
				var result = await _timeTracking.SaveTimeEntriesAsync(id, DepartmentId, mapped, access, UserId, Ip, Agent, cancellationToken);
				if (!result.Validation.IsValid) { RememberIssues(result.Validation); return RedirectToAction("TimeReport", new { id }); }

				if (string.Equals(action, "submit", StringComparison.OrdinalIgnoreCase))
				{
					if (!access.CanActOn(result.Report)) return Unauthorized();
					var submit = await _timeTracking.SubmitTimeReportAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken);
					RememberIssues(submit.Validation);
					if (!submit.Validation.IsValid) return RedirectToAction("TimeReport", new { id });
				}
				else RememberIssues(result.Validation);
				return Saved("TimeReport", new { id });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "TimeReport", new { id }); }
		}

		private void RememberIssues(TimeReportValidation validation)
		{
			if (validation.Errors.Count == 0 && validation.Warnings.Count == 0) return;
			TempData["TimeReportValidation"] = string.Join("\n", validation.Errors.Select(i => $"E|{i.Code}|{i.SubjectId}|{i.Detail}").Concat(validation.Warnings.Select(i => $"W|{i.Code}|{i.SubjectId}|{i.Detail}")));
		}

		private static bool TryParseLocal(DateTime reportDate, string time, string timeZone, out DateTime utc)
		{
			utc = default;
			// "HH:mm" on the report date, or a full local "yyyy-MM-ddTHH:mm".
			DateTime local;
			if (DateTime.TryParseExact(time, new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var full)) local = full;
			else if (TimeSpan.TryParseExact(time, new[] { "hh\\:mm", "h\\:mm" }, CultureInfo.InvariantCulture, out var span)) local = reportDate.Date.Add(span);
			else return false;
			// Same rule as ToUtc: a zone that cannot be resolved fails the entry (timereports_time_invalid) rather than saving a guessed UTC.
			try { utc = new DepartmentTime(new Department { TimeZone = timeZone }).ToUtc(local); }
			catch { return false; }
			return true;
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ApproveTimeReport(string id, CancellationToken cancellationToken)
		{
			if (!CanApprove) return Unauthorized();
			try { await _timeTracking.ApproveTimeReportAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken); return Saved("TimeReport", new { id }); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "TimeReport", new { id }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> VoidTimeReport(string id, string reason, CancellationToken cancellationToken)
		{
			if (!CanApprove) return Unauthorized();
			try { await _timeTracking.VoidTimeReportAsync(id, DepartmentId, reason, UserId, Ip, Agent, cancellationToken); return Saved("TimeReport", new { id }); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "TimeReport", new { id }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SignTimeReport(string id, bool contractorSigned, string customerSignerName, CancellationToken cancellationToken)
		{
			var report = await _timeTracking.GetTimeReportByIdAsync(id, DepartmentId);
			if (report == null) return NotFound();
			var deployment = await AccessibleAsync(report.DeploymentId);
			if (deployment == null || !(await TimeAccessAsync(deployment)).CanActOn(report)) return Unauthorized();
			try { await _timeTracking.SignTimeReportAsync(id, DepartmentId, contractorSigned, customerSignerName, UserId, Ip, Agent, cancellationToken); return Saved("TimeReport", new { id }); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "TimeReport", new { id }); }
		}

		[HttpGet]
		public async Task<IActionResult> TimeReportPdf(string id)
		{
			var report = await _timeTracking.GetTimeReportByIdAsync(id, DepartmentId);
			if (report == null) return NotFound();
			if (await AccessibleAsync(report.DeploymentId) == null) return Unauthorized();
			var pdf = await _timeTracking.GetTimeReportPdfAsync(id, DepartmentId);
			return File(pdf, "application/pdf", $"dtr-{report.ReportNumber}-{report.ReportDate:yyyyMMdd}.pdf");
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> FileTimeReportPdf(string id, CancellationToken cancellationToken)
		{
			var report = await _timeTracking.GetTimeReportByIdAsync(id, DepartmentId);
			if (report == null) return NotFound();
			var deployment = await AccessibleAsync(report.DeploymentId);
			if (deployment == null || !(await TimeAccessAsync(deployment)).CanActOn(report)) return Unauthorized();
			try { await _timeTracking.GenerateTimeReportPdfAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken); return Saved("TimeReport", new { id }); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "TimeReport", new { id }); }
		}

		#endregion

		#region Expenses

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveExpense(ExpenseInput input, IFormFile receipt, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.DeploymentId)) return BadRequest();
			var deployment = await AccessibleAsync(input.DeploymentId);
			if (deployment == null) return Unauthorized();
			var access = await TimeAccessAsync(deployment);
			if (!access.CanWrite) return Unauthorized();
			// A member edits only the expenses they added and links them only to a report that is theirs; managers edit any.
			if (!access.CanManage && !string.IsNullOrWhiteSpace(input.DeploymentExpenseId))
			{
				var existing = await _timeTracking.GetExpenseByIdAsync(input.DeploymentExpenseId, DepartmentId);
				if (existing == null) return NotFound();
				if (!string.Equals(existing.AddedByUserId, UserId, StringComparison.OrdinalIgnoreCase)) return Unauthorized();
			}
			if (!access.CanManage && !string.IsNullOrWhiteSpace(input.DeploymentTimeReportId))
			{
				var linked = await _timeTracking.GetTimeReportByIdAsync(input.DeploymentTimeReportId, DepartmentId);
				if (linked == null) return NotFound();
				if (!access.CanActOn(linked)) return Unauthorized();
			}
			var back = string.IsNullOrWhiteSpace(input.DeploymentTimeReportId) ? ("View", (object)new { id = input.DeploymentId, tab = "expenses" }) : ("TimeReport", new { id = input.DeploymentTimeReportId });
			var upload = await ReadUploadAsync(receipt, cancellationToken);
			if (upload.Error != null) return Refused(400, upload.Error, back.Item1, back.Item2);
			try
			{
				await _timeTracking.SaveExpenseAsync(new DeploymentExpense
				{
					DeploymentExpenseId = string.IsNullOrWhiteSpace(input.DeploymentExpenseId) ? null : input.DeploymentExpenseId, DeploymentId = input.DeploymentId, DeploymentTimeReportId = string.IsNullOrWhiteSpace(input.DeploymentTimeReportId) ? null : input.DeploymentTimeReportId,
					DepartmentId = DepartmentId, ExpenseDate = input.ExpenseDate ?? Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today, ExpenseType = input.ExpenseType, MealCode = input.MealCode, City = input.City, Description = input.Description, Amount = input.Amount,
					Currency = input.Currency, PreApproved = input.PreApproved, Billable = input.Billable
				}, upload.Data, upload.FileName, upload.FileType, UserId, Ip, Agent, cancellationToken);
				return Saved(back.Item1, back.Item2);
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, back.Item1, back.Item2); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteExpense(string id, string deploymentExpenseId, CancellationToken cancellationToken)
		{
			// The v4 order: the expense names the deployment that is authorized, not the posted id, so a member rostered on
			// one deployment cannot delete another deployment's expense by submitting its id.
			var expense = await _timeTracking.GetExpenseByIdAsync(deploymentExpenseId, DepartmentId);
			if (expense == null) return NotFound();
			var deployment = await AccessibleAsync(expense.DeploymentId);
			if (deployment == null) return Unauthorized();
			var access = await TimeAccessAsync(deployment);
			if (!access.CanWrite || (!access.CanManage && !string.Equals(expense.AddedByUserId, UserId, StringComparison.OrdinalIgnoreCase))) return Unauthorized();
			var back = new { id = deployment.DeploymentId, tab = "expenses" };
			try { await _timeTracking.DeleteExpenseAsync(deploymentExpenseId, DepartmentId, UserId, Ip, Agent, cancellationToken); return Saved("View", back); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "View", back); }
		}

		#endregion
	}
}
