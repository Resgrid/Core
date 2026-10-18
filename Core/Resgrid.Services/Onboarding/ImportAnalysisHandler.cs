using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Executes Analysis runs (wizard plan sections 9 and 10.1). An artifact run walks four checkpointed phases:
	/// intake (re-verify the whole-file hash while decrypting, L0 signature rules, malware scan), split (one dataset per
	/// sheet), stage (sealed rows, page by page) and validate (detection, column map, L1–L6). A revalidation run carries
	/// dataset ids instead of an artifact and starts at validate. Every phase boundary is saved on the run cursor, so a
	/// killed worker resumes where it stopped.
	/// </summary>
	public class ImportAnalysisHandler : IImportRunHandler, IImportAnalysisService
	{
		private const int PhaseIntake = 0;
		private const int PhaseSplit = 1;
		private const int PhaseStage = 2;
		private const int PhaseValidate = 3;

		private readonly IImportArtifactsRepository _artifacts;
		private readonly IImportDatasetsRepository _datasets;
		private readonly IImportColumnMapsRepository _columnMaps;
		private readonly IImportIssuesRepository _issues;
		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IImportCatalog _catalog;
		private readonly IImportFileReader _reader;
		private readonly IRecordAttachmentScanner _scanner;
		private readonly ImportStagingService _staging;
		private readonly IImportValidationService _validation;
		private readonly IImportTimelineService _timeline;
		private readonly ISubscriptionsService _subscriptions;
		private readonly IImportPlanService _plan;

		public ImportAnalysisHandler(IImportArtifactsRepository artifacts, IImportDatasetsRepository datasets, IImportColumnMapsRepository columnMaps,
			IImportIssuesRepository issues, IImportWorkspacesRepository workspaces, IImportCatalog catalog, IImportFileReader reader,
			IRecordAttachmentScanner scanner, ImportStagingService staging, IImportValidationService validation, IImportTimelineService timeline,
			ISubscriptionsService subscriptions, IImportPlanService plan)
		{
			_artifacts = artifacts;
			_datasets = datasets;
			_columnMaps = columnMaps;
			_issues = issues;
			_workspaces = workspaces;
			_catalog = catalog;
			_reader = reader;
			_scanner = scanner;
			_staging = staging;
			_validation = validation;
			_timeline = timeline;
			_subscriptions = subscriptions;
			_plan = plan;
		}

		public ImportRunKind Kind => ImportRunKind.Analysis;

		public Task<ImportStepResult> AnalyzeArtifactAsync(ImportRun run, CancellationToken cancellationToken = default) =>
			ExecuteAsync(run, new ImportRunLease(run.LeaseOwner, _ => Task.FromResult(true), _ => Task.FromResult(true)), DateTime.UtcNow.AddMinutes(30), cancellationToken);

		public async Task<ImportStepResult> RevalidateDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default)
		{
			await ValidateAndPlanAsync(departmentId, datasetId, null, cancellationToken);
			return ImportStepResult.Done();
		}

		public async Task<ImportStepResult> ExecuteAsync(ImportRun run, ImportRunLease lease, DateTime deadline, CancellationToken cancellationToken)
		{
			var cursor = string.IsNullOrWhiteSpace(run.Cursor) ? new ImportRunCursor() : JsonConvert.DeserializeObject<ImportRunCursor>(run.Cursor);
			var workspace = await _workspaces.GetByIdAsync(run.DepartmentId, run.WorkspaceId, cancellationToken);
			if (workspace == null || DepartmentImportStateMachine.IsTerminal((ImportWorkspaceStatus)workspace.Status))
				return ImportStepResult.Failed("IMP-WORKSPACE-CLOSED");

			async Task<bool> Checkpoint(int phase, string datasetId = null, int lastRow = 0)
			{
				cursor.Phase = phase;
				cursor.DatasetId = datasetId;
				cursor.LastRowNumber = lastRow;
				run.Cursor = JsonConvert.SerializeObject(cursor);
				run.CursorMovedOn = DateTime.UtcNow;
				return await lease.Save(cancellationToken);
			}

			// A revalidation run: datasets only.
			if (string.IsNullOrWhiteSpace(run.ArtifactId))
			{
				var ids = string.IsNullOrWhiteSpace(run.DatasetIds) ? new List<string>() : JsonConvert.DeserializeObject<List<string>>(run.DatasetIds);
				foreach (var id in ids.SkipWhile(id => cursor.CompletedEntities.Contains(id)))
				{
					if (cursor.CompletedEntities.Contains(id))
						continue;
					await ValidateAndPlanAsync(run.DepartmentId, id, lease.Heartbeat, cancellationToken);
					cursor.CompletedEntities.Add(id);
					if (!await Checkpoint(PhaseValidate, id))
						return ImportStepResult.More();
				}

				await RevalidateDependentsAsync(run, ids, lease, cancellationToken);
				return ImportStepResult.Done();
			}

			var artifact = await _artifacts.GetByIdAsync(run.DepartmentId, run.ArtifactId, cancellationToken);
			if (artifact == null || artifact.PurgedOn.HasValue)
				return ImportStepResult.Failed("IMP-ARTIFACT-MISSING");
			if (artifact.UploadState != (int)ImportArtifactUploadState.Completed)
				return ImportStepResult.Failed("IMP-UPLOAD-INCOMPLETE");

			// ---- Phase 0: intake --------------------------------------------------------------------------------
			if (cursor.Phase <= PhaseIntake)
			{
				var intake = await IntakeAsync(artifact, workspace, run, cancellationToken);
				if (intake != null)
					return intake;
				if (!await Checkpoint(PhaseSplit))
					return ImportStepResult.More();
			}

			if (artifact.ScanState != (int)ImportArtifactScanState.Clean && artifact.ScanState != (int)ImportArtifactScanState.NotRequired)
				return ImportStepResult.Failed(artifact.RejectionCode ?? "IMP-FILE-SCAN");

			await using var content = await _staging.OpenAsync(artifact, cancellationToken);

			// ---- Phase 1: split -----------------------------------------------------------------------------------
			var datasets = await _staging.EnsureDatasetsAsync(artifact, content, cancellationToken);
			if (cursor.Phase <= PhaseSplit)
			{
				await _timeline.RecordAsync(run.DepartmentId, run.WorkspaceId, ImportActorKind.System, lease.Owner, "ArtifactSplit",
					new { artifactId = artifact.Id, datasets = datasets.Count }, run.CorrelationId, cancellationToken);
				if (!await Checkpoint(PhaseStage, datasets.FirstOrDefault()?.Id, 0))
					return ImportStepResult.More();
			}

			// ---- Phase 2: stage, one sheet at a time ----------------------------------------------------------
			var maxRows = await MaxRowsAsync(run.DepartmentId);
			if (cursor.Phase <= PhaseStage)
			{
				var startIndex = Math.Max(0, datasets.FindIndex(d => d.Id == cursor.DatasetId));
				for (var i = startIndex; i < datasets.Count; i++)
				{
					var dataset = datasets[i];
					var after = dataset.Id == cursor.DatasetId ? cursor.LastRowNumber : 0;
					var (lastRow, finished, read) = await _staging.StageSheetAsync(artifact, dataset, content, after, maxRows, deadline, lease.Heartbeat, cancellationToken);

					if (!finished && read.ErrorCode == null)
					{
						// Budget spent mid-sheet: checkpoint and let the next claim continue (departments served round-robin).
						return await Checkpoint(PhaseStage, dataset.Id, lastRow) ? ImportStepResult.More() : ImportStepResult.More();
					}

					if (read.ErrorCode != null || read.Truncated)
						await RaiseReadIssueAsync(dataset, read.ErrorCode ?? "IMP-FILE-TOO-MANY-ROWS", read.ErrorRow, maxRows, cancellationToken);

					var next = i + 1 < datasets.Count ? datasets[i + 1].Id : null;
					if (!await Checkpoint(next == null ? PhaseValidate : PhaseStage, next, 0))
						return ImportStepResult.More();
				}
			}

			// ---- Phase 3: detect and validate ---------------------------------------------------------------------
			var catalog = _catalog.GetVersion(workspace.CatalogVersion) ?? _catalog;
			foreach (var dataset in datasets)
			{
				if (cursor.CompletedEntities.Contains(dataset.Id))
					continue;

				var fresh = await _datasets.GetByIdAsync(run.DepartmentId, dataset.Id, cancellationToken) ?? dataset;
				if (fresh.Status != (int)ImportDatasetStatus.Failed)
				{
					await DetectAsync(artifact, fresh, catalog, cancellationToken);
					await ValidateAndPlanAsync(run.DepartmentId, fresh.Id, lease.Heartbeat, cancellationToken);
				}

				cursor.CompletedEntities.Add(dataset.Id);
				if (!await Checkpoint(PhaseValidate, dataset.Id))
					return ImportStepResult.More();
			}

			await RevalidateDependentsAsync(run, datasets.Select(d => d.Id).ToList(), lease, cancellationToken);
			await _timeline.RecordAsync(run.DepartmentId, run.WorkspaceId, ImportActorKind.System, lease.Owner, "ArtifactAnalyzed",
				new { artifactId = artifact.Id, datasets = datasets.Count }, run.CorrelationId, cancellationToken);
			return ImportStepResult.Done();
		}

		/// <summary>Hash re-verification, L0 signature rules and the malware scan; returns a terminal result on refusal.</summary>
		private async Task<ImportStepResult> IntakeAsync(ImportArtifact artifact, ImportWorkspace workspace, ImportRun run, CancellationToken cancellationToken)
		{
			await using var content = await _staging.OpenAsync(artifact, cancellationToken);

			using (var sha = SHA256.Create())
			{
				artifact.Sha256Verified = Convert.ToHexString(await sha.ComputeHashAsync(content, cancellationToken)).ToLowerInvariant();
			}

			if (!string.IsNullOrWhiteSpace(artifact.Sha256Declared) && !string.Equals(artifact.Sha256Declared, artifact.Sha256Verified, StringComparison.OrdinalIgnoreCase))
				return await RejectAsync(artifact, "IMP-FILE-HASH-MISMATCH", cancellationToken);

			content.Position = 0;
			var inspection = _reader.Inspect(content, artifact.OriginalName);
			artifact.DetectedFormat = (int)inspection.Format;
			artifact.DetectedEncoding = inspection.Encoding;
			artifact.DetectedDelimiter = inspection.Delimiter;
			if (inspection.RejectionCode != null && inspection.RejectionCode != "IMP-FILE-ENCODING")
				return await RejectAsync(artifact, inspection.RejectionCode, cancellationToken);

			if (inspection.Format == ImportFileFormat.Zip || inspection.Format == ImportFileFormat.Kml || inspection.Format == ImportFileFormat.Kmz ||
				inspection.Format == ImportFileFormat.Gpx || inspection.Format == ImportFileFormat.Unknown)
				// Packages and geography formats arrive with wave 2B; until then the file is explained, not half-read.
				return await RejectAsync(artifact, "IMP-FILE-UNSUPPORTED", cancellationToken);

			if (inspection.RejectionCode == "IMP-FILE-ENCODING")
				await _issues.InsertManyAsync(new List<ImportIssue> { Warning(ImportIssueFactory.ForArtifact(artifact, "IMP-FILE-ENCODING")) }, cancellationToken);

			// Decision D6: ClamAV is mandatory for workbooks and packages; without scanning only text formats are accepted.
			if (AttachmentScanningConfig.Enabled)
			{
				content.Position = 0;
				using var buffer = new MemoryStream();
				await content.CopyToAsync(buffer, cancellationToken);
				var result = await _scanner.ScanAsync(artifact.Id, "application/octet-stream", buffer.ToArray(), cancellationToken);
				artifact.ScanAttempts++;
				artifact.ScanEngine = "clamav";
				artifact.ScannedOn = DateTime.UtcNow;
				switch (result?.State)
				{
					case RmsAttachmentScanState.Clean:
						artifact.ScanState = (int)ImportArtifactScanState.Clean;
						break;
					case RmsAttachmentScanState.Rejected:
						artifact.ScanState = (int)ImportArtifactScanState.Infected;
						return await RejectAsync(artifact, "IMP-FILE-INFECTED", cancellationToken);
					default:
						// Engine unavailable (fail-closed): wait and retry; worker 48 gives up after ImportConfig.ScanRetryHours.
						artifact.ScanState = (int)ImportArtifactScanState.Pending;
						artifact.UpdatedOn = DateTime.UtcNow;
						await _artifacts.UpdateAsync(artifact, cancellationToken, true);
						return ImportStepResult.Transient("IMP-FILE-SCAN-PENDING");
				}
			}
			else if (inspection.RequiresScanning)
			{
				return await RejectAsync(artifact, "IMP-FILE-SCANNING-REQUIRED", cancellationToken);
			}
			else
			{
				artifact.ScanState = (int)ImportArtifactScanState.NotRequired;
			}

			artifact.UpdatedOn = DateTime.UtcNow;
			await _artifacts.UpdateAsync(artifact, cancellationToken, true);
			await _timeline.RecordAsync(run.DepartmentId, run.WorkspaceId, ImportActorKind.System, "worker47", "ArtifactAccepted",
				new { artifactId = artifact.Id, format = inspection.Format.ToString(), sizeBytes = artifact.SizeBytes }, run.CorrelationId, cancellationToken);
			return null;
		}

		private async Task<ImportStepResult> RejectAsync(ImportArtifact artifact, string code, CancellationToken cancellationToken)
		{
			artifact.RejectionCode = code;
			if (artifact.ScanState == (int)ImportArtifactScanState.Pending)
				artifact.ScanState = (int)ImportArtifactScanState.Rejected;
			artifact.UpdatedOn = DateTime.UtcNow;
			// A refused file is kept only for the failure retention window (section 14.6).
			artifact.RetentionDeadline = DateTime.UtcNow.AddDays(DepartmentImportConfig.DefaultArtifactRetentionDaysAfterFailure);
			await _artifacts.UpdateAsync(artifact, cancellationToken, true);

			var issue = ImportIssueFactory.ForArtifact(artifact, code);
			issue.Parameters = JsonConvert.SerializeObject(new { file = artifact.OriginalName });
			var existing = await _issues.GetAllForWorkspaceAsync(artifact.DepartmentId, artifact.WorkspaceId, cancellationToken);
			if (existing.All(i => i.IdentityKey != issue.IdentityKey))
				await _issues.InsertManyAsync(new List<ImportIssue> { issue }, cancellationToken);

			return ImportStepResult.Failed(code);
		}

		private static ImportIssue Warning(ImportIssue issue)
		{
			issue.Severity = (int)ImportIssueSeverity.Warning;
			return issue;
		}

		/// <summary>L2: proposes the destination and column map, unless the administrator already confirmed one.</summary>
		private async Task DetectAsync(ImportArtifact artifact, ImportDataset dataset, IImportCatalog catalog, CancellationToken cancellationToken)
		{
			if (dataset.DestinationConfirmed && !string.IsNullOrWhiteSpace(dataset.ColumnMapId))
				return;

			var (profile, headers, _) = await _staging.ProfileAsync(artifact, dataset, cancellationToken);
			var detection = !string.IsNullOrWhiteSpace(dataset.EntityKey) && catalog.GetContract(dataset.EntityKey) != null
				? new ImportDetection
				{
					EntityKey = dataset.EntityKey,
					Confidence = 100,
					ProfileKey = "generic",
					Origin = ImportColumnMapOrigin.Administrator,
					Map = ImportColumnMatcher.ProposeMap(headers, catalog.GetContract(dataset.EntityKey))
				}
				: ImportColumnMatcher.Detect(headers, dataset.SheetName, catalog);

			var now = DateTime.UtcNow;
			dataset.Profile = JsonConvert.SerializeObject(profile);
			dataset.ColumnCount = headers.Count;
			dataset.DetectedProfileKey = detection.ProfileKey;
			dataset.DetectionConfidence = detection.Confidence;

			if (detection.EntityKey != null)
			{
				var contract = catalog.GetContract(detection.EntityKey);
				dataset.EntityKey = detection.EntityKey;
				dataset.ContractVersion = contract?.Version;
				dataset.Wave = contract?.Order ?? 0;
				dataset.DestinationConfirmed = dataset.DestinationConfirmed || detection.AutoConfirm ||
					(detection.Confidence >= ImportColumnMatcher.AutoConfirmThreshold && contract != null && contract.Fields.Where(f => f.Required).All(f =>
						detection.Map.Columns.Any(c => c.FieldKey == f.Key)));

				var map = new ImportColumnMap
				{
					DepartmentId = dataset.DepartmentId,
					WorkspaceId = dataset.WorkspaceId,
					Name = dataset.Name,
					EntityKey = detection.EntityKey,
					ContractVersion = contract?.Version,
					Body = JsonConvert.SerializeObject(detection.Map),
					Origin = (int)detection.Origin,
					HeaderSignature = HeaderSignature(headers),
					StaffAuthored = dataset.StaffAuthored,
					CreatedOn = now,
					UpdatedOn = now
				};
				await _columnMaps.InsertAsync(map, cancellationToken, true);
				dataset.ColumnMapId = map.Id;
			}

			dataset.Status = (int)(dataset.DestinationConfirmed ? ImportDatasetStatus.Analyzing : ImportDatasetStatus.NeedsMapping);
			dataset.UpdatedOn = now;
			await _datasets.UpdateWithRevisionAsync(dataset, dataset.Revision, cancellationToken);
		}

		public static string HeaderSignature(IEnumerable<string> headers) =>
			Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("|", headers.Select(ImportFieldConverter.Normalize).OrderBy(h => h, StringComparer.Ordinal)))))
				.ToLowerInvariant();

		private async Task RaiseReadIssueAsync(ImportDataset dataset, string code, int? row, int maxRows, CancellationToken cancellationToken)
		{
			var issue = ImportIssueFactory.Create(dataset, null, null, null, code, ImportLadderLevel.L1Parse, ImportIssueSeverity.Blocking,
				new Dictionary<string, string> { ["row"] = row?.ToString() ?? string.Empty, ["max"] = maxRows.ToString() });
			issue.RowNumber = row;
			var existing = await _issues.GetForDatasetAsync(dataset.DepartmentId, dataset.Id, cancellationToken);
			if (existing.All(i => i.IdentityKey != issue.IdentityKey))
				await _issues.InsertManyAsync(new List<ImportIssue> { issue }, cancellationToken);

			dataset.FailureCode = code;
			if (code != "IMP-FILE-TOO-MANY-ROWS")
				dataset.Status = (int)ImportDatasetStatus.Failed;
			dataset.UpdatedOn = DateTime.UtcNow;
			await _datasets.UpdateWithRevisionAsync(dataset, dataset.Revision, cancellationToken);
		}

		/// <summary>Datasets elsewhere in the workspace that reference the entities just (re)validated get their references re-checked (L6).</summary>
		private async Task RevalidateDependentsAsync(ImportRun run, IReadOnlyCollection<string> changedIds, ImportRunLease lease, CancellationToken cancellationToken)
		{
			var all = await _datasets.GetAllForWorkspaceAsync(run.DepartmentId, run.WorkspaceId, cancellationToken);
			var changedEntities = new HashSet<string>(all.Where(d => changedIds.Contains(d.Id) && d.EntityKey != null).Select(d => d.EntityKey), StringComparer.OrdinalIgnoreCase);
			if (changedEntities.Count == 0)
				return;

			foreach (var dataset in all.Where(d => !changedIds.Contains(d.Id) && DepartmentImportStateMachine.IsLive(d) && d.EntityKey != null && d.DestinationConfirmed &&
				d.Status != (int)ImportDatasetStatus.Importing && d.Status != (int)ImportDatasetStatus.Imported && d.Status != (int)ImportDatasetStatus.Verified))
			{
				var contract = _catalog.GetContract(dataset.EntityKey);
				if (contract == null || !contract.Fields.Any(f => f.References != null && changedEntities.Contains(f.References)))
					continue;

				await ValidateAndPlanAsync(run.DepartmentId, dataset.Id, lease.Heartbeat, cancellationToken);
			}
		}

		/// <summary>L1–L6, then L7–L8 against the department, so the preview never shows a plan older than the staged rows.</summary>
		private async Task ValidateAndPlanAsync(int departmentId, string datasetId, Func<CancellationToken, Task<bool>> heartbeat, CancellationToken cancellationToken)
		{
			await _validation.ValidateDatasetAsync(departmentId, datasetId, heartbeat, cancellationToken);
			await _plan.PlanDatasetAsync(departmentId, datasetId, heartbeat, cancellationToken);
		}

		private async Task<int> MaxRowsAsync(int departmentId)
		{
			try
			{
				var plan = await _subscriptions.GetCurrentPlanForDepartmentAsync(departmentId, false);
				// A null plan (billing unavailable) is treated as the free plan's limit: the safe, smaller one.
				return plan == null || plan.IsFree ? ImportConfig.FreePlanMaxRowsPerDataset : ImportConfig.PaidPlanMaxRowsPerDataset;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return ImportConfig.FreePlanMaxRowsPerDataset;
			}
		}
	}
}
