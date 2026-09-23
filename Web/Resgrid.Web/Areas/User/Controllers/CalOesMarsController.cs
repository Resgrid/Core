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
using Resgrid.Model.CostRecovery.CalOesMars;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.CostRecovery;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Cal OES MARS cost recovery (Workforce &amp; Business Operations plan, Phase C-M3 / C6): the readiness dashboard,
	/// the annual rate workspace (with agreements and the F-5 resource crosswalk), the incident action queue, the
	/// F-42 / expense editor with validation and the no-store portal handoff, and invoice / payment reconciliation.
	/// Needs the CostRecovery.CalOesMars entitlement. MutualAidReimbursement_View reads the department queue and
	/// readiness; _Update edits agency / resources / rates / agreements; _Submit records what was observed in the portal;
	/// _Reconcile decides and reconciles MARS invoices. Rostered members reach only their own incident-bound F-42 /
	/// expense drafts. The UI says "Prepared for MARS" or "Observed in MARS", never "Submitted" from a download.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Resgrid.Web.Helpers.DepartmentLocalTime]
	public sealed class CalOesMarsController : SecureBaseController
	{
		private readonly ICalOesMarsService _mars;
		private readonly IDeploymentService _deployments;
		private readonly IUnitsService _units;
		private readonly IBusinessOperationsAccessService _access;
		private readonly ICalOesMarsExternalGateway _gateway;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.CalOesMars.CalOesMars> _strings;

		public CalOesMarsController(ICalOesMarsService mars, IDeploymentService deployments, IUnitsService units, IBusinessOperationsAccessService access, ICalOesMarsExternalGateway gateway,
			IStringLocalizer<Resgrid.Localization.Areas.User.CalOesMars.CalOesMars> strings)
		{
			_mars = mars;
			_deployments = deployments;
			_units = units;
			_access = access;
			_gateway = gateway;
			_strings = strings;
		}

		#region Plumbing

		private static bool IsAdmin => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
		private static bool IsManager => IsAdmin || ClaimsAuthorizationHelper.CanManageMutualAidReimbursement();
		private static bool CanView => IsManager || ClaimsAuthorizationHelper.CanViewMutualAidReimbursement();
		private static bool CanSubmit => IsAdmin || ClaimsAuthorizationHelper.CanSubmitMutualAidReimbursement();
		private static bool CanReconcile => IsAdmin || ClaimsAuthorizationHelper.CanReconcileMutualAidReimbursement();
		private static readonly HashSet<string> FieldActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Queue", "WorkItem", "BuildF42", "BuildExpense", "SaveF42", "SaveExpense", "Validate", "Print", "Packet" };
		// JSON that lands inside a <script> block: user text with < > & is unicode-escaped so a stored "</script>" cannot end the element.
		private static readonly JsonSerializerSettings ScriptJson = new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.EscapeHtml };

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			Response.Headers["Cache-Control"] = "no-store";
			if (!await _access.CanUseCostRecoveryAsync(DepartmentId))
			{
				context.Result = Unauthorized();
				return;
			}
			var action = context.ActionDescriptor.RouteValues.TryGetValue("action", out var name) ? name : string.Empty;
			// Field members reach the queue and their own drafts (roster-scoped inside the actions); everything else needs the view claim.
			if (!CanView && !FieldActions.Contains(action))
			{
				context.Result = Unauthorized();
				return;
			}
			await next();
		}

		private T Page<T>(T view) where T : CalOesMarsPageView
		{
			view.IsManager = IsManager;
			view.CanSubmit = CanSubmit;
			view.CanReconcile = CanReconcile;
			view.AuthorityProfileCode = CalOesMarsAuthorityProfile.Current.Code;
			view.PortalUrl = _gateway.GetPortalUrl(null);
			if (TempData["CalOesMarsMessage"] is string message) view.Message = message;
			if (TempData["CalOesMarsSaved"] is bool saved) view.SaveSuccess = saved;
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
			TempData["CalOesMarsMessage"] = ErrorText(code);
			return RedirectToAction(redirectAction, routeValues);
		}

		private IActionResult Saved(string redirectAction, object routeValues = null)
		{
			if (IsAjax()) return Json(new { success = true });
			TempData["CalOesMarsSaved"] = true;
			return RedirectToAction(redirectAction, routeValues);
		}

		private string Ip => IpAddressHelper.GetRequestIP(Request, true);
		private string Agent => $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
		private static bool IsDomainError(InvalidOperationException ex) => ex.Message.StartsWith("calmars_", StringComparison.Ordinal);

		private async Task<IActionResult> GuardedAsync(Func<Task<IActionResult>> action, string redirectAction, object routeValues = null)
		{
			try { return await action(); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, redirectAction, routeValues); }
			catch (ArgumentException) { return Refused(400, "SaveFailed", redirectAction, routeValues); }
			// The rate-line, administrative-input and F-42 forms post page-script JSON as an opaque string; a truncated or tampered post is a refusal, not a 500.
			catch (JsonException) { return Refused(400, "SaveFailed", redirectAction, routeValues); }
		}

		#endregion

		#region 1. Readiness dashboard

		[HttpGet]
		public async Task<IActionResult> Index(DateTime? asOf = null)
		{
			var view = Page(new CalOesMarsDashboardView { AsOf = (asOf ?? Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today).Date });
			view.Readiness = await _mars.GetAgencyReadinessAsync(DepartmentId, view.AsOf);
			view.Authority = CalOesMarsAuthorityProfile.Get(view.Readiness.AuthorityProfileCode) ?? CalOesMarsAuthorityProfile.Current;
			return View(view);
		}

		[HttpGet]
		public async Task<IActionResult> Agency()
		{
			if (!IsManager) return Unauthorized();
			var view = Page(new CalOesMarsAgencyView { Agency = await _mars.GetAgencyProfileAsync(DepartmentId) ?? new CalOesMarsAgencyProfile { DepartmentId = DepartmentId } });
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveAgency(CalOesMarsAgencyProfile input) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			input.DepartmentId = DepartmentId;
			await _mars.SaveAgencyProfileAsync(input, UserId, Ip, Agent);
			return Saved("Agency");
		}, "Agency");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> VerifyAgency() => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			await _mars.MarkAgencyVerifiedAsync(DepartmentId, UserId, Ip, Agent);
			return Saved("Index");
		}, "Index");

		[HttpGet]
		public async Task<IActionResult> Resources(string edit = null)
		{
			if (!IsManager) return Unauthorized();
			var view = Page(new CalOesMarsResourcesView { Resources = await _mars.GetResourceProfilesAsync(DepartmentId), ResourceTypes = CalOesMarsAuthorityProfile.Current.ResourceTypes });
			view.Units = (await _units.GetUnitsForDepartmentAsync(DepartmentId) ?? new List<Unit>()).OrderBy(u => u.Name).Select(u => new SelectListItem(u.Name, u.UnitId.ToString())).ToList();
			if (!string.IsNullOrWhiteSpace(edit)) view.Editing = await _mars.GetResourceProfileAsync(edit, DepartmentId);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> BuildResources(int[] unitIds) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			await _mars.BuildResourceInventoryF5DraftAsync(DepartmentId, unitIds ?? Array.Empty<int>(), UserId, Ip, Agent);
			return Saved("Resources");
		}, "Resources");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveResource(CalOesMarsResourceProfile input) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			input.DepartmentId = DepartmentId;
			await _mars.SaveResourceProfileAsync(input, UserId, Ip, Agent);
			return Saved("Resources");
		}, "Resources");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> ObserveResource(string id, CalOesMarsObservationInput input) => GuardedAsync(async () =>
		{
			if (!CanSubmit) return Unauthorized();
			await _mars.RecordResourceObservationAsync(id, DepartmentId, ToObservation(input), UserId, Ip, Agent);
			return Saved("Resources");
		}, "Resources");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> DeleteResource(string id) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			await _mars.DeleteResourceProfileAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("Resources");
		}, "Resources");

		#endregion

		#region 2. Annual rate workspace and agreements

		[HttpGet]
		public async Task<IActionResult> Rates(int? year = null)
		{
			var view = Page(new CalOesMarsRatesView { Profiles = await _mars.GetRateProfilesAsync(DepartmentId, year), Year = year });
			return View(view);
		}

		[HttpGet]
		public async Task<IActionResult> Rate(string id = null, int? type = null)
		{
			if (!IsManager) return Unauthorized();
			var view = Page(new CalOesMarsRateEditView { Classifications = CalOesMarsAuthorityProfile.Current.SalaryClassifications, ResourceTypes = CalOesMarsAuthorityProfile.Current.ResourceTypes });
			if (!string.IsNullOrWhiteSpace(id))
			{
				view.Profile = await _mars.GetRateProfileAsync(id, DepartmentId);
				if (view.Profile == null) return NotFound();
				view.AdministrativeDraft = CostRecoveryDraft(view.Profile);
				view.WorkforceEnabled = await _access.CanUseWorkforceAsync(DepartmentId);
				view.NextStatuses = Enum.GetValues<CalOesMarsRateProfileStatuses>().Where(s => Resgrid.Services.CostRecovery.CalOesMarsService.IsValidRateTransition((CalOesMarsRateProfileStatuses)view.Profile.Status, s)).ToList();
			}
			else view.Profile = new CalOesMarsRateProfile { DepartmentId = DepartmentId, SubmissionYear = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today.Year, SubmissionType = type ?? (int)CalOesMarsSubmissionTypes.SalarySurvey, EffectiveOn = new DateTime(Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today.Year, 1, 1) };
			view.LinesJson = JsonConvert.SerializeObject(view.Profile.Lines ?? new List<CalOesMarsRateLine>(), ScriptJson);
			view.InputsJson = JsonConvert.SerializeObject((view.Profile.AdministrativeInputs ?? new List<CalOesMarsAdministrativeRateInput>()).Select(i => new { i.CalOesMarsAdministrativeRateInputId, i.FiscalYear, i.FunctionCode, i.CategoryCode, i.Classification, Amount = i.Amount, i.SourceSystem, i.SourceLine, i.IncidentDirectExclusion, i.DoubleCountMarker, i.ReviewStatus, i.ReviewReason }), ScriptJson);
			return View(view);
		}

		private static CalOesMarsAdministrativeRateDraft CostRecoveryDraft(CalOesMarsRateProfile profile) =>
			profile.SubmissionType == (int)CalOesMarsSubmissionTypes.AdministrativeRate || profile.AdministrativeInputs.Count > 0
				? Resgrid.Services.CostRecovery.CalOesMarsService.BuildAdministrativeRateDraft(profile, CalOesMarsAuthorityProfile.Get(profile.AuthorityProfileCode) ?? CalOesMarsAuthorityProfile.Current)
				: null;

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveRate(CalOesMarsRateProfile input) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			input.DepartmentId = DepartmentId;
			var saved = await _mars.SaveRateProfileAsync(input, UserId, Ip, Agent);
			return Saved("Rate", new { id = saved.CalOesMarsRateProfileId });
		}, string.IsNullOrWhiteSpace(input?.CalOesMarsRateProfileId) ? "Rates" : "Rate", string.IsNullOrWhiteSpace(input?.CalOesMarsRateProfileId) ? null : new { id = input.CalOesMarsRateProfileId });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveRateLines(string id, string linesJson) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			var lines = string.IsNullOrWhiteSpace(linesJson) ? new List<CalOesMarsRateLine>() : JsonConvert.DeserializeObject<List<CalOesMarsRateLine>>(linesJson) ?? new List<CalOesMarsRateLine>();
			await _mars.SaveRateLinesAsync(id, DepartmentId, lines, UserId, Ip, Agent);
			return Saved("Rate", new { id });
		}, "Rate", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveAdministrativeInputs(string id, string inputsJson) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			var rows = string.IsNullOrWhiteSpace(inputsJson) ? new List<AdminInputRow>() : JsonConvert.DeserializeObject<List<AdminInputRow>>(inputsJson) ?? new List<AdminInputRow>();
			var inputs = rows.Select(r => new CalOesMarsAdministrativeRateInput
			{
				CalOesMarsAdministrativeRateInputId = r.CalOesMarsAdministrativeRateInputId, FiscalYear = r.FiscalYear, FunctionCode = r.FunctionCode, CategoryCode = r.CategoryCode, Classification = r.Classification,
				ActualAmount = r.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), SourceSystem = r.SourceSystem, SourceLine = r.SourceLine, IncidentDirectExclusion = r.IncidentDirectExclusion, DoubleCountMarker = r.DoubleCountMarker, ReviewStatus = r.ReviewStatus, ReviewReason = r.ReviewReason
			}).ToList();
			await _mars.SaveAdministrativeInputsAsync(id, DepartmentId, inputs, UserId, Ip, Agent);
			return Saved("Rate", new { id });
		}, "Rate", new { id });

		private sealed class AdminInputRow
		{
			public string CalOesMarsAdministrativeRateInputId { get; set; }
			public int FiscalYear { get; set; }
			public string FunctionCode { get; set; }
			public string CategoryCode { get; set; }
			public int Classification { get; set; }
			public decimal Amount { get; set; }
			public string SourceSystem { get; set; }
			public string SourceLine { get; set; }
			public bool IncidentDirectExclusion { get; set; }
			public bool DoubleCountMarker { get; set; }
			public int ReviewStatus { get; set; }
			public string ReviewReason { get; set; }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> BuildAdministrativeRate(string id) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			var draft = await _mars.BuildAdministrativeRateDraftAsync(id, DepartmentId, UserId, Ip, Agent);
			if (!draft.IsReady) { TempData["CalOesMarsMessage"] = string.Join(" ", draft.Blockers.Select(b => ErrorText("AdminBlocker_" + b))); return RedirectToAction("Rate", new { id }); }
			return Saved("Rate", new { id });
		}, "Rate", new { id });

		/// <summary>Phase E composition: classification means from the workforce pay data become the Salary Survey / Attachment A lines (never an individual; the representative still signs in MARS).</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> BuildSalarySurvey(string id, DateTime? asOf) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			if (!await _access.CanUseWorkforceAsync(DepartmentId)) return Refused(403, "workforce_disabled", "Rate", new { id });
			var draft = await _mars.BuildSalarySurveyDraftAsync(id, DepartmentId, asOf ?? Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today, UserId, Ip, Agent);
			if (!draft.IsReady) { TempData["CalOesMarsMessage"] = string.Join(" ", draft.Blockers.Select(b => ErrorText("SurveyBlocker_" + b))); return RedirectToAction("Rate", new { id }); }
			TempData["CalOesMarsSaved"] = true;
			TempData["CalOesMarsMessage"] = string.Format(_strings["SalarySurveyDraftBuilt"].Value, draft.LinesWritten, draft.EmployeesIncluded, draft.UnknownClassifications.Count, draft.Classifications.Count(c => c.SingleEmployee));
			return RedirectToAction("Rate", new { id });
		}, "Rate", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SetRateStatus(string id, int status, string signedByName) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			if (!Enum.IsDefined(typeof(CalOesMarsRateProfileStatuses), status)) return Refused(400, "calmars_rate_transition_invalid", "Rate", new { id });
			await _mars.SetRateProfileStatusAsync(id, DepartmentId, (CalOesMarsRateProfileStatuses)status, signedByName, UserId, Ip, Agent);
			return Saved("Rate", new { id });
		}, "Rate", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> ObserveRate(string id, CalOesMarsObservationInput input) => GuardedAsync(async () =>
		{
			if (!CanSubmit) return Unauthorized();
			await _mars.RecordRateProfileObservationAsync(id, DepartmentId, ToObservation(input), UserId, Ip, Agent);
			return Saved("Rate", new { id });
		}, "Rate", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> DeleteRate(string id) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			await _mars.DeleteRateProfileAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("Rates");
		}, "Rates");

		[HttpGet]
		public async Task<IActionResult> Agreements(string edit = null)
		{
			var view = Page(new CalOesMarsAgreementsView { Agreements = await _mars.GetAgreementsAsync(DepartmentId), Classifications = CalOesMarsAuthorityProfile.Current.SalaryClassifications });
			if (!string.IsNullOrWhiteSpace(edit) && IsManager) view.Editing = await _mars.GetAgreementAsync(edit, DepartmentId);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveAgreement(CalOesMarsAgreementSnapshot input) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			input.DepartmentId = DepartmentId;
			await _mars.SaveAgreementAsync(input, UserId, Ip, Agent);
			return Saved("Agreements");
		}, "Agreements");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> ObserveAgreement(string id, CalOesMarsObservationInput input) => GuardedAsync(async () =>
		{
			if (!CanSubmit) return Unauthorized();
			await _mars.RecordAgreementObservationAsync(id, DepartmentId, ToObservation(input), UserId, Ip, Agent);
			return Saved("Agreements");
		}, "Agreements");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> DeleteAgreement(string id) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			await _mars.DeleteAgreementAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("Agreements");
		}, "Agreements");

		#endregion

		#region 3. Incident action queue

		[HttpGet]
		public async Task<IActionResult> Queue(int? type = null)
		{
			var view = Page(new CalOesMarsQueueView { TypeFilter = type });
			view.Items = await _mars.GetActionQueueAsync(DepartmentId, UserId, IsManager);
			if (type.HasValue) view.Items = view.Items.Where(i => i.WorkItem.RecordType == type.Value).ToList();
			var deployments = IsManager ? await _deployments.GetDeploymentsForDepartmentAsync(DepartmentId, openOnly: false, 0, 200) : await _deployments.GetDeploymentsForUserAsync(DepartmentId, UserId, openOnly: false);
			view.CostRecoveryDeployments = deployments.Where(d => d.FinanceMode == (int)DeploymentFinanceModes.CostRecovery || !string.IsNullOrWhiteSpace(d.RmsExternalOrderId)).OrderByDescending(d => d.StartOn ?? d.AddedOn).ToList();
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> BuildF42(string deploymentId, string fillId) => GuardedAsync(async () =>
		{
			if (!IsManager && !await _deployments.CanFieldMemberSeeAsync(deploymentId, DepartmentId, UserId)) return Unauthorized();
			var item = await _mars.BuildF42DraftAsync(deploymentId, DepartmentId, fillId, UserId, Ip, Agent);
			return Saved("WorkItem", new { id = item.CalOesMarsWorkItemId });
		}, "Queue");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> BuildExpense(string deploymentId, string f42Id) => GuardedAsync(async () =>
		{
			if (!IsManager && !await _deployments.CanFieldMemberSeeAsync(deploymentId, DepartmentId, UserId)) return Unauthorized();
			var item = await _mars.BuildExpenseClaimDraftAsync(deploymentId, DepartmentId, f42Id, UserId, Ip, Agent);
			return Saved("WorkItem", new { id = item.CalOesMarsWorkItemId });
		}, "Queue");

		#endregion

		#region 4. F-42 / expense editor and handoff

		private async Task<(CalOesMarsWorkItem Item, bool Rostered)> LoadItemAsync(string id)
		{
			var item = await _mars.GetWorkItemAsync(id, DepartmentId);
			if (item == null) return (null, false);
			var rostered = !IsManager && await _mars.IsRosteredForWorkItemAsync(id, DepartmentId, UserId);
			return (item, rostered);
		}

		[HttpGet]
		public async Task<IActionResult> WorkItem(string id)
		{
			var (item, rostered) = await LoadItemAsync(id);
			if (item == null) return NotFound();
			if (!IsManager && !CanView && !rostered) return Unauthorized();
			var view = Page(new CalOesMarsWorkItemView { Item = item, IsRostered = rostered, Authority = CalOesMarsAuthorityProfile.Get(item.AuthorityProfileCode) ?? CalOesMarsAuthorityProfile.Current, Classifications = CalOesMarsAuthorityProfile.Current.SalaryClassifications });
			view.CanEdit = (IsManager || rostered) && item.IsLocallyEditable;
			view.Validation = Resgrid.Services.CostRecovery.CalOesMarsService.Deserialize<CalOesMarsValidationResult>(item.ValidationSummaryJson);
			switch ((CalOesMarsRecordTypes)item.RecordType)
			{
				case CalOesMarsRecordTypes.F42: view.F42 = Resgrid.Services.CostRecovery.CalOesMarsService.Deserialize<CalOesMarsF42Snapshot>(item.SnapshotJson); break;
				case CalOesMarsRecordTypes.ExpenseClaim: view.Expense = Resgrid.Services.CostRecovery.CalOesMarsService.Deserialize<CalOesMarsExpenseClaimSnapshot>(item.SnapshotJson); break;
				case CalOesMarsRecordTypes.GeneratedInvoice: return RedirectToAction("Invoice", new { id });
			}
			if (!string.IsNullOrWhiteSpace(item.DeploymentId))
			{
				view.Attachments = await _deployments.GetAttachmentsAsync(item.DeploymentId, DepartmentId) ?? new List<DeploymentAttachment>();
				view.Related = (await _mars.GetWorkItemsForDeploymentAsync(item.DeploymentId, DepartmentId)).Where(w => w.CalOesMarsWorkItemId != item.CalOesMarsWorkItemId).ToList();
			}
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveF42(string id, string snapshotJson) => GuardedAsync(async () =>
		{
			var (item, rostered) = await LoadItemAsync(id);
			if (item == null) return NotFound();
			if (!IsManager && !rostered) return Unauthorized();
			var snapshot = JsonConvert.DeserializeObject<CalOesMarsF42Snapshot>(snapshotJson ?? "{}") ?? new CalOesMarsF42Snapshot();
			var time = Resgrid.Web.Helpers.DepartmentTime.From(ViewData);
            snapshot.ReturnedOn = time.ToUtc(snapshot.ReturnedOn);
            snapshot.RespondingSignedOn = time.ToUtc(snapshot.RespondingSignedOn);
            snapshot.IncidentAuthorizedOn = time.ToUtc(snapshot.IncidentAuthorizedOn);
            foreach (var person in snapshot.Personnel ?? new List<CalOesMarsF42Person>())
            { person.CommittedOn = time.ToUtc(person.CommittedOn); person.ReleasedOn = time.ToUtc(person.ReleasedOn); }
            await _mars.SaveF42SnapshotAsync(id, DepartmentId, snapshot, UserId, Ip, Agent);
			return Saved("WorkItem", new { id });
		}, "WorkItem", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveExpense(string id, CalOesMarsExpenseClaimSnapshot input) => GuardedAsync(async () =>
		{
			var (item, rostered) = await LoadItemAsync(id);
			if (item == null) return NotFound();
			if (!IsManager && !rostered) return Unauthorized();
			if (input != null) { input.SignedOn = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ToUtc(input.SignedOn); input.ApprovedOn = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ToUtc(input.ApprovedOn); }
            await _mars.SaveExpenseSnapshotAsync(id, DepartmentId, input ?? new CalOesMarsExpenseClaimSnapshot(), UserId, Ip, Agent);
			return Saved("WorkItem", new { id });
		}, "WorkItem", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> Validate(string id) => GuardedAsync(async () =>
		{
			var (item, rostered) = await LoadItemAsync(id);
			if (item == null) return NotFound();
			if (!IsManager && !rostered) return Unauthorized();
			var result = await _mars.ValidateForPortalAsync(id, DepartmentId, UserId, Ip, Agent);
			if (IsAjax()) return Json(result);
			TempData["CalOesMarsSaved"] = result.IsReadyForPortal;
			if (!result.IsReadyForPortal) TempData["CalOesMarsMessage"] = string.Format(_strings["ValidationFailedSummary"].Value, result.Errors.Count, result.Warnings.Count);
			return RedirectToAction("WorkItem", new { id });
		}, "WorkItem", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> Calculate(string id) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			var result = await _mars.CalculateExpectedReimbursementAsync(id, DepartmentId, UserId, Ip, Agent);
			if (IsAjax()) return Json(result);
			return Saved("WorkItem", new { id });
		}, "WorkItem", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> Handoff(string id, bool attested = false) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			var item = await _mars.GetWorkItemAsync(id, DepartmentId);
			if (item == null) return NotFound();
			var manifest = await _mars.OpenPortalHandoffAsync(id, DepartmentId, attested, UserId, Ip, Agent);
			Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
			Response.Headers["Pragma"] = "no-cache";
			return View("Handoff", Page(new CalOesMarsHandoffView { Item = item, Manifest = manifest }));
		}, "WorkItem", new { id });

		[HttpGet]
		public async Task<IActionResult> Packet(string id)
		{
			var (item, rostered) = await LoadItemAsync(id);
			if (item == null) return NotFound();
			if (!IsManager && !CanView && !rostered) return Unauthorized();
			var bytes = await _mars.BuildEvidencePacketAsync(id, DepartmentId, UserId);
			return File(bytes, "application/zip", $"mars-evidence-{item.CalOesMarsWorkItemId}.zip");
		}

		[HttpGet]
		public async Task<IActionResult> Print(string id)
		{
			var (item, rostered) = await LoadItemAsync(id);
			if (item == null) return NotFound();
			if (!IsManager && !CanView && !rostered) return Unauthorized();
			return Content(await _mars.RenderWorkItemHtmlAsync(id, DepartmentId), "text/html");
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> ObserveSubmission(string id, CalOesMarsObservationInput input) => GuardedAsync(async () =>
		{
			if (!CanSubmit) return Unauthorized();
			await _mars.RecordExternalSubmissionAsync(id, DepartmentId, ToObservation(input), UserId, Ip, Agent);
			return Saved("WorkItem", new { id });
		}, "WorkItem", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> ObserveStatus(string id, CalOesMarsObservationInput input) => GuardedAsync(async () =>
		{
			if (!CanSubmit) return Unauthorized();
			var item = await _mars.RecordExternalStatusAsync(id, DepartmentId, ToObservation(input), UserId, Ip, Agent);
			return Saved("WorkItem", new { id = item.CalOesMarsWorkItemId });
		}, "WorkItem", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> Close(string id) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			await _mars.CloseWorkItemAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("Queue");
		}, "WorkItem", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> Delete(string id) => GuardedAsync(async () =>
		{
			if (!IsManager) return Unauthorized();
			await _mars.DeleteWorkItemAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("Queue");
		}, "WorkItem", new { id });

		#endregion

		#region 5. Invoice / payment reconciliation

		[HttpGet]
		public async Task<IActionResult> Reconciliation()
		{
			if (!CanReconcile && !IsManager) return Unauthorized();
			var queue = await _mars.GetActionQueueAsync(DepartmentId, UserId, true);
			var view = Page(new CalOesMarsReconciliationView
			{
				Invoices = queue.Where(q => q.WorkItem.RecordType == (int)CalOesMarsRecordTypes.GeneratedInvoice).Select(q => q.WorkItem).ToList(),
				Coverable = queue.Where(q => q.WorkItem.RecordType != (int)CalOesMarsRecordTypes.GeneratedInvoice && q.WorkItem.IsExternal && q.WorkItem.LocalState != (int)CalOesMarsLocalStates.Paid).Select(q => q.WorkItem).ToList()
			});
			foreach (var q in queue.Where(q => !string.IsNullOrWhiteSpace(q.DeploymentName) && !string.IsNullOrWhiteSpace(q.WorkItem.DeploymentId))) view.DeploymentNames[q.WorkItem.DeploymentId] = q.DeploymentName;
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> RecordInvoice(CalOesMarsInvoiceInput input) => GuardedAsync(async () =>
		{
			if (!CanReconcile) return Unauthorized();
			var invoice = await _mars.RecordMarsInvoiceAsync(DepartmentId, input.DeploymentId, new CalOesMarsInvoiceObservation
			{
				MarsInvoiceId = input.MarsInvoiceId, InvoiceDate = input.InvoiceDate, InvoicedTotal = input.InvoicedTotal, PayingEntity = input.PayingEntity, ExternalStatus = input.ExternalStatus,
				ObservedOn = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ToUtc(input.ObservedOn), CoveredWorkItemIds = input.CoveredWorkItemIds ?? new List<string>(), Comment = input.Comment
			}, UserId, Ip, Agent);
			return Saved("Invoice", new { id = invoice.CalOesMarsWorkItemId });
		}, "Reconciliation");

		[HttpGet]
		public async Task<IActionResult> Invoice(string id)
		{
			if (!CanReconcile && !IsManager) return Unauthorized();
			var reconciliation = await _mars.GetInvoiceReconciliationAsync(id, DepartmentId);
			if (reconciliation == null) return NotFound();
			return View(Page(new CalOesMarsInvoiceView { Reconciliation = reconciliation }));
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> DecideInvoice(string id, bool approve, string decisionTitle, string comment) => GuardedAsync(async () =>
		{
			if (!CanReconcile) return Unauthorized();
			await _mars.ApproveOrRejectObservedInvoiceAsync(id, DepartmentId, approve, decisionTitle, comment, UserId, Ip, Agent);
			return Saved("Invoice", new { id });
		}, "Invoice", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> RecordPayment(string id, CalOesMarsPaymentInput input) => GuardedAsync(async () =>
		{
			if (!CanReconcile) return Unauthorized();
			await _mars.RecordPaymentAsync(id, DepartmentId, new CalOesMarsPaymentObservation { PaidTotal = input.PaidTotal, PaidOn = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ToUtc(input.PaidOn), PaymentReference = input.PaymentReference, PayingEntityStatus = input.PayingEntityStatus, Comment = input.Comment }, UserId, Ip, Agent);
			return Saved("Invoice", new { id });
		}, "Invoice", new { id });

		#endregion

		private CalOesMarsExternalObservation ToObservation(CalOesMarsObservationInput input) => new CalOesMarsExternalObservation
		{
			ExternalId = input?.ExternalId, ExternalStatus = input?.ExternalStatus, ObservedOn = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ToUtc(input?.ObservedOn), Comment = input?.Comment, ArtifactChecksum = input?.ArtifactChecksum, ArtifactAttachmentId = input?.ArtifactAttachmentId
		};
	}
}
