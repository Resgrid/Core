using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Globalization;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.DepartmentImport;
using Resgrid.Web.Attributes;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Import &amp; Migration (Department Onboarding and Migration Wizard plan, section 6). Every action re-checks
	/// IDepartmentImportAccessService.CanManageAsync (the master flag plus a current department administrator, the
	/// service-layer form of permission 72); read-only viewers do not exist for this module. Approval and rollback
	/// require a recent step-up. Script calls use same-origin JSON actions with the antiforgery header.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public sealed class DepartmentImportController : SecureBaseController
	{
		private readonly IDepartmentImportAccessService _access;
		private readonly IDepartmentImportService _imports;
		private readonly IImportUploadService _uploads;
		private readonly IImportMappingService _mapping;
		private readonly IImportIssueService _issues;
		private readonly IImportApprovalService _approval;
		private readonly IImportRunService _runs;
		private readonly IImportActivationService _activation;
		private readonly IDepartmentImportSupportService _support;
		private readonly IImportTemplateService _templates;
		private readonly IImportCatalog _catalog;
		private readonly IDepartmentSettingsService _settings;
		private readonly IInvitesService _invites;
		private readonly Model.Repositories.IImportInvitesRepository _importInvites;
		private readonly Model.Repositories.IImportReconciliationsRepository _reconciliations;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.DepartmentImport.DepartmentImport> _localizer;
		private readonly IImportConfigurationService _configuration;
		private readonly IModuleImpactService _moduleImpacts;
		private readonly IPermissionImpactService _permissionImpacts;
		private readonly IAdminAssistRepository _assistRepository;

		public DepartmentImportController(IDepartmentImportAccessService access, IDepartmentImportService imports, IImportUploadService uploads,
			IImportMappingService mapping, IImportIssueService issues, IImportApprovalService approval, IImportRunService runs, IImportActivationService activation,
			IDepartmentImportSupportService support, IImportTemplateService templates, IImportCatalog catalog, IDepartmentSettingsService settings,
			IInvitesService invites, Model.Repositories.IImportInvitesRepository importInvites, Model.Repositories.IImportReconciliationsRepository reconciliations,
			IStringLocalizer<Resgrid.Localization.Areas.User.DepartmentImport.DepartmentImport> localizer, IImportConfigurationService configuration,
			IModuleImpactService moduleImpacts, IPermissionImpactService permissionImpacts, IAdminAssistRepository assistRepository)
		{
			_configuration = configuration;
			_moduleImpacts = moduleImpacts;
			_permissionImpacts = permissionImpacts;
			_assistRepository = assistRepository;
			_access = access;
			_imports = imports;
			_uploads = uploads;
			_mapping = mapping;
			_issues = issues;
			_approval = approval;
			_runs = runs;
			_activation = activation;
			_support = support;
			_templates = templates;
			_catalog = catalog;
			_settings = settings;
			_invites = invites;
			_importInvites = importInvites;
			_reconciliations = reconciliations;
			_localizer = localizer;
		}

		// ---- Start / resume ---------------------------------------------------------------------------------------------

		[HttpGet]
		public async Task<IActionResult> Index(CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();

			var workspaces = await _imports.GetWorkspacesAsync(DepartmentId, cancellationToken);
			var model = new ImportIndexView
			{
				Workspaces = workspaces.OrderByDescending(w => w.LastActivityOn).ToList(),
				CanManage = true,
				StoreHealth = await _access.GetStoreHealthAsync(cancellationToken),
				RunInProgress = workspaces.Any(w => w.Status == (int)ImportWorkspaceStatus.Importing || w.Status == (int)ImportWorkspaceStatus.Verifying)
			};
			return View(model);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Create([FromForm] ImportCreateInput input, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();

			try
			{
				var workspace = await _imports.CreateWorkspaceAsync(DepartmentId, UserId, (ImportWorkspaceMode)input.Mode, input.Name, input.InternalReference,
					input.TargetActivationDate, cancellationToken);
				return RedirectToAction(nameof(Workspace), new { id = workspace.Id });
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
				return RedirectToAction(nameof(Index));
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> CancelWorkspace(string id, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _imports.CancelWorkspaceAsync(DepartmentId, UserId, id, cancellationToken);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Index));
		}

		// ---- Overview (hub) ---------------------------------------------------------------------------------------------

		[HttpGet]
		public async Task<IActionResult> Workspace(string id, CancellationToken cancellationToken)
		{
			var model = await PageAsync<ImportOverviewView>(id, cancellationToken);
			if (model == null)
				return NotFound();

			model.TopIssues = (await _issues.GetGroupsAsync(DepartmentId, id, null, true, cancellationToken))
				.OrderByDescending(g => g.Severity).ThenByDescending(g => g.OpenCount).Take(5).ToList();
			model.Requests = (await _support.GetRequestsAsync(DepartmentId, id, cancellationToken)).Take(5).ToList();
			model.Suggestions = (await _support.GetSuggestionsAsync(DepartmentId, id, cancellationToken)).Where(s => s.Status == 0).ToList();
			model.Events = await _imports.GetEventsAsync(DepartmentId, id, 0, 15, cancellationToken);
			var configuration = await _configuration.GetPendingAsync(DepartmentId, id, cancellationToken);
			model.ConfigurationPending = configuration.Changes.Count;
			model.ConfigurationDecided = configuration.Decided > 0;
			return View(model);
		}

		/// <summary>The hub's numbers, polled while background work runs (section 19.3).</summary>
		[HttpGet]
		public async Task<IActionResult> Summary(string id, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			var summary = await _imports.GetSummaryAsync(DepartmentId, id, cancellationToken);
			if (summary?.Workspace == null)
				return NotFound();

			return Json(new
			{
				status = summary.Workspace.Status,
				summary.RowsAnalysed,
				summary.RowsReady,
				summary.RowsNeedingAttention,
				summary.RowsImported,
				summary.PeopleToInvite,
				summary.OpenBlockingIssues,
				summary.ReadyDatasets,
				summary.TotalDatasets,
				runs = summary.OpenRuns.Select(r => new { r.Id, r.Kind, r.Status, r.LastErrorCode, r.CursorMovedOn }),
				datasets = summary.Datasets.Select(d => new { d.Id, d.Status, d.RowCount, d.ReadyCount, d.WarningCount, d.BlockedCount })
			});
		}

		// ---- Files ------------------------------------------------------------------------------------------------------

		[HttpGet]
		public async Task<IActionResult> Files(string id, CancellationToken cancellationToken)
		{
			var model = await PageAsync<ImportFilesView>(id, cancellationToken);
			if (model == null)
				return NotFound();

			model.Artifacts = (await _imports.GetArtifactsAsync(DepartmentId, id, cancellationToken)).OrderByDescending(a => a.CreatedOn).ToList();
			model.Datasets = await _imports.GetDatasetsAsync(DepartmentId, id, cancellationToken);
			model.Exportable = _templates.ExportableEntities;
			model.ChunkSizeBytes = Config.ImportConfig.ChunkSizeBytes;
			model.Continuation = await _imports.GetContinuationAsync(DepartmentId, id, cancellationToken);
			model.ContinuationCandidates = await _imports.GetContinuationCandidatesAsync(DepartmentId, id, cancellationToken);
			model.ContinuationLocked = model.Datasets.Any(d => d.Status == (int)ImportDatasetStatus.Importing || d.Status == (int)ImportDatasetStatus.Imported ||
				d.Status == (int)ImportDatasetStatus.Verified || d.CreatedCount + d.UpdatedCount + d.LinkedCount > 0);
			return View(model);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SetContinuation(string id, string earlierWorkspaceId, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				var source = await _imports.SetContinuationAsync(DepartmentId, UserId, ImportActorKind.Administrator, id,
					string.IsNullOrWhiteSpace(earlierWorkspaceId) ? null : earlierWorkspaceId, cancellationToken);
				TempData["ImportMessage"] = source == null ? _localizer["ContinuationClearedMessage"].Value : string.Format(_localizer["ContinuationSetMessage"].Value, source.Label);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Files), new { id });
		}

		/// <summary>Records a product the department is moving from (plan section 7.6): its glossary and its words for Resgrid's features.</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> AddProduct(string id, string productKey, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				var source = await _imports.AddSourceAsync(DepartmentId, UserId, ImportActorKind.Administrator, id, productKey, cancellationToken);
				TempData["ImportMessage"] = string.Format(_localizer["ProductAddedMessage"].Value, source.Label);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Files), null, new { id }, "import-products");
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> RemoveProduct(string id, string sourceId, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _imports.RemoveSourceAsync(DepartmentId, UserId, ImportActorKind.Administrator, id, sourceId, cancellationToken);
				TempData["ImportMessage"] = _localizer["ProductRemovedMessage"].Value;
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Files), null, new { id }, "import-products");
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> UploadStart([FromBody] ImportUploadStartInput input, CancellationToken cancellationToken) =>
			JsonAsync(() =>
			{
				RequireBody(input);
				return _uploads.StartAsync(DepartmentId, UserId, input.WorkspaceId, input.FileName, input.SizeBytes, input.Sha256, input.ContentType,
					input.SourceId, false, cancellationToken);
			});

		/// <summary>One chunk of an upload: the raw bytes are the request body (no form encoding, no base64).</summary>
		[HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16 * 1024 * 1024)]
		public async Task<IActionResult> UploadChunk(string artifactId, int part, string sha256, CancellationToken cancellationToken)
		{
			using var buffer = new System.IO.MemoryStream();
			await Request.Body.CopyToAsync(buffer, cancellationToken);
			return await JsonAsync(() => _uploads.AppendAsync(DepartmentId, artifactId, part, buffer.ToArray(), sha256, cancellationToken));
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> UploadComplete(string artifactId, CancellationToken cancellationToken) =>
			JsonAsync(() => _uploads.CompleteAsync(DepartmentId, UserId, artifactId, cancellationToken));

		[HttpGet]
		public Task<IActionResult> UploadStatus(string artifactId, CancellationToken cancellationToken) =>
			JsonAsync(() => _uploads.GetStatusAsync(DepartmentId, artifactId, cancellationToken));

		[HttpGet]
		public async Task<IActionResult> Template(string entities, string format, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();

			var keys = (entities ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
			try
			{
				var file = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase) && keys.Count == 1
					? await _templates.BuildCsvTemplateAsync(DepartmentId, keys[0], Localize, cancellationToken)
					: await _templates.BuildStarterWorkbookAsync(DepartmentId, keys, Localize, cancellationToken);
				return File(file.Content, file.ContentType, file.FileName);
			}
			catch (DepartmentImportException)
			{
				return NotFound();
			}
		}

		[HttpGet]
		public async Task<IActionResult> CurrentData(string entities, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();

			var keys = (entities ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
			try
			{
				var file = await _templates.ExportCurrentDataAsync(DepartmentId, keys, Localize, cancellationToken);
				return File(file.Content, file.ContentType, file.FileName);
			}
			catch (DepartmentImportException)
			{
				return NotFound();
			}
		}

		// ---- Data set: destination, column matching and values -----------------------------------------------------

		[HttpGet]
		public async Task<IActionResult> Dataset(string id, string datasetId, CancellationToken cancellationToken)
		{
			var model = await PageAsync<ImportDatasetView>(id, cancellationToken);
			if (model == null)
				return NotFound();

			try
			{
				model.Mapping = await _mapping.GetMappingAsync(DepartmentId, datasetId, cancellationToken);
			}
			catch (DepartmentImportException)
			{
				return NotFound();
			}
			if (model.Mapping?.Dataset == null || model.Mapping.Dataset.WorkspaceId != id)
				return NotFound();

			model.Issues = await _issues.GetGroupsAsync(DepartmentId, id, datasetId, true, cancellationToken);
			if (model.Mapping.Dataset.EntityKey != null)
				model.SavedMaps = await _mapping.GetSavedMapsAsync(DepartmentId, model.Mapping.Dataset.EntityKey, cancellationToken);
			return View(model);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SetDestination(string id, string datasetId, string entityKey, int revision, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _imports.SetDestinationAsync(DepartmentId, UserId, ImportActorKind.Administrator, datasetId, entityKey, revision, cancellationToken);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Dataset), new { id, datasetId });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ExcludeDataset(string id, string datasetId, bool excluded, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _imports.ExcludeDatasetAsync(DepartmentId, UserId, ImportActorKind.Administrator, datasetId, excluded, cancellationToken);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Files), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveMapping([FromBody] ImportMappingInput input, CancellationToken cancellationToken) =>
			JsonAsync(async () =>
			{
				RequireBody(input);
				var map = await _mapping.SaveColumnMapAsync(DepartmentId, UserId, ImportActorKind.Administrator, input.DatasetId, input.Body, input.ExpectedRevision,
					cancellationToken);
				return new { map.Id, map.Version };
			});

		/// <summary>The day/month answer for one column, from the Issues page or the data set page.</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SetDateFormat(string id, string datasetId, string column, string format, string returnTo, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _mapping.SetDateFormatAsync(DepartmentId, UserId, ImportActorKind.Administrator, datasetId, column, format, cancellationToken);
				TempData["ImportMessage"] = _localizer["DateFormatSaved"].Value;
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return returnTo == "dataset" ? RedirectToAction(nameof(Dataset), new { id, datasetId }) : RedirectToAction(nameof(Issues), new { id, datasetId });
		}

		/// <summary>The same value for every row of a field, from the Issues page (the data set page saves it with the mapping).</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SetConstant(string id, string datasetId, string fieldKey, string value, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _mapping.SetConstantAsync(DepartmentId, UserId, ImportActorKind.Administrator, datasetId, fieldKey, value, cancellationToken);
				TempData["ImportMessage"] = _localizer["SameValueSaved"].Value;
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Issues), new { id, datasetId });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ApplySavedMap(string id, string datasetId, string mapId, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _mapping.ApplySavedMapAsync(DepartmentId, UserId, ImportActorKind.Administrator, datasetId, mapId, cancellationToken);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Dataset), new { id, datasetId });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveMapAs(string id, string datasetId, string name, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _mapping.SaveMapAsAsync(DepartmentId, UserId, datasetId, name, cancellationToken);
				TempData["ImportMessage"] = _localizer["MapSaved"].Value;
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Dataset), new { id, datasetId });
		}

		[HttpGet]
		public async Task<IActionResult> Values(string id, string datasetId, string field, CancellationToken cancellationToken)
		{
			var model = await PageAsync<ImportValuesView>(id, cancellationToken);
			if (model == null)
				return NotFound();

			model.Dataset = await _imports.GetDatasetAsync(DepartmentId, datasetId, cancellationToken);
			if (model.Dataset == null || model.Dataset.WorkspaceId != id || model.Dataset.EntityKey == null)
				return NotFound();
			model.Contract = (_catalog.GetVersion(model.Workspace.CatalogVersion) ?? _catalog).GetContract(model.Dataset.EntityKey);
			model.Field = model.Contract?.Field(field);
			if (model.Field == null)
				return NotFound();

			model.Candidates = await _mapping.GetValueCandidatesAsync(DepartmentId, datasetId, field, cancellationToken);
			return View(model);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> SaveValues([FromBody] ImportValuesInput input, CancellationToken cancellationToken) =>
			JsonAsync(async () =>
			{
				RequireBody(input);
				return new
				{
					saved = await _mapping.SaveValueMapsAsync(DepartmentId, UserId, ImportActorKind.Administrator, input.DatasetId, input.FieldKey, input.Decisions,
						cancellationToken)
				};
			});

		// ---- Issues -----------------------------------------------------------------------------------------------------

		[HttpGet]
		public async Task<IActionResult> Issues(string id, string datasetId, bool all = false, CancellationToken cancellationToken = default)
		{
			var model = await PageAsync<ImportIssuesView>(id, cancellationToken);
			if (model == null)
				return NotFound();

			model.DatasetId = datasetId;
			model.OpenOnly = !all;
			model.Datasets = await _imports.GetDatasetsAsync(DepartmentId, id, cancellationToken);
			model.Groups = (await _issues.GetGroupsAsync(DepartmentId, id, datasetId, !all, cancellationToken))
				.OrderByDescending(g => g.Severity).ThenBy(g => g.DatasetName).ThenBy(g => g.Code).ToList();
			return View(model);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> IssueAction([FromBody] ImportIssueActionInput input, CancellationToken cancellationToken) =>
			JsonAsync(async () =>
			{
				RequireBody(input);
				var ids = input.IssueIds ?? new List<string>();
				var changed = input.Action switch
				{
					"accept" => await _issues.AcceptAsync(DepartmentId, UserId, ImportActorKind.Administrator, input.WorkspaceId, ids, cancellationToken),
					"exclude" => await _issues.ExcludeRowsAsync(DepartmentId, UserId, ImportActorKind.Administrator, input.WorkspaceId, ids, cancellationToken),
					"reopen" => await _issues.ReopenAsync(DepartmentId, UserId, ImportActorKind.Administrator, input.WorkspaceId, ids, cancellationToken),
					"set" => await _issues.SetValueForIssuesAsync(DepartmentId, UserId, ImportActorKind.Administrator, input.WorkspaceId, ids, input.Value, cancellationToken),
					_ => throw new DepartmentImportException("import_value_action_invalid")
				};
				return new { changed };
			});

		[HttpGet]
		public Task<IActionResult> Row(string stagedRowId, CancellationToken cancellationToken) =>
			JsonAsync(() => _issues.GetRowAsync(DepartmentId, stagedRowId, cancellationToken));

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> EditValue(string stagedRowId, string column, string value, CancellationToken cancellationToken) =>
			JsonAsync(async () =>
			{
				await _issues.EditValueAsync(DepartmentId, UserId, ImportActorKind.Administrator, stagedRowId, column, value, cancellationToken);
				return new { ok = true };
			});

		// ---- Configure (step 9) -----------------------------------------------------------------------------------------

		[HttpGet]
		public async Task<IActionResult> Configure(string id, string group, CancellationToken cancellationToken)
		{
			var model = await PageAsync<ImportConfigureView>(id, cancellationToken);
			if (model == null)
				return NotFound();

			model.Configuration = await _configuration.GetAsync(DepartmentId, UserId, id, cancellationToken);
			model.Impacts = await ImpactsAsync(model.Configuration, cancellationToken);
			model.OpenGroup = ImportConfigurationGroups.All.Contains(group) ? group : null;
			return View(model);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ConfigureDecide(string id, [FromForm] ImportConfigureInput input, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();

			var item = ImportConfigurationCatalog.Find(input?.ItemKey);
			try
			{
				await _configuration.DecideAsync(DepartmentId, UserId, ImportActorKind.Administrator, id, new ImportConfigurationDecision
				{
					ItemKey = input?.ItemKey,
					Accept = !string.Equals(input?.Decision, "reject", StringComparison.Ordinal),
					UseSuggestion = string.Equals(input?.Decision, "suggestion", StringComparison.Ordinal),
					Value = item == null ? input?.Value : ConfigurationValue(item, input)
				}, cancellationToken);
				TempData["ImportMessage"] = _localizer["ConfigSaved"].Value;
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Configure), new { id, group = item?.Group });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ConfigureAcceptAll(string id, string group, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				var accepted = await _configuration.AcceptSuggestionsAsync(DepartmentId, UserId, ImportActorKind.Administrator, id, group, cancellationToken);
				TempData["ImportMessage"] = string.Format(_localizer["ConfigAcceptedCount"].Value, accepted);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Configure), new { id, group });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ConfigureReset(string id, string itemKey, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _configuration.ResetAsync(DepartmentId, UserId, ImportActorKind.Administrator, id, itemKey, cancellationToken);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Configure), new { id, group = ImportConfigurationCatalog.Find(itemKey)?.Group });
		}

		[HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(3 * 1024 * 1024)]
		public async Task<IActionResult> ConfigureLogo(string id, IFormFile file, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				if (file == null || file.Length == 0 || file.Length > Resgrid.Services.DepartmentLogoRenditions.MaxBytes)
					throw new DepartmentImportException("IMP-CONFIG-LOGO-REJECTED");

				await using var stream = new System.IO.MemoryStream();
				await file.CopyToAsync(stream, cancellationToken);
				var logo = await _configuration.UploadLogoAsync(DepartmentId, UserId, ImportActorKind.Administrator, id, file.FileName, file.ContentType, stream.ToArray(),
					cancellationToken);
				// Uploading is choosing: the new logo is the accepted value (it can still be changed or undone).
				await _configuration.DecideAsync(DepartmentId, UserId, ImportActorKind.Administrator, id,
					new ImportConfigurationDecision { ItemKey = "branding.logo", Value = logo.Id }, cancellationToken);
				TempData["ImportMessage"] = _localizer["ConfigSaved"].Value;
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Configure), new { id, group = ImportConfigurationGroups.Branding });
		}

		/// <summary>The posted parts of a value for its kind (an address, coordinates, a permission...), before the service validates it.</summary>
		public static string ConfigurationValue(ImportConfigurationItem item, ImportConfigureInput input)
		{
			switch (item.Kind)
			{
				case ImportConfigurationValueKind.Coordinates:
					return string.IsNullOrWhiteSpace(input.Latitude) && string.IsNullOrWhiteSpace(input.Longitude)
						? input.Value
						: (input.Latitude ?? string.Empty).Trim() + "," + (input.Longitude ?? string.Empty).Trim();
				case ImportConfigurationValueKind.Address:
					return Newtonsoft.Json.JsonConvert.SerializeObject(new ImportConfigurationAddress
					{
						Address1 = input.Address1, City = input.City, State = input.State, PostalCode = input.PostalCode, Country = input.Country
					});
				case ImportConfigurationValueKind.MonthDay:
					return input.Month.HasValue && input.Day.HasValue ? ImportConfigurationCatalog.MonthDay(input.Month.Value, input.Day.Value) : input.Value;
				case ImportConfigurationValueKind.Permission:
					return input.Action.HasValue ? ImportConfigurationCatalog.Permission(input.Action.Value, input.LockToGroup, input.Roles) : input.Value;
				default:
					return input.Value;
			}
		}

		/// <summary>
		/// Admin Assist's own previews (section 8.1 A) for the module switches and permissions a decision would change; a
		/// preview Admin Assist cannot give is left out rather than guessed.
		/// </summary>
		private async Task<Dictionary<string, ConfigurationImpactReport>> ImpactsAsync(ImportConfigurationView view, CancellationToken cancellationToken)
		{
			var reports = new Dictionary<string, ConfigurationImpactReport>(StringComparer.Ordinal);
			if (view?.PermissionPreviewAvailable != true)
				return reports;

			string revision;
			try
			{
				revision = (await _assistRepository.GetConfigurationRevisionAsync(DepartmentId, cancellationToken)).ToString(CultureInfo.InvariantCulture);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Framework.Logging.LogException(ex);
				return reports;
			}

			var actor = new AdminAssistActor(DepartmentId, UserId, CultureInfo.CurrentUICulture.Name);
			foreach (var row in view.Groups.SelectMany(g => g.Rows).Where(r => r.Pending || r.AwaitingDecision))
			{
				var value = row.Pending ? row.Change.Value : row.Suggested;
				try
				{
					if (row.Item.Group == ImportConfigurationGroups.Modules && ModuleImpactSelection.Supported.Contains(row.Item.Target))
					{
						reports[row.Item.Key] = await _moduleImpacts.PreviewAsync(actor, new ModuleImpactRequest(revision, row.Item.Target, value != "true"), cancellationToken);
					}
					else if (row.Item.Group == ImportConfigurationGroups.Access)
					{
						var permission = ImportConfigurationCatalog.ReadJson<ImportConfigurationPermission>(value);
						if (permission != null)
							reports[row.Item.Key] = await _permissionImpacts.PreviewAsync(actor, new PermissionImpactRequest(revision, row.Item.Target, permission.Action,
								permission.LockToGroup, (permission.Roles ?? new List<int>()).ToArray()), cancellationToken);
					}
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					Framework.Logging.LogException(ex);
				}
			}

			return reports;
		}

		// ---- Review & import --------------------------------------------------------------------------------------------

		[HttpGet]
		public async Task<IActionResult> Review(string id, CancellationToken cancellationToken)
		{
			var model = await PageAsync<ImportReviewView>(id, cancellationToken);
			if (model == null)
				return NotFound();

			model.Preview = await _approval.GetPreviewAsync(DepartmentId, id, cancellationToken);
			model.ManagedMigration = await _access.IsManagedMigrationEnabledAsync(DepartmentId);
			model.ReplaceAvailable = await _imports.HasReplaceSupportAsync(DepartmentId, id, cancellationToken);
			return View(model);
		}

		/// <summary>How existing records are treated; the preview is planned again for the new choice.</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SetWriteMode(string id, int writeMode, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _imports.SetWriteModeAsync(DepartmentId, UserId, ImportActorKind.Administrator, id, (ImportWriteMode)writeMode, cancellationToken);
				TempData["ImportMessage"] = _localizer["WriteModeChangedMessage"].Value;
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Review), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 10)]
		public async Task<IActionResult> Approve([FromForm] ImportApproveInput input, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _approval.ApproveAsync(DepartmentId, UserId, input.WorkspaceId, new ImportApprovalRequest
				{
					DatasetIds = input.DatasetIds ?? new List<string>(),
					WriteMode = (ImportWriteMode)input.WriteMode,
					AcknowledgedConsequences = input.Acknowledged ?? new List<string>(),
					SuppressExpiredCertificationNotices = input.SuppressExpiredCertificationNotices
				}, cancellationToken);
				return RedirectToAction(nameof(History), new { id = input.WorkspaceId });
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
				return RedirectToAction(nameof(Review), new { id = input.WorkspaceId });
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 10)]
		public async Task<IActionResult> ApproveAsChecker(string id, string runId, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _approval.ApproveAsCheckerAsync(DepartmentId, UserId, runId, cancellationToken);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(History), new { id });
		}

		// ---- History, run control and rollback ------------------------------------------------------------------------

		[HttpGet]
		public async Task<IActionResult> History(string id, CancellationToken cancellationToken)
		{
			var model = await PageAsync<ImportHistoryView>(id, cancellationToken);
			if (model == null)
				return NotFound();

			model.Runs = (await _runs.GetRunsAsync(DepartmentId, id, cancellationToken)).OrderByDescending(r => r.RequestedOn).ToList();
			model.Datasets = await _imports.GetDatasetsAsync(DepartmentId, id, cancellationToken);
			foreach (var run in model.Runs.Where(r => r.Kind == (int)ImportRunKind.Commit || r.Kind == (int)ImportRunKind.Rollback))
				model.Reconciliations[run.Id] = await _reconciliations.GetForRunAsync(DepartmentId, run.Id, cancellationToken);
			foreach (var run in model.Runs.Where(r => r.Kind == (int)ImportRunKind.Commit &&
				(r.Status == (int)ImportRunStatus.Completed || r.Status == (int)ImportRunStatus.Cancelled)))
				model.Rollbacks[run.Id] = await _approval.GetRollbackPreviewAsync(DepartmentId, run.Id, cancellationToken);
			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> RunStatus(string runId, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			var run = await _runs.GetRunAsync(DepartmentId, runId, cancellationToken);
			if (run == null)
				return NotFound();
			return Json(new { run.Id, run.Kind, run.Status, run.Counts, run.LastErrorCode, run.CursorMovedOn, run.CompletedOn, run.Attempts, run.NextAttemptOn });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> RunControl(string id, string runId, string control, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				if (control == "resume")
					await _runs.ResumeAsync(DepartmentId, UserId, runId, cancellationToken);
				else
					await _runs.RequestCancelAsync(DepartmentId, UserId, runId, control == "cancel" ? ImportRunCancelRequest.Cancel : ImportRunCancelRequest.Pause, cancellationToken);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(History), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 10)]
		public async Task<IActionResult> Rollback(string id, string runId, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _approval.RequestRollbackAsync(DepartmentId, UserId, runId, cancellationToken);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(History), new { id });
		}

		// ---- Verify & activate -----------------------------------------------------------------------------------------

		[HttpGet]
		public async Task<IActionResult> Verify(string id, CancellationToken cancellationToken)
		{
			var model = await PageAsync<ImportVerifyView>(id, cancellationToken);
			if (model == null)
				return NotFound();

			model.Checklist = await _activation.GetChecklistAsync(DepartmentId, id, cancellationToken);
			model.Invitations = (await _importInvites.GetForWorkspaceAsync(DepartmentId, id, cancellationToken)).OrderByDescending(i => i.InviteId).ToList();
			model.ManagedRuns = await _activation.GetManagedRunsAsync(DepartmentId, id, cancellationToken);
			return View(model);
		}

		/// <summary>An administrator approves a staff-requested activation step or invitation send (the third signature; also ends its notice period).</summary>
		[HttpPost, ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 10)]
		public Task<IActionResult> ApproveManagedRun(string id, string runId, CancellationToken cancellationToken) =>
			DecideManagedRunAsync(id, runId, true, cancellationToken);

		/// <summary>An administrator cancels a staff-requested activation step or invitation send before it is carried out.</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> CancelManagedRun(string id, string runId, CancellationToken cancellationToken) =>
			DecideManagedRunAsync(id, runId, false, cancellationToken);

		private async Task<IActionResult> DecideManagedRunAsync(string id, string runId, bool approve, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _activation.DecideManagedRunAsync(DepartmentId, UserId, runId, approve, cancellationToken);
				TempData["ImportMessage"] = _localizer[approve ? "ManagedRunApprovedMessage" : "ManagedRunCancelledMessage"].Value;
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Verify), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Invite([FromForm] ImportInviteInput input, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				var count = await _activation.InvitePeopleAsync(DepartmentId, UserId, input.WorkspaceId, new ImportInvitationRequest
				{
					UserIds = input.UserIds ?? new List<string>(),
					IncludeExistingAccounts = input.IncludeExistingAccounts,
					SendAfter = input.SendAfter?.ToUniversalTime()
				}, cancellationToken);
				TempData["ImportMessage"] = string.Format(_localizer["InvitationsCreated"].Value, count);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Verify), new { id = input.WorkspaceId });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> InvitationControl(string id, int inviteId, string control, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			if (control == "revoke")
				await _activation.RevokeInvitationAsync(DepartmentId, UserId, inviteId, cancellationToken);
			else
				await _activation.ResendInvitationAsync(DepartmentId, UserId, inviteId, cancellationToken);
			return RedirectToAction(nameof(Verify), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ApplyAdministrators(string id, List<string> userIds, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _activation.ApplyAdministratorFlagsAsync(DepartmentId, UserId, id, userIds ?? new List<string>(), cancellationToken);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Verify), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 10)]
		public async Task<IActionResult> SignOff(string id, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _activation.SignOffAsync(DepartmentId, UserId, id, cancellationToken);
				TempData["ImportMessage"] = _localizer["SignedOffMessage"].Value;
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Verify), new { id });
		}

		// ---- Support ----------------------------------------------------------------------------------------------------

		[HttpGet]
		public async Task<IActionResult> Support(string id, string datasetId, string runId, string issueCode, CancellationToken cancellationToken)
		{
			var model = await PageAsync<ImportSupportView>(id, cancellationToken);
			if (model == null)
				return NotFound();

			model.Requests = await _support.GetRequestsAsync(DepartmentId, id, cancellationToken);
			model.Messages = await _support.GetThreadAsync(DepartmentId, id, 0, 100, true, cancellationToken);
			model.Grants = await _support.GetGrantsAsync(DepartmentId, id, cancellationToken);
			model.Suggestions = await _support.GetSuggestionsAsync(DepartmentId, id, cancellationToken);
			model.ManagedMigration = await _access.IsManagedMigrationEnabledAsync(DepartmentId);
			model.SupportGrantDefaultDays = (await _settings.GetDepartmentImportConfigAsync(DepartmentId)).EffectiveSupportGrantDays();
			model.RequestDatasetId = datasetId;
			model.RequestRunId = runId;
			model.RequestIssueCode = issueCode;
			return View(model);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> OpenRequest(string id, int category, string subject, string description, string datasetId, string runId, string issueCode,
			CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _support.OpenRequestAsync(DepartmentId, UserId, id, (ImportSupportRequestCategory)category, subject, description, datasetId, runId,
					string.IsNullOrWhiteSpace(issueCode) ? Array.Empty<string>() : new[] { issueCode }, cancellationToken);
				TempData["ImportMessage"] = _localizer["RequestOpened"].Value;
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Support), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> PostMessage(string id, string body, string requestId, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _support.PostMessageAsync(DepartmentId, UserId, id, body, requestId, cancellationToken);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Support), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> RequestControl(string id, string requestId, bool reopen, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _support.ConfirmOrReopenAsync(DepartmentId, UserId, requestId, reopen, cancellationToken);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Support), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[RequiresRecentTwoFactor(RequireForOperation = true, VerificationWindowMinutes = 10)]
		public async Task<IActionResult> GrantAccess(string id, int level, int days, bool requireSignOff, string message, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _support.GrantAccessAsync(DepartmentId, UserId, id, (ImportSupportAccessLevel)level, days, ImportOperateActions.Default, requireSignOff, message,
					IpAddressHelper.GetRequestIP(Request, true), cancellationToken);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Support), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> RevokeAccess(string id, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			await _support.RevokeAccessAsync(DepartmentId, UserId, id, IpAddressHelper.GetRequestIP(Request, true), cancellationToken);
			return RedirectToAction(nameof(Support), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> DecideSuggestion(string id, string suggestionId, bool accept, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();
			try
			{
				await _support.DecideSuggestionAsync(DepartmentId, UserId, suggestionId, accept, cancellationToken);
			}
			catch (DepartmentImportException ex)
			{
				TempData["ImportError"] = Message(ex.Code);
			}
			return RedirectToAction(nameof(Support), new { id });
		}

		// ---- Settings ---------------------------------------------------------------------------------------------------

		[HttpGet]
		public async Task<IActionResult> Settings(CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();

			var config = await _settings.GetDepartmentImportConfigAsync(DepartmentId) ?? new DepartmentImportConfig();
			return View(new ImportSettingsView
			{
				CanManage = true,
				ArtifactRetentionDaysAfterSuccess = config.ArtifactRetentionDaysAfterSuccess,
				ArtifactRetentionDaysAfterFailure = config.ArtifactRetentionDaysAfterFailure,
				StagingRetentionDaysAfterActivation = config.StagingRetentionDaysAfterActivation,
				RequireSecondApprover = config.RequireSecondApprover,
				DefaultWriteMode = config.DefaultWriteMode == 0 ? (int)ImportWriteMode.CreateOnly : config.DefaultWriteMode,
				SupportGrantDefaultDays = config.SupportGrantDefaultDays,
				Saved = TempData["ImportSettingsSaved"] as bool? == true
			});
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Settings([FromForm] ImportSettingsView input, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Unauthorized();

			static int Days(int value, int max) => Math.Max(0, Math.Min(max, value));
			var config = new DepartmentImportConfig
			{
				ArtifactRetentionDaysAfterSuccess = Days(input.ArtifactRetentionDaysAfterSuccess, DepartmentImportConfig.MaxRetentionDays),
				ArtifactRetentionDaysAfterFailure = Days(input.ArtifactRetentionDaysAfterFailure, DepartmentImportConfig.MaxRetentionDays),
				StagingRetentionDaysAfterActivation = Days(input.StagingRetentionDaysAfterActivation, DepartmentImportConfig.MaxRetentionDays),
				RequireSecondApprover = input.RequireSecondApprover,
				// Replace is support-assisted only (section 5.2): never a department default.
				DefaultWriteMode = input.DefaultWriteMode == (int)ImportWriteMode.ReviewedUpdate ? (int)ImportWriteMode.ReviewedUpdate : (int)ImportWriteMode.CreateOnly,
				SupportGrantDefaultDays = Days(input.SupportGrantDefaultDays, DepartmentImportConfig.MaxSupportGrantDays)
			};
			await _settings.SetDepartmentImportConfigAsync(DepartmentId, config, cancellationToken);
			TempData["ImportSettingsSaved"] = true;
			return RedirectToAction(nameof(Settings));
		}

		// ---- helpers ----------------------------------------------------------------------------------------------------

		private Task<bool> CanManageAsync() => _access.CanManageAsync(DepartmentId, UserId);

		private async Task<T> PageAsync<T>(string workspaceId, CancellationToken cancellationToken) where T : ImportPageView, new()
		{
			if (string.IsNullOrWhiteSpace(workspaceId) || !await CanManageAsync())
				return null;

			var summary = await _imports.GetSummaryAsync(DepartmentId, workspaceId, cancellationToken);
			if (summary?.Workspace == null)
				return null;

			return new T
			{
				Workspace = summary.Workspace,
				Summary = summary,
				ActiveGrant = await _support.GetActiveGrantAsync(DepartmentId, workspaceId, cancellationToken),
				CanManage = true,
				Catalog = _catalog.GetVersion(summary.Workspace.CatalogVersion) ?? _catalog,
				Sources = await _imports.GetSourcesAsync(DepartmentId, workspaceId, cancellationToken) ?? new List<ImportSource>()
			};
		}

		private string Localize(string key)
		{
			var value = _localizer[key];
			return value.ResourceNotFound ? key : value.Value;
		}

		/// <summary>A refusal code's text: "Error.{code}" when localized, otherwise the generic failure text.</summary>
		private string Message(string code)
		{
			var value = _localizer["Error." + code];
			return value.ResourceNotFound ? _localizer["Error.Generic"].Value : value.Value;
		}

		/// <summary>An empty or malformed JSON body binds to null: a controlled import error rather than a null dereference (a 500).</summary>
		private static void RequireBody(object input)
		{
			if (input == null)
				throw new DepartmentImportException("import_unknown_field");
		}

		private async Task<IActionResult> JsonAsync<T>(Func<Task<T>> action)
		{
			if (!await CanManageAsync())
				return StatusCode(StatusCodes.Status403Forbidden, new { code = "import_not_allowed", message = Message("import_not_allowed") });

			try
			{
				return Json(await action());
			}
			catch (DepartmentImportException ex)
			{
				return StatusCode(ex.StatusCode, new { code = ex.Code, message = Message(ex.Code) });
			}
			catch (ImportArtifactIntegrityException ex)
			{
				return StatusCode(StatusCodes.Status409Conflict, new { code = ex.Code, message = Message(ex.Code) });
			}
		}
	}
}
