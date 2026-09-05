using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// The NERIS incident-analysis filing (RMS-3): the fire/hazmat investigation posted separately from the
	/// incident, once the destination already holds it.
	/// <para>
	/// It has its own screens rather than a tab on the incident report because it is a second submittable artifact
	/// with its own lifecycle: an analysis that will not validate must never block the report, and an analysis may
	/// be finalized before its incident is filed — the submission simply waits for the incident's NERIS id.
	/// Visibility is inherited from the incident, so nobody reaches an analysis for a report they cannot open.
	/// </para>
	/// </summary>
	[Area("User")]
	public class IncidentAnalysisController : SecureBaseController
	{
		private readonly IIncidentAnalysisService _analysis;
		private readonly IIncidentReportsService _incidentReports;
		private readonly IRecordsCutoverService _cutoverService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;
		private readonly IDepartmentsService _departmentsService;
		private readonly INerisProfileService _neris;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> _localizer;

		public IncidentAnalysisController(IIncidentAnalysisService analysis, IIncidentReportsService incidentReports, IRecordsCutoverService cutoverService,
			IRecordsAuthorizationService recordsAuthorizationService, IDepartmentsService departmentsService, INerisProfileService neris,
			IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer)
		{
			_analysis = analysis;
			_incidentReports = incidentReports;
			_cutoverService = cutoverService;
			_recordsAuthorizationService = recordsAuthorizationService;
			_departmentsService = departmentsService;
			_neris = neris;
			_localizer = localizer;
		}

		#region Reads

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<IActionResult> Details(string id)
		{
			var model = await BuildDetailAsync(id);
			if (model == null)
				return NotFound();

			if (TempData["RecordsMessage"] is string message)
				model.Message = message;
			if (TempData["RecordsError"] is string error)
				model.ErrorMessage = error;
			return View(model);
		}

		#endregion

		#region Authoring

		/// <summary>Starts (or opens) the analysis for a report; one per report.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Start(string reportId, CancellationToken cancellationToken)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.RecordsUsable)
				return NotFound();
			if (string.IsNullOrWhiteSpace(reportId) || !await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, reportId, DepartmentId))
				return NotFound();

			try
			{
				var aggregate = await _analysis.StartForReportAsync(DepartmentId, UserId, reportId, RmsOriginClient.Web, cancellationToken);
				return RedirectToAction("Edit", new { id = aggregate.Analysis.RmsIncidentAnalysisId });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				TempData["RecordsError"] = ex.Message;
				return RedirectToAction("Details", "IncidentReports", new { id = reportId });
			}
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Edit(string id)
		{
			var aggregate = await LoadAuthorizedAsync(id);
			if (aggregate == null)
				return NotFound();
			if (!CanEdit(aggregate))
				return Unauthorized();

			var model = await BuildEditAsync(aggregate);
			if (TempData["RecordsMessage"] is string message)
				model.Message = message;
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Edit(IncidentAnalysisEditView model, CancellationToken cancellationToken)
		{
			var aggregate = await LoadAuthorizedAsync(model.AnalysisId);
			if (aggregate == null)
				return NotFound();
			if (!CanEdit(aggregate))
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			try
			{
				await _analysis.SaveDraftAsync(DepartmentId, UserId, model.AnalysisId, model.RowVersion, BuildInput(model, department),
					await CanViewRestrictedAsync(), cancellationToken);

				if (model.ValidateAfterSave)
				{
					var issues = await _analysis.ValidateAsync(DepartmentId, model.AnalysisId, cancellationToken);
					TempData["RecordsMessage"] = issues.Count == 0 ? _localizer["NoValidationIssues"].Value : string.Format(_localizer["ValidationRun"].Value, issues.Count);
					return RedirectToAction(issues.Any(i => i.Severity == (int)RmsValidationSeverity.Error) ? "Edit" : "Details", new { id = model.AnalysisId });
				}

				TempData["RecordsMessage"] = _localizer["RecordSaved"].Value;
				return RedirectToAction("Details", new { id = model.AnalysisId });
			}
			catch (RecordConcurrencyException)
			{
				return await EditWithErrorAsync(aggregate, _localizer["ConcurrencyError"]);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is RecordTransitionException || ex is InvalidOperationException)
			{
				return await EditWithErrorAsync(aggregate, ex.Message);
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Validate(string id, CancellationToken cancellationToken)
		{
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();

			try
			{
				var issues = await _analysis.ValidateAsync(DepartmentId, id, cancellationToken);
				TempData["RecordsMessage"] = issues.Count == 0 ? _localizer["NoValidationIssues"].Value : string.Format(_localizer["ValidationRun"].Value, issues.Count);
				return RedirectToAction("Details", new { id });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				return await DetailsWithErrorAsync(id, ex.Message);
			}
		}

		#endregion

		#region Lifecycle

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Finalize)]
		public Task<IActionResult> Finalize(string id, long rowVersion, CancellationToken cancellationToken)
		{
			return TransitionAsync(id, () => _analysis.FinalizeAsync(DepartmentId, UserId, id, rowVersion, cancellationToken));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Submit)]
		public async Task<IActionResult> Submit(string id, CancellationToken cancellationToken)
		{
			var result = await TransitionAsync(id, () => _analysis.QueueSubmissionAsync(DepartmentId, UserId, id, cancellationToken));
			if (result is RedirectToActionResult)
				TempData["RecordsMessage"] = _localizer["SubmissionQueued"].Value;
			return result;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Void)]
		public Task<IActionResult> Void(string id, string reasonCode, string reasonText, CancellationToken cancellationToken)
		{
			return TransitionAsync(id, () => _analysis.VoidAsync(DepartmentId, UserId, id, reasonCode, reasonText, cancellationToken));
		}

		private async Task<IActionResult> TransitionAsync(string id, Func<Task<IncidentAnalysisAggregate>> action)
		{
			if (await LoadAuthorizedAsync(id) == null)
				return NotFound();

			try
			{
				await action();
				return RedirectToAction("Details", new { id });
			}
			catch (RecordConcurrencyException)
			{
				return await DetailsWithErrorAsync(id, _localizer["ConcurrencyError"]);
			}
			catch (IncidentReportValidationException ex)
			{
				return await DetailsWithErrorAsync(id, _localizer["ValidationBlocked"] + " " + ex.Message);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is RecordTransitionException || ex is InvalidOperationException)
			{
				return await DetailsWithErrorAsync(id, ex.Message);
			}
		}

		#endregion

		#region Helpers

		/// <summary>An analysis is only as visible as the incident it belongs to.</summary>
		private async Task<IncidentAnalysisAggregate> LoadAuthorizedAsync(string id, bool includeHistory = false)
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;

			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return null;

			var aggregate = await _analysis.GetAsync(DepartmentId, id, includeHistory);
			if (aggregate?.Analysis == null)
				return null;

			if (!await _recordsAuthorizationService.CanUserViewRecordAsync(UserId, aggregate.Analysis.IncidentReportId, DepartmentId))
			{
				await _incidentReports.RecordAccessAsync(DepartmentId, UserId, aggregate.Analysis.IncidentReportId, null, RmsAccessAuditAction.Denied, null, IpAddressHelper.GetRequestIP(Request, true));
				return null;
			}

			return aggregate;
		}

		private bool CanEdit(IncidentAnalysisAggregate aggregate)
		{
			if (!ClaimsAuthorizationHelper.CanCreateRecord())
				return false;

			var analysis = aggregate.Analysis;
			return ClaimsAuthorizationHelper.IsUserDepartmentAdmin()
				|| string.Equals(analysis.OwnerUserId, UserId, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(analysis.AuthorUserId, UserId, StringComparison.OrdinalIgnoreCase);
		}

		private async Task<IncidentAnalysisDetailView> BuildDetailAsync(string id)
		{
			var aggregate = await LoadAuthorizedAsync(id, true);
			if (aggregate == null)
				return null;

			var report = aggregate.Report;
			return new IncidentAnalysisDetailView
			{
				Aggregate = aggregate,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				SubmissionEnabled = await _neris.IsSubmissionEnabledAsync(DepartmentId),
				Reference = report?.RecordNumber ?? report?.DraftReference,
				PersonnelNames = await PersonnelNamesAsync(),
				CanEdit = CanEdit(aggregate),
				CanFinalize = ClaimsAuthorizationHelper.CanFinalizeRecords(),
				CanSubmit = ClaimsAuthorizationHelper.CanSubmitRecords(),
				CanVoid = ClaimsAuthorizationHelper.CanVoidRecords(),
				CanViewRestricted = await CanViewRestrictedAsync(),
				IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin()
			};
		}

		private async Task<IActionResult> DetailsWithErrorAsync(string id, string error)
		{
			var model = await BuildDetailAsync(id);
			if (model == null)
				return NotFound();
			model.ErrorMessage = error;
			return View("Details", model);
		}

		private async Task<IActionResult> EditWithErrorAsync(IncidentAnalysisAggregate aggregate, string error)
		{
			var model = await BuildEditAsync(aggregate);
			model.ErrorMessage = error;
			return View("Edit", model);
		}

		private async Task<IncidentAnalysisEditView> BuildEditAsync(IncidentAnalysisAggregate aggregate)
		{
			var analysis = aggregate.Analysis;
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var model = new IncidentAnalysisEditView
			{
				AnalysisId = analysis.RmsIncidentAnalysisId, Issues = await _analysis.ValidateAsync(DepartmentId, analysis.RmsIncidentAnalysisId),
				ReportId = analysis.IncidentReportId,
				RowVersion = analysis.RowVersion,
				Reference = aggregate.Report?.RecordNumber ?? aggregate.Report?.DraftReference,
				State = aggregate.State,
				GeneralCause = analysis.GeneralCause,
				InvestigationTypes = Split(analysis.InvestigationTypesCsv),
				CurrencyCode = analysis.CurrencyCode,
				CanEditRestricted = await CanViewRestrictedAsync(),
				Department = department
			};

			// Only the analysis half of the progressive rules belongs here; the incident's own sections stay on the report.
			var requirements = (await _incidentReports.GetSectionRequirementsAsync(DepartmentId, analysis.IncidentReportId))
				.Where(r => RmsIncidentModuleCatalog.Get(r.Kind)?.BelongsToAnalysis == true).ToList();
			var kinds = requirements.Select(r => r.Kind).Concat(RmsIncidentModuleCatalog.AnalysisModules().Select(d => d.Kind)).Distinct().ToList();
			foreach (var kind in aggregate.Modules.Select(m => (RmsIncidentModuleKind)m.ModuleKind).Distinct())
			{
				if (!kinds.Contains(kind))
					kinds.Add(kind);
			}

			foreach (var kind in kinds)
			{
				var requirement = requirements.FirstOrDefault(r => r.Kind == kind);
				var descriptor = RmsIncidentModuleCatalog.Get(kind);
				var rows = aggregate.Modules.Where(m => m.ModuleKind == (int)kind).OrderBy(m => m.Ordinal).ToList();

				model.Sections.Add(new IncidentSectionView
				{
					Kind = kind,
					Required = requirement?.Required ?? false,
					Reason = requirement?.Reason ?? _localizer["Suggested"].Value,
					Present = rows.Count > 0,
					IsCollection = descriptor?.IsCollection ?? false,
					PayloadPath = descriptor?.PayloadPath,
					SchemaName = descriptor?.SchemaName,
					PrimaryCodes = Codes(requirement?.PrimaryCodeSet),
					SecondaryCodes = Codes(requirement?.SecondaryCodeSet)
				});

				foreach (var row in rows)
				{
					model.Modules.Add(new IncidentModuleRow
					{
						Kind = (int)kind, Included = true, PrimaryCode = row.PrimaryCode, SecondaryCode = row.SecondaryCode, Quantity = row.Quantity,
						QuantityUnit = row.QuantityUnit, OccurredOn = row.OccurredOn?.TimeConverter(department), DetailJson = row.DetailJson
					});
				}

				if (rows.Count == 0 || (descriptor?.IsCollection ?? false))
					model.Modules.Add(new IncidentModuleRow { Kind = (int)kind, Included = false });
			}

			model.Properties = aggregate.Properties.OrderBy(p => p.Ordinal).Select(p => new IncidentPropertyRow
			{
				Included = true, LocationUse = p.LocationUse, ConstructionType = p.ConstructionType, Foundation = p.Foundation, ExteriorFinish = p.ExteriorFinish,
				RoofMaterial = p.RoofMaterial, StoriesAboveGrade = p.StoriesAboveGrade, StoriesBelowGrade = p.StoriesBelowGrade, YearBuilt = p.YearBuilt,
				Vacancy = p.Vacancy, DamageType = p.DamageType, FireSpread = p.FireSpread, EstimatedValue = p.EstimatedValue, EstimatedLoss = p.EstimatedLoss,
				ContentsValue = p.ContentsValue, ContentsLoss = p.ContentsLoss, DetailJson = Resgrid.Providers.Neris.NerisMappingService.MapProperty(p).ToString(Newtonsoft.Json.Formatting.None)
			}).ToList();

			model.Vehicles = aggregate.Vehicles.OrderBy(v => v.Ordinal).Select(v => new IncidentVehicleRow
			{
				Included = true, VehicleId = v.RmsIncidentVehicleId, VehicleKind = v.VehicleKind, Make = v.Make, Model = v.Model, ModelYear = v.ModelYear, BodyStyle = v.BodyStyle, Powertrain = v.Powertrain,
				DamageType = v.DamageType, Vin = v.Vin, LicensePlate = v.LicensePlate, LicenseState = v.LicenseState, WasOccupied = v.WasOccupied,
				EstimatedValue = v.EstimatedValue, EstimatedLoss = v.EstimatedLoss, DetailJson = v.DetailJson
			}).ToList();

			model.GeneralCauses = Codes("fire_cause_general");
			model.InvestigationTypeCodes = Codes("fire_invest");
			model.LocationUses = Codes("location_use");
			model.ConstructionTypes = Codes("construction");
			model.Vacancies = Codes("vacancy");
			model.BuildingDamageTypes = Codes("fire_bldg_damage");
			model.FireSpreads = Codes("fire_spread");
			model.VehicleMakes = Codes("auto_make");
			model.VehicleBodyStyles = Codes("auto_body_style");
			model.Powertrains = Codes("powertrain");
			model.VehicleDamageTypes = Codes("vehicle_damage");
			return model;
		}

		private IncidentAnalysisDraftInput BuildInput(IncidentAnalysisEditView model, Department department)
		{
			return new IncidentAnalysisDraftInput
			{
				GeneralCause = model.GeneralCause,
				InvestigationTypes = model.InvestigationTypes ?? new List<string>(),
				CurrencyCode = model.CurrencyCode,
				Modules = (model.Modules ?? new List<IncidentModuleRow>()).Where(m => m.Included && m.Kind > 0).Select(m => new IncidentModuleInput
				{
					Kind = (RmsIncidentModuleKind)m.Kind, PrimaryCode = m.PrimaryCode, SecondaryCode = m.SecondaryCode, Quantity = m.Quantity,
					QuantityUnit = m.QuantityUnit, OccurredOn = ToUtc(m.OccurredOn, department), DetailJson = m.DetailJson
				}).ToList(),
				Properties = (model.Properties ?? new List<IncidentPropertyRow>()).Where(p => p.Included).Select(IncidentGuidedFormMapper.Property).ToList(),
				Vehicles = (model.Vehicles ?? new List<IncidentVehicleRow>()).Where(v => v.Included).Select(v => new IncidentVehicleInput
				{
					VehicleId = v.VehicleId, VehicleKind = v.VehicleKind, Make = v.Make, Model = v.Model, ModelYear = v.ModelYear, BodyStyle = v.BodyStyle, Powertrain = v.Powertrain,
					DamageType = v.DamageType, Vin = v.Vin, LicensePlate = v.LicensePlate, LicenseState = v.LicenseState, WasOccupied = v.WasOccupied,
					EstimatedValue = v.EstimatedValue, EstimatedLoss = v.EstimatedLoss, DetailJson = v.DetailJson
				}).ToList(),
				OriginClient = RmsOriginClient.Web
			};
		}

		private async Task<bool> CanViewRestrictedAsync() => ClaimsAuthorizationHelper.CanViewRestrictedRecords() && await _recordsAuthorizationService.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords);

		private List<SelectListItem> Codes(string setKey)
		{
			if (string.IsNullOrWhiteSpace(setKey))
				return new List<SelectListItem>();

			var set = _neris.GetValueSet(setKey);
			return (set?.Codes ?? new List<string>()).Select(c => new SelectListItem { Value = c, Text = c.Replace("||", " / ").Replace('_', ' ') }).ToList();
		}

		private static List<string> Split(string csv)
		{
			return string.IsNullOrWhiteSpace(csv)
				? new List<string>()
				: csv.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
		}

		private static DateTime? ToUtc(DateTime? local, Department department)
		{
			if (!local.HasValue || local.Value == DateTime.MinValue)
				return null;
			if (department == null || string.IsNullOrWhiteSpace(department.TimeZone))
				return DateTime.SpecifyKind(local.Value, DateTimeKind.Utc);

			return DateTimeHelpers.ConvertToUtc(local.Value, department.TimeZone, true);
		}

		private async Task<Dictionary<string, string>> PersonnelNamesAsync()
		{
			var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var name in names ?? new List<PersonName>())
			{
				if (!string.IsNullOrWhiteSpace(name.UserId))
					map[name.UserId] = name.Name;
			}
			return map;
		}

		#endregion
	}
}
