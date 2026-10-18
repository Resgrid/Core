using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Import &amp; Migration for automation and future clients (wizard plan section 19.2): read-mostly — contracts, templates,
	/// workspaces and progress — plus chunked upload and starting an analysis. Approval, commit, rollback and activation
	/// are interactive (step-up) and exist only in the web application; department API keys gain no import scope.
	/// Every call re-checks that the caller is a current administrator of a department with the module on.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]"), ApiVersion("4.0"), ApiExplorerSettings(GroupName = "v4"), Authorize]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public sealed class DepartmentImportController : V4AuthenticatedApiControllerbase
	{
		private readonly IDepartmentImportAccessService _access;
		private readonly IDepartmentImportService _imports;
		private readonly IImportUploadService _uploads;
		private readonly IImportMappingService _mapping;
		private readonly IImportTemplateService _templates;
		private readonly IImportCatalog _catalog;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.DepartmentImport.DepartmentImport> _strings;

		public DepartmentImportController(IDepartmentImportAccessService access, IDepartmentImportService imports, IImportUploadService uploads,
			IImportMappingService mapping, IImportTemplateService templates, IImportCatalog catalog,
			IStringLocalizer<Resgrid.Localization.Areas.User.DepartmentImport.DepartmentImport> strings)
		{
			_access = access;
			_imports = imports;
			_uploads = uploads;
			_mapping = mapping;
			_templates = templates;
			_catalog = catalog;
			_strings = strings;
		}

		/// <summary>The import entity contracts (field keys, types, required flags, choices and references) of the current catalog.</summary>
		[HttpGet("GetContracts")]
		public Task<IActionResult> GetContracts() => Run(() => Task.FromResult<object>(new
		{
			_catalog.Version,
			Contracts = _catalog.InDependencyOrder().Select(c => new
			{
				c.Key,
				c.Version,
				Label = Localize(c.LabelKey),
				c.DependsOn,
				c.IdentityFields,
				c.NaturalKey,
				Fields = c.Fields.Select(f => new
				{
					f.Key,
					Label = Localize(f.LabelKey),
					f.Type,
					f.ItemType,
					f.Required,
					f.MaxLength,
					f.References,
					Choices = f.Choices.Select(ch => ch.Code)
				})
			})
		}));

		/// <summary>The starter workbook (format xlsx, any entities) or one entity's CSV template (format csv).</summary>
		[HttpGet("GetTemplate")]
		public async Task<IActionResult> GetTemplate(string entities, string format, CancellationToken cancellationToken)
		{
			if (!await CanManageAsync())
				return Forbid();

			var keys = (entities ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
			try
			{
				var file = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase) && keys.Count == 1
					? await _templates.BuildCsvTemplateAsync(DepartmentId, keys[0], Localize, cancellationToken)
					: await _templates.BuildStarterWorkbookAsync(DepartmentId, keys, Localize, cancellationToken);
				return File(file.Content, file.ContentType, file.FileName);
			}
			catch (DepartmentImportException ex)
			{
				return Problem(ex);
			}
		}

		[HttpGet("GetWorkspaces")]
		public Task<IActionResult> GetWorkspaces(CancellationToken cancellationToken) => Run(async () =>
			(await _imports.GetWorkspacesAsync(DepartmentId, cancellationToken)).Select(w => new
			{
				w.Id,
				w.Name,
				w.Mode,
				w.Status,
				w.TargetActivationDate,
				w.CreatedOn,
				w.LastActivityOn,
				w.ActivatedOn
			}));

		[HttpGet("GetWorkspace")]
		public Task<IActionResult> GetWorkspace(string id, CancellationToken cancellationToken) => Run(async () =>
		{
			var summary = await _imports.GetSummaryAsync(DepartmentId, id, cancellationToken) ?? throw new DepartmentImportException("import_workspace_not_found", 404);
			return (object)new
			{
				summary.Workspace.Id,
				summary.Workspace.Name,
				summary.Workspace.Mode,
				summary.Workspace.Status,
				Datasets = summary.Datasets.Select(d => new
				{
					d.Id,
					d.Name,
					d.EntityKey,
					d.Status,
					d.RowCount,
					d.ReadyCount,
					d.WarningCount,
					d.BlockedCount,
					d.CreatedCount,
					d.UpdatedCount,
					d.LinkedCount,
					d.SkippedCount,
					d.FailedCount
				})
			};
		});

		/// <summary>Progress for polling (section 19.3): every 3 s while a run is running, every 15 s otherwise.</summary>
		[HttpGet("GetProgress")]
		public Task<IActionResult> GetProgress(string id, CancellationToken cancellationToken) => Run(async () =>
		{
			var summary = await _imports.GetSummaryAsync(DepartmentId, id, cancellationToken) ?? throw new DepartmentImportException("import_workspace_not_found", 404);
			return (object)new
			{
				summary.Workspace.Status,
				summary.RowsAnalysed,
				summary.RowsReady,
				summary.RowsNeedingAttention,
				summary.RowsImported,
				summary.PeopleToInvite,
				summary.OpenBlockingIssues,
				summary.UnreadMessages,
				NextAction = summary.NextActionKey == null ? null : string.Format(Localize(summary.NextActionKey), summary.NextActionArgs.Cast<object>().ToArray()),
				Runs = summary.OpenRuns.Select(r => new
				{
					r.Id,
					r.Kind,
					r.Status,
					r.LastErrorCode,
					r.Attempts,
					r.NextAttemptOn,
					r.CursorMovedOn,
					r.HeartbeatOn
				}),
				PollSeconds = summary.OpenRuns.Any(r => r.Status == (int)ImportRunStatus.Running || r.Status == (int)ImportRunStatus.Reconciling) ? 3 : 15
			};
		});

		[HttpPost("StartUpload")]
		public Task<IActionResult> StartUpload([FromBody] ImportUploadStartRequest request, CancellationToken cancellationToken) => Run(async () =>
		{
			if (request == null)
				throw new DepartmentImportException("import_unknown_field");
			return (object)await _uploads.StartAsync(DepartmentId, UserId, request.WorkspaceId, request.FileName, request.SizeBytes, request.Sha256, request.ContentType,
				request.SourceId, false, cancellationToken);
		});

		/// <summary>One chunk: the raw request body, 1-based part number and the chunk's SHA-256 (hex).</summary>
		[HttpPost("UploadChunk"), RequestSizeLimit(16 * 1024 * 1024)]
		public Task<IActionResult> UploadChunk(string artifactId, int part, string sha256, CancellationToken cancellationToken) => Run(async () =>
		{
			using var buffer = new MemoryStream();
			await Request.Body.CopyToAsync(buffer, cancellationToken);
			return (object)await _uploads.AppendAsync(DepartmentId, artifactId, part, buffer.ToArray(), sha256, cancellationToken);
		});

		/// <summary>Completes the upload; the analysis run (scan, detection, staging, checks) starts in the background.</summary>
		[HttpPost("CompleteUpload")]
		public Task<IActionResult> CompleteUpload(string artifactId, CancellationToken cancellationToken) => Run(async () =>
			(object)await _uploads.CompleteAsync(DepartmentId, UserId, artifactId, cancellationToken));

		/// <summary>Re-runs the checks for some or all data sets of a workspace (after the source file changed upstream).</summary>
		[HttpPost("StartAnalysis")]
		public Task<IActionResult> StartAnalysis(string id, [FromBody] List<string> datasetIds, CancellationToken cancellationToken) => Run(async () =>
		{
			var ids = datasetIds?.Count > 0 ? datasetIds : (await _imports.GetDatasetsAsync(DepartmentId, id, cancellationToken)).Select(d => d.Id).ToList();
			var run = await _mapping.RevalidateAsync(DepartmentId, id, ids, cancellationToken);
			return (object)new { RunId = run?.Id };
		});

		private Task<bool> CanManageAsync() => _access.CanManageAsync(DepartmentId, UserId);

		private string Localize(string key)
		{
			var value = _strings[key];
			return value.ResourceNotFound ? key : value.Value;
		}

		private async Task<IActionResult> Run(Func<Task<object>> action)
		{
			if (!await CanManageAsync())
				return Forbid();
			try
			{
				return Ok(await action());
			}
			catch (DepartmentImportException ex)
			{
				return Problem(ex);
			}
			catch (ImportArtifactIntegrityException ex)
			{
				return Problem(new DepartmentImportException(ex.Code, 409));
			}
		}

		private ObjectResult Problem(DepartmentImportException ex)
		{
			var text = _strings["Error." + ex.Code];
			var problem = new ProblemDetails { Status = ex.StatusCode, Type = ex.Code, Title = text.ResourceNotFound ? _strings["Error.Generic"].Value : text.Value };
			return new ObjectResult(problem) { StatusCode = ex.StatusCode };
		}
	}

	public sealed class ImportUploadStartRequest
	{
		public string WorkspaceId { get; set; }
		public string FileName { get; set; }
		public long SizeBytes { get; set; }
		public string Sha256 { get; set; }
		public string ContentType { get; set; }
		public string SourceId { get; set; }
	}
}
