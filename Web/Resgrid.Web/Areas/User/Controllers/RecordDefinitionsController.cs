using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services.Records;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// The controlled definition designer (RMS plan sections 4.1 and 5.4, RMS-1B): template browse, clone or blank
	/// draft, policy and schema editing with server validation, impact preview, publish, retire, history, diff and
	/// draft migration. Managing needs ManageRecordDefinitions; publishing and retiring need PublishRecordDefinitions.
	/// </summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.RecordDefinition_Update)]
	public class RecordDefinitionsController : SecureBaseController
	{
		private readonly IRecordDefinitionsService _definitions;
		private readonly IRecordTemplatePacksService _templates;
		private readonly IRecordsCutoverService _cutover;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IDepartmentsService _departments;
		private readonly IPersonnelRolesService _roles;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> _localizer;
		private readonly IRecordsPrintLayoutService _printLayouts;

		public RecordDefinitionsController(IRecordDefinitionsService definitions, IRecordTemplatePacksService templates, IRecordsCutoverService cutover, IRecordsAuthorizationService authorization,
			IDepartmentsService departments, IPersonnelRolesService roles, IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer, IRecordsPrintLayoutService printLayouts)
		{
			_printLayouts = printLayouts;
			_definitions = definitions;
			_templates = templates;
			_cutover = cutover;
			_authorization = authorization;
			_departments = departments;
			_roles = roles;
			_localizer = localizer;
		}

		[HttpGet]
		public async Task<IActionResult> Index(bool includeRetired = false)
		{
			if (!await RequireManageAsync()) return Forbid();
			var moduleState = await _cutover.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled) return NotFound();
			var model = new RecordDefinitionsIndexView
			{
				ModuleState = moduleState, Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false), IncludeRetired = includeRetired,
				Definitions = await _definitions.ListAsync(DepartmentId, includeRetired), CanPublish = await CanPublishAsync()
			};
			ReadTempData(model);
			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Templates()
		{
			if (!await RequireManageAsync()) return Forbid();
			var model = new RecordTemplatesView { Packs = await _templates.GetCatalogAsync(), Profiles = await _templates.GetProfilesAsync() };
			ReadTempData(model);
			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Create(string templateKey, string cloneFrom, string profile = "generic", string locale = null)
		{
			if (!await RequireManageAsync()) return Forbid();
			var model = new RecordDefinitionCreateView { TemplateKey = templateKey, CloneFromDefinitionKey = cloneFrom, JurisdictionProfileKey = string.IsNullOrWhiteSpace(profile) ? "generic" : profile, Locale = locale };
			await PopulateCreateAsync(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Create(RecordDefinitionCreateView model, CancellationToken cancellationToken)
		{
			if (!await RequireManageAsync()) return Forbid();
			try
			{
				var aggregate = await _definitions.CreateAsync(DepartmentId, UserId, new RecordDefinitionCreateInput
				{
					DefinitionKey = model.DefinitionKey, Name = model.Name, Category = model.Category, TemplateKey = model.TemplateKey, CloneFromDefinitionKey = model.CloneFromDefinitionKey, JurisdictionProfileKey = model.JurisdictionProfileKey, Locale = model.Locale
				}, cancellationToken);
				TempData["RecordsMessage"] = _localizer["DefinitionCreated"].Value;
				return RedirectToAction("Edit", new { key = aggregate.Definition.DefinitionKey, version = aggregate.Latest.Version });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				model.ErrorMessage = ex.Message;
				await PopulateCreateAsync(model);
				return View(model);
			}
		}

		[HttpGet]
		public async Task<IActionResult> Edit(string key, int? version)
		{
			if (!await RequireManageAsync()) return Forbid();
			var aggregate = await _definitions.GetAsync(DepartmentId, key);
			if (aggregate == null) return NotFound();
			var row = version.HasValue ? aggregate.Versions.FirstOrDefault(v => v.Version == version) : aggregate.Draft ?? aggregate.Published ?? aggregate.Latest;
			if (row == null) return NotFound();
			var model = RecordDefinitionEditView.From(aggregate, row);
			await PopulateEditAsync(model);
			var validation = await _definitions.ValidateAsync(DepartmentId, model.ToDraftInput());
			model.Issues = validation.Issues;
			ReadTempData(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Edit(RecordDefinitionEditView model, string action, CancellationToken cancellationToken)
		{
			if (!await RequireManageAsync()) return Forbid();
			var aggregate = await _definitions.GetAsync(DepartmentId, model.DefinitionKey);
			if (aggregate == null) return NotFound();
			model.Aggregate = aggregate;
			model.VersionRow = aggregate.Versions.FirstOrDefault(v => v.Version == model.Version);
			try
			{
				var input = model.ToDraftInput();
				if (string.Equals(action, "validate", StringComparison.OrdinalIgnoreCase))
				{
					var validation = await _definitions.ValidateAsync(DepartmentId, input);
					model.Issues = validation.Issues;
					model.MinimumClientCapability = validation.MinimumClientCapability;
					model.Message = validation.IsValid ? _localizer["DefinitionValid"].Value : _localizer["DefinitionInvalid"].Value;
					await PopulateEditAsync(model);
					return View(model);
				}
				var saved = await _definitions.SaveDraftAsync(DepartmentId, UserId, model.DefinitionKey, model.Version, model.RowVersion, input, cancellationToken);
				TempData["RecordsMessage"] = _localizer["DefinitionSaved"].Value;
				return RedirectToAction("Edit", new { key = model.DefinitionKey, version = saved.Version });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException) { model.ErrorMessage = _localizer["ConcurrencyError"].Value; }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is Newtonsoft.Json.JsonException) { model.ErrorMessage = ex.Message; }
			model.Schema = SafeParse(model.SchemaJson);
			await PopulateEditAsync(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> OpenDraft(string key, CancellationToken cancellationToken)
		{
			if (!await RequireManageAsync()) return Forbid();
			try
			{
				var draft = await _definitions.OpenDraftAsync(DepartmentId, UserId, key, cancellationToken);
				return RedirectToAction("Edit", new { key, version = draft.Version });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; return RedirectToAction("Index"); }
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteDraft(string key, int version, CancellationToken cancellationToken)
		{
			if (!await RequireManageAsync()) return Forbid();
			try
			{
				await _definitions.DeleteDraftAsync(DepartmentId, UserId, key, version, cancellationToken);
				TempData["RecordsMessage"] = _localizer["DefinitionDraftDeleted"].Value;
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; }
			return RedirectToAction("Index");
		}

		[HttpGet]
		public async Task<IActionResult> Impact(string key, int version)
		{
			if (!await RequireManageAsync()) return Forbid();
			var aggregate = await _definitions.GetAsync(DepartmentId, key);
			var row = aggregate?.Versions.FirstOrDefault(v => v.Version == version);
			if (row == null) return NotFound();
			var model = new RecordDefinitionImpactView { Aggregate = aggregate, VersionRow = row, Preview = await _definitions.ImpactPreviewAsync(DepartmentId, key, version), CanPublish = await CanPublishAsync() };
			if (aggregate.Published != null && aggregate.Published.Version != version)
				model.Diff = await _definitions.DiffAsync(DepartmentId, key, aggregate.Published.Version, version);
			ReadTempData(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.RecordDefinition_Publish)]
		public async Task<IActionResult> Publish(string key, int version, long rowVersion, CancellationToken cancellationToken)
		{
			if (!await CanPublishAsync()) return Forbid();
			try
			{
				await _definitions.PublishAsync(DepartmentId, UserId, key, version, rowVersion, cancellationToken);
				TempData["RecordsMessage"] = string.Format(_localizer["DefinitionPublished"].Value, version);
				return RedirectToAction("Edit", new { key, version });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException) { TempData["RecordsError"] = _localizer["ConcurrencyError"].Value; }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; }
			return RedirectToAction("Impact", new { key, version });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.RecordDefinition_Publish)]
		public async Task<IActionResult> Retire(string key, long rowVersion, string reason, CancellationToken cancellationToken)
		{
			if (!await CanPublishAsync()) return Forbid();
			try
			{
				await _definitions.RetireAsync(DepartmentId, UserId, key, rowVersion, reason, cancellationToken);
				TempData["RecordsMessage"] = _localizer["DefinitionRetired"].Value;
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException) { TempData["RecordsError"] = _localizer["ConcurrencyError"].Value; }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; }
			return RedirectToAction("Index");
		}

		[HttpGet]
		public async Task<IActionResult> History(string key, int? from, int? to)
		{
			if (!await RequireManageAsync()) return Forbid();
			var aggregate = await _definitions.GetAsync(DepartmentId, key);
			if (aggregate == null) return NotFound();
			var model = new RecordDefinitionHistoryView
			{
				Aggregate = aggregate, Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false), Versions = await _definitions.HistoryAsync(DepartmentId, key), From = from, To = to, CanManage = true
			};
			if (from.HasValue && to.HasValue)
			{
				try { model.Diff = await _definitions.DiffAsync(DepartmentId, key, from.Value, to.Value); }
				catch (ArgumentException ex) { model.ErrorMessage = ex.Message; }
			}
			ReadTempData(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Migrate(string key, int from, int to, bool preview, string mappingJson, CancellationToken cancellationToken)
		{
			if (!await RequireManageAsync()) return Forbid();
			try
			{
				var mapping = string.IsNullOrWhiteSpace(mappingJson) ? new List<RecordDefinitionFieldMapping>() : Newtonsoft.Json.JsonConvert.DeserializeObject<List<RecordDefinitionFieldMapping>>(mappingJson) ?? new List<RecordDefinitionFieldMapping>();
				var result = await _definitions.MigrateDraftsAsync(DepartmentId, UserId, key, from, to, mapping, preview, cancellationToken);
				TempData["RecordsMessage"] = string.Format(_localizer[preview ? "DefinitionMigrationPreview" : "DefinitionMigrationDone"].Value, result.Migrated, result.Skipped, result.UnmappedFieldKeys.Count == 0 ? "-" : string.Join(", ", result.UnmappedFieldKeys));
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is Newtonsoft.Json.JsonException) { TempData["RecordsError"] = ex.Message; }
			return RedirectToAction("History", new { key, from, to });
		}

		private async Task PopulateCreateAsync(RecordDefinitionCreateView model)
		{
			var profiles = await _templates.GetProfilesAsync();
			model.Profiles = profiles.Select(p => new SelectListItem { Value = p.ProfileKey, Text = p.Name }).ToList();
			model.Locales = new[] { "en-US", "en-CA", "fr-CA" }.Select(l => new SelectListItem { Value = l, Text = l }).ToList();
			model.Templates = (await _templates.GetCatalogAsync()).SelectMany(p => p.Definitions.Select(d => new SelectListItem { Value = d.Key, Text = p.Name + " — " + d.Name + (d.IsPreview ? " (Preview)" : string.Empty) })).ToList();
			model.DepartmentDefinitions = (await _definitions.ListAsync(DepartmentId)).Where(d => !d.Locked).Select(d => new SelectListItem { Value = d.Key, Text = d.Name }).ToList();
			if (!string.IsNullOrWhiteSpace(model.TemplateKey))
			{
				try { model.Rendering = await _templates.RenderAsync(model.TemplateKey, model.JurisdictionProfileKey, model.Locale); }
				catch (ArgumentException ex) { model.ErrorMessage = ex.Message; }
				if (model.Rendering != null && string.IsNullOrWhiteSpace(model.Name)) model.Name = model.Rendering.Template.Name;
				if (model.Rendering != null && string.IsNullOrWhiteSpace(model.DefinitionKey)) model.DefinitionKey = model.Rendering.Template.Key.Replace("template.", string.Empty).Replace("pack.", string.Empty);
			}
		}

		[HttpGet]
		public async Task<IActionResult> Layout(string key)
		{
			if (!await RequireManageAsync()) return Forbid();
			var aggregate = await _definitions.GetAsync(DepartmentId, key);
			if (aggregate == null || aggregate.Definition.Owner == (int)RmsDefinitionOwner.System) return NotFound();
			var version = aggregate.Published ?? aggregate.Latest ?? aggregate.Draft;
			if (version == null) return NotFound();
			var stored = await _printLayouts.GetDefinitionLayoutAsync(DepartmentId, aggregate.Definition.DefinitionKey);
			var department = await _printLayouts.GetDepartmentDefaultAsync(DepartmentId);
			var model = RecordDefinitionLayoutView.From(aggregate, version, stored, department.LayoutVersion);
			ReadTempData(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Layout(RecordDefinitionLayoutView model, CancellationToken cancellationToken)
		{
			if (!await RequireManageAsync()) return Forbid();
			var aggregate = await _definitions.GetAsync(DepartmentId, model.DefinitionKey);
			if (aggregate == null || aggregate.Definition.Owner == (int)RmsDefinitionOwner.System) return NotFound();
			try
			{
				await _printLayouts.SaveDefinitionLayoutAsync(DepartmentId, UserId, aggregate.Definition.DefinitionKey, model.ToConfig(), cancellationToken);
				TempData["RecordsMessage"] = _localizer["LayoutSaved"].Value;
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; }
			return RedirectToAction("Layout", new { key = aggregate.Definition.DefinitionKey });
		}

		private async Task PopulateEditAsync(RecordDefinitionEditView model)
		{
			model.Roles = ((await _roles.GetRolesForDepartmentAsync(DepartmentId)) ?? new List<PersonnelRole>()).OrderBy(r => r.Name).Select(r => new SelectListItem { Value = r.PersonnelRoleId.ToString(), Text = r.Name }).ToList();
			model.CanPublish = await CanPublishAsync();
			model.Schema ??= SafeParse(model.SchemaJson);
			if (model.Aggregate?.Definition.TemplateKey != null)
				model.TemplateDiff = _templates.DiffAgainstTemplate(model.Aggregate.Definition.TemplateKey, model.Aggregate.Definition.JurisdictionProfileKey, null, model.Schema, model.DefinitionKey, model.Version);
		}

		private static RecordDefinitionSchema SafeParse(string json)
		{
			try { return RecordDefinitionSchema.Parse(json); } catch (Newtonsoft.Json.JsonException) { return new RecordDefinitionSchema(); }
		}

		private void ReadTempData(RecordsBaseView model)
		{
			if (TempData["RecordsMessage"] is string message) model.Message = message;
			if (TempData["RecordsError"] is string error) model.ErrorMessage = error;
		}

		private Task<bool> RequireManageAsync() => _authorization.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ManageRecordDefinitions);
		private async Task<bool> CanPublishAsync() => ClaimsAuthorizationHelper.CanPublishRecordDefinitions() && await _authorization.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.PublishRecordDefinitions);
	}
}
