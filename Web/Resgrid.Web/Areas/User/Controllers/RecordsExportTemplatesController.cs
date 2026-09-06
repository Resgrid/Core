using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Department report exports (RMS plan section 5.6): a department designs the file an agency without an API
	/// receives, and a Workflow step or the schedule sweep (worker 45) carries it. Authoring needs
	/// ManageRecordReports; every render is an Export audit against each record it contains.
	/// </summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_Export)]
	public class RecordsExportTemplatesController : SecureBaseController
	{
		private readonly IRecordsExportService _exports;
		private readonly IRecordsCutoverService _cutover;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordsProtectionService _protection;
		private readonly IDepartmentsService _departments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> _localizer;

		public RecordsExportTemplatesController(IRecordsExportService exports, IRecordsCutoverService cutover, IRecordsAuthorizationService authorization,
			IRecordsProtectionService protection, IDepartmentsService departments, IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer)
		{
			_exports = exports;
			_cutover = cutover;
			_authorization = authorization;
			_protection = protection;
			_departments = departments;
			_localizer = localizer;
		}

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (!await RequireAsync()) return Forbid();
			var moduleState = await _cutover.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled) return NotFound();

			var model = new RecordsExportTemplatesIndexView
			{
				ModuleState = moduleState,
				Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false),
				Templates = await _exports.GetTemplatesAsync(DepartmentId),
				ProtectionEnforced = await _protection.IsEnforcedAsync(DepartmentId)
			};
			if (TempData["RecordsMessage"] is string message) model.Message = message;
			if (TempData["RecordsError"] is string error) model.ErrorMessage = error;
			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Edit(string id)
		{
			if (!await RequireAsync()) return Forbid();
			var moduleState = await _cutover.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled) return NotFound();

			RecordsExportTemplateEditView model;
			if (string.IsNullOrWhiteSpace(id))
			{
				model = new RecordsExportTemplateEditView { Columns = RecordsExportFieldCatalog.DefaultColumns.ToList() };
			}
			else
			{
				var template = await _exports.GetTemplateAsync(DepartmentId, id);
				if (template == null) return NotFound();
				model = RecordsExportTemplateEditView.From(template);
			}
			await DecorateAsync(model, moduleState);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Edit(RecordsExportTemplateEditView model, CancellationToken cancellationToken)
		{
			if (!await RequireAsync()) return Forbid();
			var moduleState = await _cutover.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled) return NotFound();

			try
			{
				var template = model.ToTemplate();
				var saved = await _exports.SaveAsync(DepartmentId, UserId, template, model.AcknowledgeEgress, cancellationToken);
				TempData["RecordsMessage"] = _localizer["ExportTemplateSaved"].Value;
				return RedirectToAction("Edit", new { id = saved.RmsExportTemplateId });
			}
			catch (UnauthorizedAccessException)
			{
				return Forbid();
			}
			catch (ArgumentException ex)
			{
				model.ErrorMessage = ex.Message;
				await DecorateAsync(model, moduleState);
				return View(model);
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			if (!await RequireAsync()) return Forbid();
			try
			{
				await _exports.DeleteAsync(DepartmentId, UserId, id, cancellationToken);
				TempData["RecordsMessage"] = _localizer["ExportTemplateDeleted"].Value;
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			return RedirectToAction("Index");
		}

		/// <summary>Renders the template now for the last window (or a named record) and returns the file, so the author can check the layout before an agency does.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RunNow(string id, string recordId, CancellationToken cancellationToken)
		{
			if (!await RequireAsync()) return Forbid();
			var template = await _exports.GetTemplateAsync(DepartmentId, id);
			if (template == null) return NotFound();

			try
			{
				var request = new RecordsExportRequest { Trigger = RmsExportTrigger.Manual, ActingUserId = UserId, Purpose = "Manual export " + template.Name };
				if ((RmsExportScope)template.Scope == RmsExportScope.TriggeringRecord)
				{
					if (string.IsNullOrWhiteSpace(recordId))
					{
						TempData["RecordsError"] = _localizer["ExportRunNowNeedsRecord"].Value;
						return RedirectToAction("Runs", new { id });
					}
					request.RecordId = recordId.Trim();
				}
				var run = await _exports.RenderAsync(DepartmentId, template, request, cancellationToken);
				return File(run.Data, run.ContentType, run.FileName);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				TempData["RecordsError"] = ex.Message;
				return RedirectToAction("Runs", new { id });
			}
		}

		[HttpGet]
		public async Task<IActionResult> Runs(string id)
		{
			if (!await RequireAsync()) return Forbid();
			var template = await _exports.GetTemplateAsync(DepartmentId, id);
			if (template == null) return NotFound();

			var model = new RecordsExportRunsView
			{
				Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false),
				Template = template,
				Runs = await _exports.GetRunsAsync(DepartmentId, id, 100)
			};
			if (TempData["RecordsMessage"] is string message) model.Message = message;
			if (TempData["RecordsError"] is string error) model.ErrorMessage = error;
			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Download(string id)
		{
			if (!await RequireAsync()) return Forbid();
			var run = await _exports.GetRunAsync(DepartmentId, id, true);
			if (run?.Data == null) return NotFound();
			return File(run.Data, run.ContentType ?? "application/octet-stream", run.FileName ?? "export");
		}

		private async Task<bool> RequireAsync()
			=> await _authorization.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ManageRecordReports);

		private async Task DecorateAsync(RecordsExportTemplateEditView model, RecordsModuleState moduleState)
		{
			model.ModuleState = moduleState;
			model.Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false);
			model.CanIncludeRestricted = await _authorization.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords);
			model.ProtectionEnforced = await _protection.IsEnforcedAsync(DepartmentId);
			model.Definitions = RmsDefinitionKeys.LockedTypes.Select(t => new KeyValuePair<string, string>(t.Key, t.Value.ToString()))
				.Append(new KeyValuePair<string, string>(RmsDefinitionKeys.NerisIncidentReport, "NERIS incident report")).ToList();
			if (!string.IsNullOrWhiteSpace(model.RmsExportTemplateId))
			{
				var validation = await _exports.ValidateAsync(DepartmentId, UserId, model.ToTemplate());
				model.Warnings = validation.Warnings;
			}
		}
	}
}
