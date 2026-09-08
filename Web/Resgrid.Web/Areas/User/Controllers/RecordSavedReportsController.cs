using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Department saved reports (RMS plan section 4.1, RMS-1B): allowlisted typed columns, bounded filters, one
	/// group-by, count/sum/avg/min/max. Managing needs ManageRecordReports; running honors the runner's group scope.
	/// </summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordSavedReportsController : SecureBaseController
	{
		private readonly IRecordSavedReportsService _reports;
		private readonly IRecordDefinitionsService _definitions;
		private readonly IRecordsCutoverService _cutover;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IDepartmentsService _departments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> _localizer;

		public RecordSavedReportsController(IRecordSavedReportsService reports, IRecordDefinitionsService definitions, IRecordsCutoverService cutover, IRecordsAuthorizationService authorization,
			IDepartmentsService departments, IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer)
		{
			_reports = reports;
			_definitions = definitions;
			_cutover = cutover;
			_authorization = authorization;
			_departments = departments;
			_localizer = localizer;
		}

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled) return NotFound();
			var model = new RecordSavedReportsIndexView { Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false), Reports = await _reports.GetForDepartmentAsync(DepartmentId), CanManage = await CanManageAsync() };
			if (TempData["RecordsMessage"] is string message) model.Message = message;
			if (TempData["RecordsError"] is string error) model.ErrorMessage = error;
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.RecordReport_Update)]
		public async Task<IActionResult> Edit(string id, string definitionKey)
		{
			if (!await CanManageAsync()) return Forbid();
			RecordSavedReportEditView model;
			if (string.IsNullOrWhiteSpace(id))
				model = new RecordSavedReportEditView { DefinitionKey = definitionKey };
			else
			{
				var report = await _reports.GetAsync(DepartmentId, id);
				if (report == null) return NotFound();
				model = RecordSavedReportEditView.From(report);
			}
			await PopulateAsync(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.RecordReport_Update)]
		public async Task<IActionResult> Edit(RecordSavedReportEditView model, string action, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync()) return Forbid();
			try
			{
				var report = model.ToReport();
				if (string.Equals(action, "validate", StringComparison.OrdinalIgnoreCase))
				{
					var validation = await _reports.ValidateAsync(DepartmentId, report);
					model.Issues = validation.Issues;
					model.Message = validation.IsValid ? _localizer["ReportValid"].Value : _localizer["ReportInvalid"].Value;
					await PopulateAsync(model);
					return View(model);
				}
				var saved = await _reports.SaveAsync(DepartmentId, UserId, report, cancellationToken);
				TempData["RecordsMessage"] = _localizer["ReportSaved"].Value;
				return RedirectToAction("Edit", new { id = saved.RmsSavedReportDefinitionId });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException) { model.ErrorMessage = _localizer["ConcurrencyError"].Value; }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is Newtonsoft.Json.JsonException) { model.ErrorMessage = ex.Message; }
			await PopulateAsync(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.RecordReport_Update)]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync()) return Forbid();
			try
			{
				await _reports.DeleteAsync(DepartmentId, UserId, id, null, cancellationToken);
				TempData["RecordsMessage"] = _localizer["ReportDeleted"].Value;
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			return RedirectToAction("Index");
		}

		[HttpGet]
		public async Task<IActionResult> Run(string id, CancellationToken cancellationToken)
		{
			var report = await _reports.GetAsync(DepartmentId, id);
			if (report == null) return NotFound();
			var model = new RecordSavedReportRunView { Report = report, Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false) };
			try { model.Result = await _reports.RunAsync(DepartmentId, UserId, id, cancellationToken); }
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { model.ErrorMessage = ex.Message; }
			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> RunCsv(string id, CancellationToken cancellationToken)
		{
			try
			{
				var result = await _reports.RunAsync(DepartmentId, UserId, id, cancellationToken);
				return File(Encoding.UTF8.GetBytes(_reports.ToCsv(result)), "text/csv", (result.Name ?? "report").Replace(' ', '-') + ".csv");
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; return RedirectToAction("Index"); }
		}

		private async Task PopulateAsync(RecordSavedReportEditView model)
		{
			model.Definitions = (await _definitions.ListAsync(DepartmentId)).Where(d => !d.Locked && d.PublishedVersion.HasValue).Select(d => new SelectListItem { Value = d.Key, Text = d.Name }).ToList();
			model.CanIncludeRestricted = await _authorization.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords);
			if (!string.IsNullOrWhiteSpace(model.DefinitionKey))
			{
				var aggregate = await _definitions.GetAsync(DepartmentId, model.DefinitionKey);
				var version = model.DefinitionVersion.HasValue ? aggregate?.Versions.FirstOrDefault(v => v.Version == model.DefinitionVersion) : aggregate?.Published;
				model.Schema = version?.Schema;
			}
		}

		private Task<bool> CanManageAsync() => _authorization.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ManageRecordReports);
	}
}
