using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.ContractorBilling;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Contractor rate schedules (Workforce &amp; Business Operations plan, Phase C6): the schedule list, the editor
	/// (entry grid grouped by type, band editor with threshold columns and multiplier prefill, premium editor, policy
	/// panel) and JSON import/export. Needs the Invoicing.ContractorBilling entitlement; Invoicing_View reads,
	/// Invoicing_Update (admins by default) edits.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public sealed class RateSchedulesController : SecureBaseController
	{
		private static readonly string[] Currencies = { "USD", "CAD", "EUR", "GBP", "AUD", "NZD", "MXN", "CHF", "SEK", "NOK", "DKK", "PLN" };

		private readonly IRateScheduleService _rateSchedules;
		private readonly IBusinessOperationsAccessService _access;
		private readonly IUnitsService _units;
		private readonly ICertificationService _certifications;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.ContractorBilling.ContractorBilling> _strings;

		public RateSchedulesController(IRateScheduleService rateSchedules, IBusinessOperationsAccessService access, IUnitsService units, ICertificationService certifications,
			IStringLocalizer<Resgrid.Localization.Areas.User.ContractorBilling.ContractorBilling> strings)
		{
			_rateSchedules = rateSchedules;
			_access = access;
			_units = units;
			_certifications = certifications;
			_strings = strings;
		}

		#region Plumbing

		private static bool IsAdmin => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
		private static bool CanManage => IsAdmin || ClaimsAuthorizationHelper.CanManageInvoicing();
		private static bool CanView => CanManage || ClaimsAuthorizationHelper.CanViewInvoicing();

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
			view.CanManageRates = CanManage;
			view.CanManageBids = IsAdmin || ClaimsAuthorizationHelper.CanManageBids();
			view.CanManageContracts = IsAdmin || ClaimsAuthorizationHelper.CanManageContracts();
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
		private static bool IsDomainError(InvalidOperationException ex) => ex.Message.StartsWith("rateschedules_", StringComparison.Ordinal);

		#endregion

		[HttpGet]
		public async Task<IActionResult> Index(bool all = false)
		{
			var view = Page(new RateScheduleIndexView { IncludeInactive = all });
			view.Schedules = await _rateSchedules.GetSchedulesForDepartmentAsync(DepartmentId, all);
			return View(view);
		}

		[HttpGet]
		public async Task<IActionResult> New()
		{
			if (!CanManage) return Unauthorized();
			var view = Page(new RateScheduleEditView { Schedule = new RateSchedule { DepartmentId = DepartmentId, Currency = "USD", IsActive = true } });
			await FillLookupsAsync(view);
			return View("Edit", view);
		}

		[HttpGet]
		public async Task<IActionResult> Edit(string id)
		{
			var schedule = await _rateSchedules.GetScheduleByIdAsync(id, DepartmentId, includeInactive: true);
			if (schedule == null) return NotFound();
			var view = Page(new RateScheduleEditView { Schedule = schedule, Policy = schedule.Policy, ExportJson = await _rateSchedules.ExportScheduleJsonAsync(id, DepartmentId) });
			await FillLookupsAsync(view);
			return View(view);
		}

		private async Task FillLookupsAsync(RateScheduleEditView view)
		{
			view.Currencies = Currencies.Select(c => new SelectListItem(c, c, string.Equals(c, view.Schedule.Currency, StringComparison.OrdinalIgnoreCase))).ToList();
			try { view.UnitTypes = (await _units.GetUnitTypesForDepartmentAsync(DepartmentId) ?? new List<UnitType>()).OrderBy(t => t.Type).Select(t => new SelectListItem(t.Type, t.UnitTypeId.ToString())).ToList(); }
			catch (Exception ex) { Resgrid.Framework.Logging.LogException(ex, "Rate schedule editor: unit types unavailable."); }
			try { view.CertificationTypes = (await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId) ?? new List<DepartmentCertificationType>()).OrderBy(t => t.Type).Select(t => new SelectListItem(string.IsNullOrWhiteSpace(t.Code) ? t.Type : $"{t.Code} — {t.Type}", string.IsNullOrWhiteSpace(t.Code) ? t.Type : t.Code)).ToList(); }
			catch (Exception ex) { Resgrid.Framework.Logging.LogException(ex, "Rate schedule editor: certification types unavailable."); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Save(RateScheduleInput input, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			if (input == null) return BadRequest();
			var policy = new RateSchedulePolicy
			{
				RoundingMinutes = input.RoundingMinutes, CancellationMinimumHours = input.CancellationMinimumHours, CancellationVehiclesFullDay = input.CancellationVehiclesFullDay, DailyGuaranteeHours = input.DailyGuaranteeHours,
				PortalToPortal = input.PortalToPortal, UnsafeStandDownHours = input.UnsafeStandDownHours, NoClear8CarryOver = input.NoClear8CarryOver, TravelDayCapHours = input.TravelDayCapHours,
				OvertimeBasis = Enum.IsDefined(typeof(OvertimeBases), input.OvertimeBasis) ? (OvertimeBases)input.OvertimeBasis : OvertimeBases.ConsecutiveHours, FuelDeductionRatePerLitre = input.FuelDeductionRatePerLitre,
				ContinuousRunGapMinutes = input.ContinuousRunGapMinutes
			};
			if (!string.IsNullOrWhiteSpace(input.MealEligibilityJson))
			{
				try { policy.MealEligibility = JsonConvert.DeserializeObject<List<MealEligibilityWindow>>(input.MealEligibilityJson) ?? new List<MealEligibilityWindow>(); }
				catch (JsonException) { return Refused(400, "rateschedules_policy_invalid", string.IsNullOrWhiteSpace(input.RateScheduleId) ? "New" : "Edit", new { id = input.RateScheduleId }); }
			}
			try
			{
				var saved = await _rateSchedules.SaveScheduleAsync(new RateSchedule
				{
					RateScheduleId = input.RateScheduleId, DepartmentId = DepartmentId, Name = input.Name, Description = input.Description, Currency = input.Currency,
					EffectiveOn = input.EffectiveOn, ExpiresOn = input.ExpiresOn, IsActive = input.IsActive, PolicyJson = policy.ToJson()
				}, UserId, Ip, Agent, cancellationToken);
				return Saved("Edit", new { id = saved.RateScheduleId });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, string.IsNullOrWhiteSpace(input.RateScheduleId) ? "New" : "Edit", new { id = input.RateScheduleId }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			try
			{
				if (!await _rateSchedules.DeleteScheduleAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken)) return NotFound();
				return Saved("Index");
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(409, ex.Message, "Edit", new { id }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Clone(string id, string name, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			try
			{
				var clone = await _rateSchedules.CloneScheduleAsync(id, DepartmentId, name, UserId, Ip, Agent, cancellationToken);
				return Saved("Edit", new { id = clone.RateScheduleId });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "Edit", new { id }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveEntry(RateEntryInput input, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			if (input == null) return BadRequest();
			List<RateScheduleEntryBand> bands;
			if (input.PrefillBaseRate.HasValue)
				bands = _rateSchedules.PrefillHourlyBands(input.PrefillBaseRate.Value, input.PrefillStandbyRate, input.PrefillOvertime1Multiplier ?? 1.5m, input.PrefillOvertime1StartHours ?? 8, input.PrefillOvertime2Multiplier, input.PrefillOvertime2StartHours);
			else
			{
				try { bands = string.IsNullOrWhiteSpace(input.BandsJson) ? new List<RateScheduleEntryBand>() : JsonConvert.DeserializeObject<List<RateScheduleEntryBand>>(input.BandsJson) ?? new List<RateScheduleEntryBand>(); }
				catch (JsonException) { return Refused(400, "rateschedules_band_invalid", "Edit", new { id = input.RateScheduleId }); }
			}
			try
			{
				await _rateSchedules.SaveEntryAsync(new RateScheduleEntry
				{
					RateScheduleEntryId = input.RateScheduleEntryId, RateScheduleId = input.RateScheduleId, DepartmentId = DepartmentId, EntryType = input.EntryType, Name = input.Name, Code = input.Code, GroupKey = input.GroupKey,
					CrewSize = input.CrewSize, CertificationCode = input.CertificationCode, UnitTypeId = input.UnitTypeId, InventoryItemId = input.InventoryItemId, BillingBasis = input.BillingBasis,
					RequiredCertificationsJson = input.RequiredCertificationsJson, SortOrder = input.SortOrder, IsActive = input.IsActive, Bands = bands
				}, UserId, Ip, Agent, cancellationToken);
				return Saved("Edit", new { id = input.RateScheduleId });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "Edit", new { id = input.RateScheduleId }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteEntry(string id, string scheduleId, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			if (!await _rateSchedules.DeleteEntryAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken)) return NotFound();
			return Saved("Edit", new { id = scheduleId });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SavePremium(RatePremiumInput input, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			if (input == null) return BadRequest();
			try
			{
				await _rateSchedules.SavePremiumAsync(new RatePremium
				{
					RatePremiumId = input.RatePremiumId, RateScheduleId = input.RateScheduleId, DepartmentId = DepartmentId, Name = input.Name, Code = input.Code,
					StandbyAdder = input.StandbyAdder, DeploymentAdder = input.DeploymentAdder, Overtime1Adder = input.Overtime1Adder, Overtime2Adder = input.Overtime2Adder, IsActive = input.IsActive
				}, UserId, Ip, Agent, cancellationToken);
				return Saved("Edit", new { id = input.RateScheduleId });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "Edit", new { id = input.RateScheduleId }); }
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> DeletePremium(string id, string scheduleId, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			if (!await _rateSchedules.DeletePremiumAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken)) return NotFound();
			return Saved("Edit", new { id = scheduleId });
		}

		[HttpGet]
		public async Task<IActionResult> Export(string id)
		{
			var json = await _rateSchedules.ExportScheduleJsonAsync(id, DepartmentId);
			if (json == null) return NotFound();
			var schedule = await _rateSchedules.GetScheduleByIdAsync(id, DepartmentId, includeInactive: true);
			return File(Encoding.UTF8.GetBytes(json), "application/json", $"rate-schedule-{FileHelper.GetSafeFileName(schedule?.Name ?? id)}.json");
		}

		[HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(2 * 1024 * 1024)]
		public async Task<IActionResult> Import(Microsoft.AspNetCore.Http.IFormFile file, string json, CancellationToken cancellationToken)
		{
			if (!CanManage) return Unauthorized();
			var content = json;
			if (file != null && file.Length > 0)
			{
				using var reader = new System.IO.StreamReader(file.OpenReadStream(), Encoding.UTF8);
				content = await reader.ReadToEndAsync();
			}
			try
			{
				var imported = await _rateSchedules.ImportScheduleJsonAsync(DepartmentId, content, UserId, Ip, Agent, cancellationToken);
				return Saved("Edit", new { id = imported.RateScheduleId });
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Refused(400, ex.Message, "Index"); }
		}
	}
}
