using System;
using System.Collections.Generic;
using System.Linq;
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
using Resgrid.Model.Workforce;
using Resgrid.Web.Areas.User.Models.Workforce;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Workforce pay data, field costing and California pay data reporting (Workforce &amp; Business Operations plan,
	/// Phase E / E6). Needs the Workforce.InternalCosting entitlement for the workforce, compensation and costing
	/// screens and Compliance.CaliforniaPayDataReporting (plus an Enabled ADP state) for the report wizard.
	/// Workforce_View / _Update (75, 77) manage employer, establishments, workers and employments;
	/// WorkforceCompensation_View / _Update (75, 76) the compensation profiles and annual facts; InternalCosts_View
	/// (74) the aggregate cost runs; PayDataReporting_View / _Update / _Export (77, 78) the CRD wizard. Every member
	/// reaches their own demographic response. Protected values render REDACTED without a current grant.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Resgrid.Web.Helpers.DepartmentLocalTime]
	public sealed class WorkforceController : SecureBaseController
	{
		private readonly IWorkforceService _workforce;
		private readonly ICompensationCostService _compensation;
		private readonly IFieldCostingService _costing;
		private readonly IPayDataDemographicsService _demographics;
		private readonly ICaPayDataReportingService _reporting;
		private readonly IBusinessOperationsAccessService _access;
		private readonly IPersonnelRolesService _roles;
		private readonly IUserProfileService _profiles;
		private readonly IUnitsService _units;
		private readonly IDeploymentService _deployments;
		private readonly IBidsService _bids;
		private readonly ICallsService _calls;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Workforce.Workforce> _strings;

		public WorkforceController(IWorkforceService workforce, ICompensationCostService compensation, IFieldCostingService costing, IPayDataDemographicsService demographics, ICaPayDataReportingService reporting,
			IBusinessOperationsAccessService access, IPersonnelRolesService roles, IUserProfileService profiles, IUnitsService units, IDeploymentService deployments, IBidsService bids, ICallsService calls,
			IStringLocalizer<Resgrid.Localization.Areas.User.Workforce.Workforce> strings)
		{
			_workforce = workforce;
			_compensation = compensation;
			_costing = costing;
			_demographics = demographics;
			_reporting = reporting;
			_access = access;
			_roles = roles;
			_profiles = profiles;
			_units = units;
			_deployments = deployments;
			_bids = bids;
			_calls = calls;
			_strings = strings;
		}

		#region Plumbing

		private static bool IsAdmin => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
		private static bool CanManage => IsAdmin || ClaimsAuthorizationHelper.CanManageWorkforce();
		private static bool CanView => CanManage || ClaimsAuthorizationHelper.CanViewWorkforce();
		private static bool CanManageCompensation => IsAdmin || ClaimsAuthorizationHelper.CanManageWorkforceCompensation();
		private static bool CanViewCompensation => CanManageCompensation || ClaimsAuthorizationHelper.CanViewWorkforceCompensation();
		private static bool CanViewInternalCosts => IsAdmin || ClaimsAuthorizationHelper.CanViewInternalCosts();
		private static bool CanManagePayData => IsAdmin || ClaimsAuthorizationHelper.CanManagePayDataReporting();
		private static bool CanViewPayData => CanManagePayData || ClaimsAuthorizationHelper.CanViewPayDataReporting();
		private static bool CanExportPayData => IsAdmin || ClaimsAuthorizationHelper.CanExportPayDataReporting();
		private static readonly HashSet<string> PayDataActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PayData", "PayDataRun", "CreatePayDataRun", "BuildSnapshots", "OverrideSnapshot", "AggregateRows", "SaveRemarks", "ValidateRun", "FreezeAndExport", "DownloadArtifact", "Worksheet", "MarkCertified", "CreateCorrection", "VoidRun", "MyDemographics", "SaveMyDemographics", "WorkerDemographics", "SaveWorkerDemographics" };
		private static readonly HashSet<string> SelfActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MyDemographics", "SaveMyDemographics" };
		// JSON that lands inside a <script> block: component names and pay-code lists are user text, so < > & are unicode-escaped.
		private static readonly JsonSerializerSettings ScriptJson = new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.EscapeHtml };

		private bool _workforceEnabled;
		private bool _payDataEnabled;

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			Response.Headers["Cache-Control"] = "no-store";
			_workforceEnabled = await _access.CanUseWorkforceAsync(DepartmentId);
			_payDataEnabled = await _access.CanUsePayDataReportingAsync(DepartmentId);
			var action = context.ActionDescriptor.RouteValues.TryGetValue("action", out var name) ? name : string.Empty;
			var payData = PayDataActions.Contains(action);
			if (payData ? !_payDataEnabled : !_workforceEnabled && !(action == "Index" && _payDataEnabled)) { context.Result = Unauthorized(); return; }
			// Every member answers their own demographic response; everything else needs a view claim of its family.
			if (!SelfActions.Contains(action) && !CanView && !CanViewCompensation && !CanViewInternalCosts && !CanViewPayData) { context.Result = Unauthorized(); return; }
			await next();
		}

		private T Page<T>(T view) where T : WorkforcePageView
		{
			view.CanManage = CanManage; view.CanViewCompensation = CanViewCompensation; view.CanManageCompensation = CanManageCompensation; view.CanViewInternalCosts = CanViewInternalCosts;
			view.CanViewPayData = CanViewPayData; view.CanManagePayData = CanManagePayData; view.CanExportPayData = CanExportPayData;
			view.WorkforceEnabled = _workforceEnabled; view.PayDataEnabled = _payDataEnabled;
			if (TempData["WorkforceMessage"] is string message) view.Message = message;
			if (TempData["WorkforceSaved"] is bool saved) view.SaveSuccess = saved;
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
			TempData["WorkforceMessage"] = ErrorText(code);
			return RedirectToAction(redirectAction, routeValues);
		}

		private IActionResult Saved(string redirectAction, object routeValues = null)
		{
			if (IsAjax()) return Json(new { success = true });
			TempData["WorkforceSaved"] = true;
			return RedirectToAction(redirectAction, routeValues);
		}

		private string Ip => IpAddressHelper.GetRequestIP(Request, true);
		private string Agent => $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
		private static bool IsDomainError(InvalidOperationException ex) => ex.Message.StartsWith("workforce_", StringComparison.Ordinal) || ex.Message.StartsWith("paydata_", StringComparison.Ordinal);

		private async Task<IActionResult> GuardedAsync(Func<Task<IActionResult>> action, string redirectAction, object routeValues = null)
		{
			try { return await action(); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, redirectAction, routeValues); }
			catch (ArgumentException) { return Refused(400, "SaveFailed", redirectAction, routeValues); }
			catch (JsonException) { return Refused(400, "SaveFailed", redirectAction, routeValues); }
		}

		private async Task<Dictionary<string, string>> MemberNamesAsync()
		{
			var profiles = await _profiles.GetAllProfilesForDepartmentAsync(DepartmentId) ?? new Dictionary<string, UserProfile>();
			return profiles.ToDictionary(p => p.Key, p => p.Value?.FullName.AsFirstNameLastName ?? p.Key, StringComparer.OrdinalIgnoreCase);
		}

		private async Task<List<SelectListItem>> RoleItemsAsync() => (await _roles.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>()).OrderBy(r => r.Name).Select(r => new SelectListItem(r.Name, r.PersonnelRoleId.ToString())).ToList();
		private async Task<List<SelectListItem>> EstablishmentItemsAsync() => (await _workforce.GetEstablishmentsAsync(DepartmentId)).OrderBy(e => e.Code).Select(e => new SelectListItem($"{e.Code} — {e.Name}", e.WorkforceEstablishmentId)).ToList();
		private async Task<List<SelectListItem>> UnitItemsAsync() => (await _units.GetUnitsForDepartmentAsync(DepartmentId) ?? new List<Unit>()).OrderBy(u => u.Name).Select(u => new SelectListItem(u.Name, u.UnitId.ToString())).ToList();

		private async Task<string> WorkerNameAsync(WorkforceWorker worker)
		{
			if (worker == null) return null;
			if (!string.IsNullOrWhiteSpace(worker.DisplayName)) return worker.DisplayName;
			var names = await MemberNamesAsync();
			return worker.UserId != null && names.TryGetValue(worker.UserId, out var n) ? n : worker.WorkforceWorkerId;
		}

		#endregion

		#region Dashboard

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			var view = Page(new WorkforceDashboardView { ReportingYear = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today.Year - 1 });
			if (_workforceEnabled && CanView)
			{
				view.Employer = await _workforce.GetEmployerProfileAsync(DepartmentId);
				view.EstablishmentCount = (await _workforce.GetEstablishmentsAsync(DepartmentId)).Count;
				view.WorkerCount = (await _workforce.GetWorkersAsync(DepartmentId)).Count;
				view.EmploymentCount = (await _workforce.GetEmploymentsAsync(DepartmentId)).Count;
			}
			if (_workforceEnabled && CanViewInternalCosts)
			{
				view.ResourceProfileCount = (await _costing.GetResourceProfilesAsync(DepartmentId)).Count;
				view.CostRunCount = (await _costing.GetRunsAsync(DepartmentId, 0, 500)).Count;
			}
			if (_payDataEnabled && CanViewPayData)
			{
				view.Readiness = await _reporting.GetReadinessAsync(DepartmentId, view.ReportingYear);
				view.Completeness = await _demographics.GetCompletenessAsync(DepartmentId, DateTime.UtcNow);
			}
			return View(view);
		}

		#endregion

		#region Employer, establishments, contractors

		[HttpGet]
		public async Task<IActionResult> Employer(string affiliate = null)
		{
			if (!CanView) return Unauthorized();
			var view = Page(new WorkforceEmployerView { Employer = await _workforce.GetEmployerProfileAsync(DepartmentId) ?? new WorkforceEmployerProfile { DepartmentId = DepartmentId }, Affiliates = await _workforce.GetAffiliatesAsync(DepartmentId) });
			if (!string.IsNullOrWhiteSpace(affiliate)) view.EditingAffiliate = view.Affiliates.FirstOrDefault(a => a.WorkforceAffiliatedEntityId == affiliate);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveEmployer(WorkforceEmployerProfile input) => GuardedAsync(async () =>
		{
			if (!CanManage) return Unauthorized();
			input.DepartmentId = DepartmentId;
			await _workforce.SaveEmployerProfileAsync(input, UserId, Ip, Agent);
			return Saved("Employer");
		}, "Employer");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveAffiliate(WorkforceAffiliatedEntity input) => GuardedAsync(async () =>
		{
			if (!CanManage) return Unauthorized();
			input.DepartmentId = DepartmentId;
			await _workforce.SaveAffiliateAsync(input, UserId, Ip, Agent);
			return Saved("Employer");
		}, "Employer");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> DeleteAffiliate(string id) => GuardedAsync(async () =>
		{
			if (!CanManage) return Unauthorized();
			await _workforce.DeleteAffiliateAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("Employer");
		}, "Employer");

		[HttpGet]
		public async Task<IActionResult> Establishments(string edit = null)
		{
			if (!CanView) return Unauthorized();
			var view = Page(new WorkforceEstablishmentsView { Establishments = await _workforce.GetEstablishmentsAsync(DepartmentId) });
			view.Affiliates = (await _workforce.GetAffiliatesAsync(DepartmentId)).Select(a => new SelectListItem(a.LegalName, a.WorkforceAffiliatedEntityId)).ToList();
			if (edit == "new") view.Editing = new WorkforceEstablishment { DepartmentId = DepartmentId, StateCode = "CA" };
			else if (!string.IsNullOrWhiteSpace(edit)) view.Editing = await _workforce.GetEstablishmentAsync(edit, DepartmentId);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveEstablishment(WorkforceEstablishment input) => GuardedAsync(async () =>
		{
			if (!CanManage) return Unauthorized();
			input.DepartmentId = DepartmentId;
			await _workforce.SaveEstablishmentAsync(input, UserId, Ip, Agent);
			return Saved("Establishments");
		}, "Establishments");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> DeleteEstablishment(string id) => GuardedAsync(async () =>
		{
			if (!CanManage) return Unauthorized();
			await _workforce.DeleteEstablishmentAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("Establishments");
		}, "Establishments");

		[HttpGet]
		public async Task<IActionResult> Contractors(string edit = null)
		{
			if (!CanView) return Unauthorized();
			var view = Page(new WorkforceContractorsView { Contractors = await _workforce.GetLaborContractorsAsync(DepartmentId) });
			if (edit == "new") view.Editing = new WorkforceLaborContractor { DepartmentId = DepartmentId };
			else if (!string.IsNullOrWhiteSpace(edit)) view.Editing = view.Contractors.FirstOrDefault(c => c.WorkforceLaborContractorId == edit);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveContractor(WorkforceLaborContractor input) => GuardedAsync(async () =>
		{
			if (!CanManage) return Unauthorized();
			input.DepartmentId = DepartmentId;
			await _workforce.SaveLaborContractorAsync(input, UserId, Ip, Agent);
			return Saved("Contractors");
		}, "Contractors");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> DeleteContractor(string id) => GuardedAsync(async () =>
		{
			if (!CanManage) return Unauthorized();
			await _workforce.DeleteLaborContractorAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("Contractors");
		}, "Contractors");

		#endregion

		#region Workers, employments, assignments

		[HttpGet]
		public async Task<IActionResult> Workers()
		{
			if (!CanView) return Unauthorized();
			var view = Page(new WorkforceWorkersView { Workers = await _workforce.GetWorkersAsync(DepartmentId) });
			var employments = await _workforce.GetEmploymentsAsync(DepartmentId);
			view.EmploymentCounts = employments.GroupBy(e => e.WorkforceWorkerId).ToDictionary(g => g.Key, g => g.Count());
			var names = await MemberNamesAsync();
			var linked = new HashSet<string>(view.Workers.Where(w => w.UserId != null).Select(w => w.UserId), StringComparer.OrdinalIgnoreCase);
			view.Members = names.Where(n => !linked.Contains(n.Key)).OrderBy(n => n.Value).Select(n => new SelectListItem(n.Value, n.Key)).ToList();
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> AddWorker(string userId, string externalWorkerKey, string displayLabel) => GuardedAsync(async () =>
		{
			if (!CanManage) return Unauthorized();
			WorkforceWorker worker;
			if (!string.IsNullOrWhiteSpace(userId)) worker = await _workforce.GetOrCreateWorkerForUserAsync(DepartmentId, userId, UserId);
			else worker = await _workforce.SaveWorkerAsync(new WorkforceWorker { DepartmentId = DepartmentId, ExternalWorkerKey = externalWorkerKey, DisplayLabel = displayLabel }, UserId, Ip, Agent);
			return Saved("Worker", new { id = worker.WorkforceWorkerId });
		}, "Workers");

		[HttpGet]
		public async Task<IActionResult> Worker(string id, string employment = null, string assignment = null)
		{
			if (!CanView) return Unauthorized();
			var worker = await _workforce.GetWorkerAsync(id, DepartmentId);
			if (worker == null) return NotFound();
			var view = Page(new WorkforceWorkerView { Worker = worker, Employments = await _workforce.GetEmploymentsForWorkerAsync(worker.WorkforceWorkerId, DepartmentId) });
			view.Establishments = await EstablishmentItemsAsync();
			view.Contractors = (await _workforce.GetLaborContractorsAsync(DepartmentId)).Select(c => new SelectListItem(c.LegalName, c.WorkforceLaborContractorId)).ToList();
			view.Affiliates = (await _workforce.GetAffiliatesAsync(DepartmentId)).Select(a => new SelectListItem(a.LegalName, a.WorkforceAffiliatedEntityId)).ToList();
			view.Roles = await RoleItemsAsync();
			if (employment == "new") view.EditingEmployment = new WorkforceEmployment { DepartmentId = DepartmentId, WorkforceWorkerId = worker.WorkforceWorkerId, StartOn = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today };
			else if (!string.IsNullOrWhiteSpace(employment)) view.EditingEmployment = view.Employments.FirstOrDefault(e => e.WorkforceEmploymentId == employment);
			if (!string.IsNullOrWhiteSpace(assignment))
			{
				var parts = assignment.Split(':');
				var owner = view.Employments.FirstOrDefault(e => e.WorkforceEmploymentId == parts[0]);
				if (owner != null) view.EditingAssignment = parts.Length > 1 && parts[1] != "new" ? owner.Assignments.FirstOrDefault(a => a.WorkforceJobAssignmentId == parts[1]) : new WorkforceJobAssignment { DepartmentId = DepartmentId, WorkforceEmploymentId = owner.WorkforceEmploymentId, EffectiveOn = owner.StartOn, WorkforceEstablishmentId = owner.DefaultEstablishmentId, WorkCountry = "US", WorkSubdivision = "CA" };
			}
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveWorker(WorkforceWorker input) => GuardedAsync(async () =>
		{
			if (!CanManage) return Unauthorized();
			input.DepartmentId = DepartmentId;
			var saved = await _workforce.SaveWorkerAsync(input, UserId, Ip, Agent);
			return Saved("Worker", new { id = saved.WorkforceWorkerId });
		}, "Workers");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveEmployment(WorkforceEmployment input) => GuardedAsync(async () =>
		{
			if (!CanManage) return Unauthorized();
			input.DepartmentId = DepartmentId;
			var saved = await _workforce.SaveEmploymentAsync(input, UserId, Ip, Agent);
			return Saved("Worker", new { id = saved.WorkforceWorkerId });
		}, "Worker", new { id = input?.WorkforceWorkerId });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> DeleteEmployment(string id, string workerId) => GuardedAsync(async () =>
		{
			if (!CanManage) return Unauthorized();
			await _workforce.DeleteEmploymentAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("Worker", new { id = workerId });
		}, "Worker", new { id = workerId });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveJobAssignment(WorkforceJobAssignment input, string workerId) => GuardedAsync(async () =>
		{
			if (!CanManage) return Unauthorized();
			input.DepartmentId = DepartmentId;
			await _workforce.SaveJobAssignmentAsync(input, UserId, Ip, Agent);
			return Saved("Worker", new { id = workerId });
		}, "Worker", new { id = workerId });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> DeleteJobAssignment(string id, string workerId) => GuardedAsync(async () =>
		{
			if (!CanManage) return Unauthorized();
			await _workforce.DeleteJobAssignmentAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("Worker", new { id = workerId });
		}, "Worker", new { id = workerId });

		#endregion

		#region Compensation

		[HttpGet]
		public async Task<IActionResult> Compensation(string employmentId = null)
		{
			if (!CanViewCompensation) return Unauthorized();
			var view = Page(new WorkforceCompensationView { Defaults = await _compensation.GetDefaultProfilesAsync(DepartmentId), EmploymentId = employmentId });
			view.RoleNames = (await _roles.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>()).ToDictionary(r => r.PersonnelRoleId, r => r.Name);
			if (!string.IsNullOrWhiteSpace(employmentId))
			{
				view.Employee = await _compensation.GetProfilesForEmploymentAsync(employmentId, DepartmentId);
				var employment = await _workforce.GetEmploymentAsync(employmentId, DepartmentId);
				view.WorkerName = employment == null ? null : await WorkerNameAsync(await _workforce.GetWorkerAsync(employment.WorkforceWorkerId, DepartmentId));
			}
			return View(view);
		}

		[HttpGet]
		public async Task<IActionResult> CompensationProfile(string id = null, string employmentId = null, int scope = 2)
		{
			if (!CanViewCompensation) return Unauthorized();
			EmployeeCompensationProfile profile;
			if (string.IsNullOrWhiteSpace(id)) profile = new EmployeeCompensationProfile { DepartmentId = DepartmentId, Scope = string.IsNullOrWhiteSpace(employmentId) ? scope : (int)CompensationScopes.Employee, WorkforceEmploymentId = employmentId, EffectiveOn = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today, StandardHoursPerDay = 8, StandardHoursPerWeek = 40 };
			else { profile = await _compensation.GetProfileAsync(id, DepartmentId); if (profile == null) return NotFound(); }
			var view = Page(new WorkforceCompensationProfileView { Profile = profile, PayComponentsJson = JsonConvert.SerializeObject(profile.PayComponents.Select(c => new { c.EmployeePayComponentId, c.Category, c.Name, c.Basis, Amount = c.Amount, c.EligiblePayCodesCsv, c.PaidForEachOvertimeHour, c.EffectiveOn, c.ExpiresOn, c.SourceAgreement }), ScriptJson), CostComponentsJson = JsonConvert.SerializeObject(profile.CostComponents.Select(c => new { c.EmployeeCostComponentId, c.Category, c.Name, c.Basis, RateAmount = c.RateAmount, c.Cap, c.EligiblePayCodesCsv, c.EffectiveOn, c.ExpiresOn, c.Source }), ScriptJson) });
			view.Roles = await RoleItemsAsync();
			if (!string.IsNullOrWhiteSpace(profile.WorkforceEmploymentId))
			{
				var employment = await _workforce.GetEmploymentAsync(profile.WorkforceEmploymentId, DepartmentId);
				view.WorkerName = employment == null ? null : await WorkerNameAsync(await _workforce.GetWorkerAsync(employment.WorkforceWorkerId, DepartmentId));
			}
			if (!string.IsNullOrWhiteSpace(id) && !WorkforceProtectionSeamHelper.IsUnavailable(profile.BaseAmount))
				view.Preview = Resgrid.Services.Workforce.FieldCostCalculator.CalculateLabor(new LaborCostInput { Work = new LaborWorkQuantity { Hours = 8, PayCode = (int)PayCodes.Regular }, Profile = profile, AsOf = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today, Currency = profile.Currency });
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveCompensationProfile(EmployeeCompensationProfile input, string payComponentsJson, string costComponentsJson) => GuardedAsync(async () =>
		{
			if (!CanManageCompensation) return Unauthorized();
			input.DepartmentId = DepartmentId;
			var pay = string.IsNullOrWhiteSpace(payComponentsJson) ? new List<EmployeePayComponent>() : JsonConvert.DeserializeObject<List<EmployeePayComponent>>(payComponentsJson) ?? new List<EmployeePayComponent>();
			var cost = string.IsNullOrWhiteSpace(costComponentsJson) ? new List<EmployeeCostComponent>() : JsonConvert.DeserializeObject<List<EmployeeCostComponent>>(costComponentsJson) ?? new List<EmployeeCostComponent>();
			try
			{
				if (!string.IsNullOrWhiteSpace(input.RateMultipliersJson) && !WorkforceProtectionSeamHelper.IsUnavailable(input.RateMultipliersJson))
				{
					var multipliers = Resgrid.Framework.JsonInput.Read<Dictionary<string, decimal>>(input.RateMultipliersJson, nameof(input.RateMultipliersJson));
					foreach (var multiplier in multipliers)
					{
						if (!Enum.GetNames<PayCodes>().Contains(multiplier.Key, StringComparer.OrdinalIgnoreCase)) throw new Resgrid.Framework.JsonInputException("RateMultipliersJson: $." + multiplier.Key + ": use a pay code from " + string.Join(", ", Enum.GetNames<PayCodes>()) + ".");
						if (multiplier.Value < 0) throw new Resgrid.Framework.JsonInputException("RateMultipliersJson: $." + multiplier.Key + ": use zero or a positive multiplier.");
					}
				}
			}
			catch (Resgrid.Framework.JsonInputException ex)
			{
				if (IsAjax()) return BadRequest(new { message = ex.Message });
				var view = Page(new WorkforceCompensationProfileView { Profile = input, Roles = await RoleItemsAsync(), PayComponentsJson = JsonConvert.SerializeObject(pay, ScriptJson), CostComponentsJson = JsonConvert.SerializeObject(cost, ScriptJson) });
				view.Message = ex.Message;
				Response.StatusCode = 400;
				return View("CompensationProfile", view);
			}
			var saved = await _compensation.SaveProfileAsync(input, UserId, Ip, Agent);
			await _compensation.SaveComponentsAsync(saved.EmployeeCompensationProfileId, DepartmentId, pay, cost, UserId, Ip, Agent);
			return Saved("CompensationProfile", new { id = saved.EmployeeCompensationProfileId });
		}, "Compensation");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> ApproveCompensationProfile(string id) => GuardedAsync(async () =>
		{
			if (!CanManageCompensation) return Unauthorized();
			await _compensation.ApproveProfileAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("CompensationProfile", new { id });
		}, "Compensation");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> DeleteCompensationProfile(string id, string employmentId) => GuardedAsync(async () =>
		{
			if (!CanManageCompensation) return Unauthorized();
			await _compensation.DeleteProfileAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("Compensation", new { employmentId });
		}, "Compensation");

		#endregion

		#region Annual facts and work entries

		[HttpGet]
		public async Task<IActionResult> AnnualFacts(int? year = null, int type = 0, string edit = null)
		{
			if (!CanViewCompensation) return Unauthorized();
			var view = Page(new WorkforceAnnualFactsView { ReportingYear = year ?? Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today.Year - 1, ReportType = (PayDataReportTypes)type });
			view.Facts = await _workforce.GetAnnualPayFactsAsync(DepartmentId, view.ReportingYear, view.ReportType);
			await LabelEmploymentsAsync(view);
			if (edit == "new") view.Editing = new WorkforceAnnualPayFact { DepartmentId = DepartmentId, ReportingYear = view.ReportingYear, ReportType = type };
			else if (!string.IsNullOrWhiteSpace(edit)) view.Editing = view.Facts.FirstOrDefault(f => f.WorkforceAnnualPayFactId == edit);
			if (TempData["WorkforceImport"] is string import) { view.ImportResult = JsonConvert.DeserializeObject<WorkforceImportResult>(import); view.Csv = TempData["WorkforceImportCsv"] as string; }
			return View(view);
		}

		private async Task LabelEmploymentsAsync(WorkforceAnnualFactsView view)
		{
			var employments = await _workforce.GetEmploymentsAsync(DepartmentId);
			var workers = (await _workforce.GetWorkersAsync(DepartmentId)).ToDictionary(w => w.WorkforceWorkerId, w => w.DisplayName ?? w.WorkforceWorkerId);
			foreach (var employment in employments)
			{
				var label = $"{(workers.TryGetValue(employment.WorkforceWorkerId, out var n) ? n : employment.WorkforceWorkerId)} ({employment.StartOn:yyyy-MM-dd}–{employment.EndOn?.ToString("yyyy-MM-dd") ?? "…"})";
				view.EmploymentLabels[employment.WorkforceEmploymentId] = label;
				view.Employments.Add(new SelectListItem(label, employment.WorkforceEmploymentId));
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveAnnualFact(WorkforceAnnualPayFact input, decimal? w2Box5, decimal? w2Box1, decimal? clientAllocatedEarnings) => GuardedAsync(async () =>
		{
			if (!CanManageCompensation) return Unauthorized();
			input.DepartmentId = DepartmentId;
			input.W2Box5Value = w2Box5; input.W2Box1Value = w2Box1; input.ClientAllocatedEarningsValue = clientAllocatedEarnings;
			await _workforce.SaveAnnualPayFactAsync(input, UserId, Ip, Agent);
			return Saved("AnnualFacts", new { year = input.ReportingYear, type = input.ReportType });
		}, "AnnualFacts");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> ImportAnnualFacts(string csv, bool commit, int year, int type) => GuardedAsync(async () =>
		{
			if (!CanManageCompensation) return Unauthorized();
			var result = await _workforce.ImportAnnualPayFactsAsync(DepartmentId, csv, !commit, UserId, Ip, Agent);
			TempData["WorkforceImport"] = JsonConvert.SerializeObject(result);
			if (!commit || result.HasErrors) TempData["WorkforceImportCsv"] = csv;
			return Saved("AnnualFacts", new { year, type });
		}, "AnnualFacts");

		[HttpGet]
		public async Task<IActionResult> WorkEntries(DateTime? from = null, DateTime? to = null, string edit = null)
		{
			if (!CanViewCompensation) return Unauthorized();
			var view = Page(new WorkforceWorkEntriesView { From = (from ?? Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today.AddDays(-30)).Date, To = (to ?? Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today).Date });
			view.Entries = await _workforce.GetWorkEntriesAsync(DepartmentId, view.From, view.To);
			var workers = await _workforce.GetWorkersAsync(DepartmentId);
			view.WorkerNames = workers.ToDictionary(w => w.WorkforceWorkerId, w => w.DisplayName ?? w.WorkforceWorkerId);
			view.Workers = workers.OrderBy(w => w.DisplayName).Select(w => new SelectListItem(w.DisplayName ?? w.WorkforceWorkerId, w.WorkforceWorkerId)).ToList();
			view.Establishments = await EstablishmentItemsAsync();
			if (edit == "new") view.Editing = new WorkforceWorkEntry { DepartmentId = DepartmentId, WorkDate = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today, WorkCountry = "US", WorkSubdivision = "CA" };
			else if (!string.IsNullOrWhiteSpace(edit)) view.Editing = view.Entries.FirstOrDefault(e => e.WorkforceWorkEntryId == edit);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveWorkEntry(WorkforceWorkEntry input, decimal? approvedPayrollCost) => GuardedAsync(async () =>
		{
			if (!CanManageCompensation) return Unauthorized();
			input.DepartmentId = DepartmentId;
			if (approvedPayrollCost.HasValue) input.ApprovedPayrollCostValue = approvedPayrollCost;
			await _workforce.SaveWorkEntryAsync(input, UserId, Ip, Agent);
			return Saved("WorkEntries", new { from = input.WorkDate.AddDays(-30).ToString("yyyy-MM-dd"), to = input.WorkDate.ToString("yyyy-MM-dd") });
		}, "WorkEntries");

		#endregion

		#region Resource costs and usage

		[HttpGet]
		public async Task<IActionResult> ResourceCosts(string edit = null)
		{
			if (!CanViewInternalCosts) return Unauthorized();
			var view = Page(new WorkforceResourceCostsView { Profiles = await _costing.GetResourceProfilesAsync(DepartmentId), Units = await UnitItemsAsync() });
			if (edit == "new") view.Editing = new ResourceCostProfile { DepartmentId = DepartmentId, EffectiveOn = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today };
			else if (!string.IsNullOrWhiteSpace(edit)) view.Editing = await _costing.GetResourceProfileAsync(edit, DepartmentId);
			view.ComponentsJson = JsonConvert.SerializeObject((view.Editing?.Components ?? new List<ResourceCostComponent>()).Select(c => new { c.ResourceCostComponentId, c.Category, c.Basis, c.Rate, c.ConsumptionQuantity, c.ConsumptionUnit, c.UnitPrice, c.Source, c.SourceWindowStart, c.SourceWindowEnd, c.SourceMeterStart, c.SourceMeterEnd, c.IsApproved, c.EffectiveOn, c.ExpiresOn }), ScriptJson);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveResourceCost(ResourceCostProfile input, string componentsJson) => GuardedAsync(async () =>
		{
			if (!CanViewInternalCosts || !CanManage) return Unauthorized();
			input.DepartmentId = DepartmentId;
			var saved = await _costing.SaveResourceProfileAsync(input, UserId, Ip, Agent);
			var components = string.IsNullOrWhiteSpace(componentsJson) ? new List<ResourceCostComponent>() : JsonConvert.DeserializeObject<List<ResourceCostComponent>>(componentsJson) ?? new List<ResourceCostComponent>();
			await _costing.SaveResourceComponentsAsync(saved.ResourceCostProfileId, DepartmentId, components, UserId, Ip, Agent);
			return Saved("ResourceCosts", new { edit = saved.ResourceCostProfileId });
		}, "ResourceCosts");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> DeleteResourceCost(string id) => GuardedAsync(async () =>
		{
			if (!CanViewInternalCosts || !CanManage) return Unauthorized();
			await _costing.DeleteResourceProfileAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("ResourceCosts");
		}, "ResourceCosts");

		[HttpGet]
		public async Task<IActionResult> Usage(string deploymentId = null, int? callId = null, string edit = null)
		{
			if (!CanViewInternalCosts) return Unauthorized();
			if (string.IsNullOrWhiteSpace(deploymentId) && !callId.HasValue) return RedirectToAction("CostRuns");
			var view = Page(new WorkforceUsageView { DeploymentId = deploymentId, CallId = callId, Units = await UnitItemsAsync() });
			view.UnitNames = view.Units.ToDictionary(u => int.Parse(u.Value), u => u.Text);
			if (!string.IsNullOrWhiteSpace(deploymentId)) { view.ContextLabel = (await _deployments.GetDeploymentByIdAsync(deploymentId, DepartmentId))?.Name ?? deploymentId; view.Entries = await _costing.GetUsageForDeploymentAsync(deploymentId, DepartmentId); }
			else { view.ContextLabel = (await _calls.GetCallByIdAsync(callId.Value))?.Name ?? $"#{callId}"; view.Entries = await _costing.GetUsageForCallAsync(callId.Value, DepartmentId); }
			if (edit == "new") view.Editing = new ResourceUsageEntry { DepartmentId = DepartmentId, DeploymentId = deploymentId, CallId = callId, UsageDate = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today, Phase = (int)UsagePhases.Incident };
			else if (!string.IsNullOrWhiteSpace(edit)) view.Editing = view.Entries.FirstOrDefault(e => e.ResourceUsageEntryId == edit);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveUsage(ResourceUsageEntry input) => GuardedAsync(async () =>
		{
			if (!CanViewInternalCosts) return Unauthorized();
			input.DepartmentId = DepartmentId;
			await _costing.SaveUsageEntryAsync(input, UserId, Ip, Agent);
			return Saved("Usage", new { deploymentId = input.DeploymentId, callId = input.CallId });
		}, "CostRuns");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> DeleteUsage(string id, string deploymentId, int? callId) => GuardedAsync(async () =>
		{
			if (!CanViewInternalCosts) return Unauthorized();
			await _costing.DeleteUsageEntryAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("Usage", new { deploymentId, callId });
		}, "CostRuns");

		#endregion

		#region Cost runs

		[HttpGet]
		public async Task<IActionResult> CostRuns()
		{
			if (!CanViewInternalCosts) return Unauthorized();
			var view = Page(new WorkforceCostRunsView { Runs = await _costing.GetRunsAsync(DepartmentId, 0, 200) });
			var deployments = await _deployments.GetDeploymentsForDepartmentAsync(DepartmentId, false, 0, 200) ?? new List<Deployment>();
			view.Deployments = deployments.OrderByDescending(d => d.StartOn).Select(d => new SelectListItem(d.Name, d.DeploymentId)).ToList();
			var bids = await _bids.GetBidsForDepartmentAsync(DepartmentId, null, 0, 200) ?? new List<Bid>();
			view.Bids = bids.OrderByDescending(b => b.BidNumber).Select(b => new SelectListItem($"#{b.BidNumber} {b.Title}", b.BidId)).ToList();
			foreach (var d in deployments) view.ContextLabels["D:" + d.DeploymentId] = d.Name;
			foreach (var b in bids) view.ContextLabels["B:" + b.BidId] = $"#{b.BidNumber} {b.Title}";
			return View(view);
		}

		[HttpGet]
		public async Task<IActionResult> CostRun(string id)
		{
			if (!CanViewInternalCosts) return Unauthorized();
			var run = await _costing.GetRunAsync(id, DepartmentId);
			if (run == null) return NotFound();
			var view = Page(new WorkforceCostRunView { Run = run, Summary = await _costing.GetFieldCostSummaryAsync(id, DepartmentId) });
			if (!string.IsNullOrWhiteSpace(run.DeploymentId)) { view.ContextLabel = (await _deployments.GetDeploymentByIdAsync(run.DeploymentId, DepartmentId))?.Name; view.Comparison = await _costing.CompareEstimateToActualAsync(run.DeploymentId, DepartmentId); }
			else if (!string.IsNullOrWhiteSpace(run.BidId)) { var bid = await _bids.GetBidByIdAsync(run.BidId, DepartmentId); view.ContextLabel = bid == null ? run.BidId : $"#{bid.BidNumber} {bid.Title}"; }
			else if (run.CallId.HasValue) view.ContextLabel = (await _calls.GetCallByIdAsync(run.CallId.Value))?.Name ?? $"#{run.CallId}";
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> RunBidEstimate(string bidId) => GuardedAsync(async () =>
		{
			if (!CanViewInternalCosts) return Unauthorized();
			var run = await _costing.EstimateBidCostAsync(bidId, DepartmentId, UserId, Ip, Agent);
			return Saved("CostRun", new { id = run.FieldCostRunId });
		}, "CostRuns");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> RunCallCost(int callId) => GuardedAsync(async () =>
		{
			if (!CanViewInternalCosts) return Unauthorized();
			var run = await _costing.CalculateCallCostAsync(callId, DepartmentId, UserId, Ip, Agent);
			return Saved("CostRun", new { id = run.FieldCostRunId });
		}, "CostRuns");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> RunDeploymentCost(string deploymentId, DateTime? throughDate, int revenueSource) => GuardedAsync(async () =>
		{
			if (!CanViewInternalCosts) return Unauthorized();
			var run = await _costing.CalculateDeploymentCostAsync(deploymentId, DepartmentId, throughDate, Enum.IsDefined(typeof(RevenueSources), revenueSource) ? (RevenueSources)revenueSource : RevenueSources.None, UserId, Ip, Agent);
			return Saved("CostRun", new { id = run.FieldCostRunId });
		}, "CostRuns");

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> FreezeCostRun(string id) => GuardedAsync(async () =>
		{
			if (!CanViewInternalCosts) return Unauthorized();
			await _costing.FreezeCostRunAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("CostRun", new { id });
		}, "CostRun", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> DeleteCostRun(string id) => GuardedAsync(async () =>
		{
			if (!CanViewInternalCosts) return Unauthorized();
			await _costing.DeleteRunAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("CostRuns");
		}, "CostRun", new { id });

		#endregion

		#region Pay data reporting

		[HttpGet]
		public async Task<IActionResult> PayData(int? year = null)
		{
			if (!CanViewPayData) return Unauthorized();
			var view = Page(new WorkforcePayDataView { ReportingYear = year ?? Resgrid.Web.Helpers.DepartmentTime.From(ViewData).Today.Year - 1 });
			view.Runs = await _reporting.GetRunsAsync(DepartmentId, null);
			view.Readiness = await _reporting.GetReadinessAsync(DepartmentId, view.ReportingYear);
			view.Completeness = await _demographics.GetCompletenessAsync(DepartmentId, new DateTime(view.ReportingYear, 12, 31));
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> CreatePayDataRun(int reportingYear, int reportType, DateTime snapshotStart, DateTime snapshotEnd) => GuardedAsync(async () =>
		{
			if (!CanManagePayData) return Unauthorized();
			var run = await _reporting.CreateRunAsync(DepartmentId, reportingYear, (PayDataReportTypes)reportType, snapshotStart, snapshotEnd, UserId, Ip, Agent);
			return Saved("PayDataRun", new { id = run.PayDataReportRunId });
		}, "PayData");

		[HttpGet]
		public async Task<IActionResult> PayDataRun(string id, string tab = "snapshots")
		{
			if (!CanViewPayData) return Unauthorized();
			var run = await _reporting.GetRunAsync(id, DepartmentId);
			if (run == null) return NotFound();
			var view = Page(new WorkforcePayDataRunView { Run = run, Profile = CaPayDataSchemaProfile.Get(run.SchemaProfileCode) ?? CaPayDataSchemaProfile.Current, Tab = tab });
			view.Snapshots = await _reporting.GetSnapshotsAsync(id, DepartmentId);
			view.Rows = await _reporting.GetRowsAsync(id, DepartmentId);
			view.Artifacts = await _reporting.GetArtifactsAsync(id, DepartmentId);
			view.EstablishmentLabels = (await _workforce.GetEstablishmentsAsync(DepartmentId)).ToDictionary(e => e.WorkforceEstablishmentId, e => $"{e.Code} — {e.Name}");
			if (!string.IsNullOrWhiteSpace(run.ValidationSummaryJson)) { try { view.Validation = JsonConvert.DeserializeObject<PayDataValidationResult>(run.ValidationSummaryJson); } catch (JsonException) { view.Validation = null; } }
			if (TempData["WorkforceValidation"] is string validation) view.Validation = JsonConvert.DeserializeObject<PayDataValidationResult>(validation);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> BuildSnapshots(string id) => GuardedAsync(async () =>
		{
			if (!CanManagePayData) return Unauthorized();
			await _reporting.BuildEmployeeSnapshotsAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("PayDataRun", new { id, tab = "snapshots" });
		}, "PayDataRun", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> OverrideSnapshot(string id, string snapshotId, bool include, string jobCategoryCode, int? workMode, string reason) => GuardedAsync(async () =>
		{
			if (!CanManagePayData) return Unauthorized();
			await _reporting.OverrideSnapshotAsync(id, snapshotId, DepartmentId, include, jobCategoryCode, workMode, reason, UserId, Ip, Agent);
			return Saved("PayDataRun", new { id, tab = "snapshots" });
		}, "PayDataRun", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> AggregateRows(string id) => GuardedAsync(async () =>
		{
			if (!CanManagePayData) return Unauthorized();
			await _reporting.AggregateRowsAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("PayDataRun", new { id, tab = "rows" });
		}, "PayDataRun", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveRemarks(string id, string runRemarks) => GuardedAsync(async () =>
		{
			if (!CanManagePayData) return Unauthorized();
			await _reporting.SaveRemarksAsync(id, DepartmentId, runRemarks, UserId, Ip, Agent);
			return Saved("PayDataRun", new { id, tab = "validation" });
		}, "PayDataRun", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> ValidateRun(string id) => GuardedAsync(async () =>
		{
			if (!CanManagePayData) return Unauthorized();
			var result = await _reporting.ValidateRunAsync(id, DepartmentId, UserId, Ip, Agent);
			TempData["WorkforceValidation"] = JsonConvert.SerializeObject(result);
			return Saved("PayDataRun", new { id, tab = "validation" });
		}, "PayDataRun", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> FreezeAndExport(string id) => GuardedAsync(async () =>
		{
			if (!CanExportPayData) return Unauthorized();
			await _reporting.FreezeAndExportAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("PayDataRun", new { id, tab = "artifacts" });
		}, "PayDataRun", new { id });

		[HttpGet]
		public Task<IActionResult> DownloadArtifact(string id, string runId) => GuardedAsync(async () =>
		{
			if (!CanExportPayData) return Unauthorized();
			var artifact = await _reporting.DownloadArtifactAsync(id, DepartmentId, UserId, Ip, Agent);
			Response.Headers["Cache-Control"] = "no-store";
			return File(artifact.Data, artifact.Format == (int)PayDataExportFormats.Xlsx ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/csv", artifact.FileName);
		}, "PayDataRun", new { id = runId });

		[HttpGet]
		public async Task<IActionResult> Worksheet(string id)
		{
			if (!CanExportPayData && !CanManagePayData) return Unauthorized();
			var worksheet = await _reporting.GetWorksheetAsync(id, DepartmentId);
			return View(Page(new WorkforceWorksheetView { Worksheet = worksheet }));
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> MarkCertified(string id, string certificationReference) => GuardedAsync(async () =>
		{
			if (!CanManagePayData) return Unauthorized();
			await _reporting.MarkCertifiedExternallyAsync(id, DepartmentId, certificationReference, UserId, Ip, Agent);
			return Saved("PayDataRun", new { id, tab = "artifacts" });
		}, "PayDataRun", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> CreateCorrection(string id) => GuardedAsync(async () =>
		{
			if (!CanManagePayData) return Unauthorized();
			var correction = await _reporting.CreateCorrectionAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("PayDataRun", new { id = correction.PayDataReportRunId });
		}, "PayDataRun", new { id });

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> VoidRun(string id) => GuardedAsync(async () =>
		{
			if (!CanManagePayData) return Unauthorized();
			await _reporting.VoidRunAsync(id, DepartmentId, UserId, Ip, Agent);
			return Saved("PayData");
		}, "PayDataRun", new { id });

		#endregion

		#region Demographics

		[HttpGet]
		public async Task<IActionResult> MyDemographics()
		{
			var view = Page(new WorkforceDemographicsView { Response = await _demographics.GetOwnAsync(DepartmentId, UserId) ?? new PayDataReportingDemographic(), IsOwn = true });
			return View("Demographics", view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveMyDemographics(WorkforceDemographicInput input) => GuardedAsync(async () =>
		{
			await _demographics.SaveOwnAsync(DepartmentId, UserId, ToResponse(input), Ip, Agent);
			return Saved("MyDemographics");
		}, "MyDemographics");

		[HttpGet]
		public async Task<IActionResult> WorkerDemographics(string workerId)
		{
			if (!CanManagePayData) return Unauthorized();
			var worker = await _workforce.GetWorkerAsync(workerId, DepartmentId);
			if (worker == null) return NotFound();
			var view = Page(new WorkforceDemographicsView { Response = await _demographics.GetForWorkerAsync(DepartmentId, workerId, DateTime.UtcNow) ?? new PayDataReportingDemographic { CollectionSource = (int)DemographicCollectionSources.EmploymentRecord }, IsOwn = false, WorkerId = workerId, WorkerName = await WorkerNameAsync(worker) });
			return View("Demographics", view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveWorkerDemographics(string workerId, WorkforceDemographicInput input) => GuardedAsync(async () =>
		{
			if (!CanManagePayData) return Unauthorized();
			await _demographics.SaveForWorkerAsync(DepartmentId, workerId, ToResponse(input), input.Reason, UserId, Ip, Agent);
			return Saved("WorkerDemographics", new { workerId });
		}, "WorkerDemographics", new { workerId });

		private static PayDataReportingDemographic ToResponse(WorkforceDemographicInput input) => new PayDataReportingDemographic
		{
			HispanicLatino = input.HispanicLatino, RaceEthnicityCodes = input.RaceCodes == null ? null : string.Join(",", input.RaceCodes), SexCode = input.SexCode,
			DeclinedRaceEthnicity = input.DeclinedRaceEthnicity, DeclinedSex = input.DeclinedSex, CollectionSource = input.CollectionSource
		};

		#endregion
	}

	internal static class WorkforceProtectionSeamHelper
	{
		public static bool IsUnavailable(string value) => Resgrid.Services.Workforce.WorkforceProtectionSeam.IsUnavailable(value);
	}
}
