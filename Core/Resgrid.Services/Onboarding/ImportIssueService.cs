using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Issue groups and fixes (wizard plan sections 6.4 and 9). Issues are grouped by problem, not listed by row; fixes are
	/// accept (warnings only), exclude rows, edit a value inline or set a value on every affected row. Each fix re-runs the
	/// ladder for the dataset, so counts update from the same deterministic rules that raised the issue.
	/// </summary>
	public class ImportIssueService : IImportIssueService
	{
		private readonly IImportIssuesRepository _issues;
		private readonly IImportDatasetsRepository _datasets;
		private readonly IImportStagedRowsRepository _rows;
		private readonly IImportArtifactsRepository _artifacts;
		private readonly IImportKeyService _keys;
		private readonly IImportArtifactCipher _cipher;
		private readonly IImportMappingService _mapping;
		private readonly IImportTimelineService _timeline;

		public ImportIssueService(IImportIssuesRepository issues, IImportDatasetsRepository datasets, IImportStagedRowsRepository rows, IImportArtifactsRepository artifacts,
			IImportKeyService keys, IImportArtifactCipher cipher, IImportMappingService mapping, IImportTimelineService timeline)
		{
			_issues = issues;
			_datasets = datasets;
			_rows = rows;
			_artifacts = artifacts;
			_keys = keys;
			_cipher = cipher;
			_mapping = mapping;
			_timeline = timeline;
		}

		public async Task<List<ImportIssueGroup>> GetGroupsAsync(int departmentId, string workspaceId, string datasetId, bool openOnly, CancellationToken cancellationToken = default)
		{
			var issues = await _issues.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var datasets = (await _datasets.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).ToDictionary(d => d.Id);

			return issues
				.Where(i => datasetId == null || i.DatasetId == datasetId)
				.Where(i => !openOnly || i.Status == (int)ImportIssueStatus.Open)
				.GroupBy(i => (i.DatasetId, i.ArtifactId, i.Code, i.FieldKey, i.ColumnName))
				.Select(g => new ImportIssueGroup
				{
					DatasetId = g.Key.DatasetId,
					DatasetName = g.Key.DatasetId != null && datasets.TryGetValue(g.Key.DatasetId, out var d) ? d.Name : null,
					Code = g.Key.Code,
					FieldKey = g.Key.FieldKey,
					ColumnName = g.Key.ColumnName,
					Severity = g.Max(i => i.Severity),
					Level = g.Min(i => i.Level),
					Count = g.Count(),
					OpenCount = g.Count(i => i.Status == (int)ImportIssueStatus.Open),
					SampleRows = g.Where(i => i.RowNumber.HasValue).Select(i => i.RowNumber.Value).OrderBy(r => r).Take(20).ToList(),
					IssueIds = g.Select(i => i.Id).ToList(),
					Parameters = g.Select(i => i.Parameters).FirstOrDefault(p => p != null),
					NeedsDepartment = g.Any(i => i.NeedsDepartment)
				})
				.OrderByDescending(g => g.Severity).ThenBy(g => g.Level).ThenByDescending(g => g.OpenCount)
				.ToList();
		}

		public Task<List<ImportIssue>> GetIssuesAsync(int departmentId, string workspaceId, string datasetId, string code, int? status, int skip, int take,
			CancellationToken cancellationToken = default) =>
			_issues.GetPageAsync(departmentId, workspaceId, datasetId, null, status, code, Math.Max(0, skip), Math.Clamp(take, 1, 200), cancellationToken);

		public async Task<int> AcceptAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, IReadOnlyCollection<string> issueIds,
			CancellationToken cancellationToken = default)
		{
			var issues = await LoadAsync(departmentId, workspaceId, issueIds, cancellationToken);
			// Blocking issues cannot be accepted: they must be fixed or their rows excluded (section 9).
			var accepted = issues.Where(i => i.Status == (int)ImportIssueStatus.Open && i.Severity != (int)ImportIssueSeverity.Blocking).ToList();
			await DecideAsync(accepted, ImportIssueStatus.Accepted, ImportIssueResolutionKind.WarningAccepted, actor, actorKind, cancellationToken);
			await AfterChangeAsync(departmentId, workspaceId, accepted, actor, actorKind, "IssuesAccepted", cancellationToken);
			return accepted.Count;
		}

		public async Task<int> ExcludeRowsAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, IReadOnlyCollection<string> issueIds,
			CancellationToken cancellationToken = default)
		{
			var issues = (await LoadAsync(departmentId, workspaceId, issueIds, cancellationToken)).Where(i => i.StagedRowId != null && i.Status == (int)ImportIssueStatus.Open).ToList();
			var rows = await _rows.GetByIdsAsync(departmentId, issues.Select(i => i.StagedRowId).Distinct().ToList(), cancellationToken);
			foreach (var row in rows.Where(r => !r.Excluded))
			{
				row.Excluded = true;
				row.EditedByStaff = row.EditedByStaff || actorKind == ImportActorKind.ResgridStaff;
				row.UpdatedOn = DateTime.UtcNow;
				await _rows.UpdateStateAsync(row, cancellationToken);
			}

			await DecideAsync(issues, ImportIssueStatus.Excluded, ImportIssueResolutionKind.RowsExcluded, actor, actorKind, cancellationToken);
			await AfterChangeAsync(departmentId, workspaceId, issues, actor, actorKind, "RowsExcluded", cancellationToken);
			return rows.Count;
		}

		public async Task<int> ReopenAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, IReadOnlyCollection<string> issueIds,
			CancellationToken cancellationToken = default)
		{
			var issues = (await LoadAsync(departmentId, workspaceId, issueIds, cancellationToken)).Where(i => i.Status != (int)ImportIssueStatus.Open).ToList();
			foreach (var issue in issues.Where(i => i.Status == (int)ImportIssueStatus.Excluded && i.StagedRowId != null))
			{
				var row = (await _rows.GetByIdsAsync(departmentId, new[] { issue.StagedRowId }, cancellationToken)).FirstOrDefault();
				if (row != null && row.Excluded)
				{
					row.Excluded = false;
					row.UpdatedOn = DateTime.UtcNow;
					await _rows.UpdateStateAsync(row, cancellationToken);
				}
			}

			await DecideAsync(issues, ImportIssueStatus.Open, ImportIssueResolutionKind.None, null, ImportActorKind.Administrator, cancellationToken);
			await AfterChangeAsync(departmentId, workspaceId, issues, actor, actorKind, "IssuesReopened", cancellationToken);
			return issues.Count;
		}

		public async Task EditValueAsync(int departmentId, string actor, ImportActorKind actorKind, string stagedRowId, string column, string value,
			CancellationToken cancellationToken = default)
		{
			var row = (await _rows.GetByIdsAsync(departmentId, new[] { stagedRowId }, cancellationToken)).FirstOrDefault()
				?? throw new DepartmentImportException("import_row_not_found", 404);
			var dataset = await RequireEditableAsync(departmentId, row.DatasetId, cancellationToken);
			await ReplaceCellAsync(dataset, row, column, value, actorKind, cancellationToken);

			await _mapping.RevalidateAsync(departmentId, dataset.WorkspaceId, new[] { dataset.Id }, cancellationToken);
			await _timeline.RecordAsync(departmentId, dataset.WorkspaceId, actorKind, actor, "ValueEdited", new { datasetId = dataset.Id, row = row.RowNumber },
				cancellationToken: cancellationToken);
		}

		public async Task<int> SetValueForIssuesAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, IReadOnlyCollection<string> issueIds,
			string value, CancellationToken cancellationToken = default)
		{
			var issues = (await LoadAsync(departmentId, workspaceId, issueIds, cancellationToken))
				.Where(i => i.StagedRowId != null && !string.IsNullOrWhiteSpace(i.ColumnName) && i.Status == (int)ImportIssueStatus.Open).ToList();
			var touched = new HashSet<string>();

			foreach (var group in issues.GroupBy(i => i.DatasetId))
			{
				var dataset = await RequireEditableAsync(departmentId, group.Key, cancellationToken);
				var rows = (await _rows.GetByIdsAsync(departmentId, group.Select(i => i.StagedRowId).Distinct().ToList(), cancellationToken)).ToDictionary(r => r.Id);
				foreach (var issue in group)
					if (rows.TryGetValue(issue.StagedRowId, out var row))
					{
						await ReplaceCellAsync(dataset, row, issue.ColumnName, value, actorKind, cancellationToken);
						touched.Add(dataset.Id);
					}
			}

			if (touched.Count > 0)
				await _mapping.RevalidateAsync(departmentId, workspaceId, touched.ToList(), cancellationToken);

			await _timeline.RecordAsync(departmentId, workspaceId, actorKind, actor, "ValuesSet", new { count = issues.Count }, cancellationToken: cancellationToken);
			return issues.Count;
		}

		public async Task<ImportRowView> GetRowAsync(int departmentId, string stagedRowId, CancellationToken cancellationToken = default)
		{
			var row = (await _rows.GetByIdsAsync(departmentId, new[] { stagedRowId }, cancellationToken)).FirstOrDefault()
				?? throw new DepartmentImportException("import_row_not_found", 404);
			var dataset = await _datasets.GetByIdAsync(departmentId, row.DatasetId, cancellationToken) ?? throw new DepartmentImportException("import_dataset_not_found", 404);
			var (key, codec) = await CodecAsync(dataset, cancellationToken);

			return new ImportRowView
			{
				StagedRowId = row.Id,
				DatasetId = row.DatasetId,
				RowNumber = row.RowNumber,
				Headers = await HeadersAsync(dataset, key, codec, cancellationToken),
				Cells = (codec.Open<string[]>(key, ImportStagingCodec.Binding(dataset.Id, row.RowNumber, "source"), row.SourcePayload) ?? Array.Empty<string>()).ToList(),
				ValidationState = row.ValidationState,
				Excluded = row.Excluded
			};
		}

		private async Task ReplaceCellAsync(ImportDataset dataset, ImportStagedRow row, string column, string value, ImportActorKind actorKind, CancellationToken cancellationToken)
		{
			var (key, codec) = await CodecAsync(dataset, cancellationToken);
			var headers = await HeadersAsync(dataset, key, codec, cancellationToken);
			var index = headers.FindIndex(h => string.Equals(h, column?.Trim(), StringComparison.Ordinal));
			if (index < 0)
				throw new DepartmentImportException("import_unknown_column");
			if (value != null && value.Length > Config.ImportConfig.MaxFieldLength)
				throw new DepartmentImportException("IMP-FILE-FIELD-TOO-LONG");

			var binding = ImportStagingCodec.Binding(dataset.Id, row.RowNumber, "source");
			var cells = (codec.Open<string[]>(key, binding, row.SourcePayload) ?? Array.Empty<string>()).ToList();
			while (cells.Count <= index)
				cells.Add(null);
			cells[index] = value;

			row.SourcePayload = codec.Seal(key, binding, cells.ToArray());
			row.EditedByStaff = row.EditedByStaff || actorKind == ImportActorKind.ResgridStaff;
			row.UpdatedOn = DateTime.UtcNow;
			await _rows.UpdateStateAsync(row, cancellationToken);
		}

		private async Task<List<string>> HeadersAsync(ImportDataset dataset, string key, ImportStagingCodec codec, CancellationToken cancellationToken)
		{
			var after = 0;
			while (true)
			{
				var page = await _rows.GetPageAsync(dataset.DepartmentId, dataset.Id, after, 50, cancellationToken);
				if (page.Count == 0)
					return new List<string>();
				foreach (var row in page)
				{
					after = row.RowNumber;
					var cells = codec.Open<string[]>(key, ImportStagingCodec.Binding(dataset.Id, row.RowNumber, "source"), row.SourcePayload);
					if (cells != null && cells.Any(c => !string.IsNullOrWhiteSpace(c)))
						return cells.Select(c => c?.Trim() ?? string.Empty).ToList();
				}
			}
		}

		private async Task<(string Key, ImportStagingCodec Codec)> CodecAsync(ImportDataset dataset, CancellationToken cancellationToken)
		{
			var artifact = await _artifacts.GetByIdAsync(dataset.DepartmentId, dataset.ArtifactId, cancellationToken) ?? throw new ImportArtifactIntegrityException("IMP-ARTIFACT-MISSING");
			if (artifact.PurgedOn.HasValue)
				throw new DepartmentImportException("IMP-ARTIFACT-PURGED", 410);
			return (await _keys.UnwrapAsync(dataset.DepartmentId, artifact.WrappedDataKey), new ImportStagingCodec(_cipher));
		}

		private async Task<List<ImportIssue>> LoadAsync(int departmentId, string workspaceId, IReadOnlyCollection<string> issueIds, CancellationToken cancellationToken)
		{
			if (issueIds == null || issueIds.Count == 0)
				return new List<ImportIssue>();
			var wanted = new HashSet<string>(issueIds, StringComparer.OrdinalIgnoreCase);
			// Loaded by workspace AND department: an id from another department never matches (section 14.2).
			return (await _issues.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).Where(i => wanted.Contains(i.Id)).ToList();
		}

		private async Task DecideAsync(IEnumerable<ImportIssue> issues, ImportIssueStatus status, ImportIssueResolutionKind kind, string actor, ImportActorKind actorKind,
			CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			foreach (var issue in issues)
			{
				issue.Status = (int)status;
				issue.ResolutionKind = (int)kind;
				issue.ResolvedBy = status == ImportIssueStatus.Open ? null : actor;
				issue.ResolvedByKind = (int)actorKind;
				issue.ResolvedOn = status == ImportIssueStatus.Open ? (DateTime?)null : now;
				issue.UpdatedOn = now;
				await _issues.UpdateAsync(issue, cancellationToken, true);
			}
		}

		private async Task AfterChangeAsync(int departmentId, string workspaceId, IReadOnlyCollection<ImportIssue> issues, string actor, ImportActorKind actorKind, string action,
			CancellationToken cancellationToken)
		{
			var datasets = issues.Where(i => i.DatasetId != null).Select(i => i.DatasetId).Distinct().ToList();
			if (datasets.Count > 0)
				await _mapping.RevalidateAsync(departmentId, workspaceId, datasets, cancellationToken);
			await _timeline.RecordAsync(departmentId, workspaceId, actorKind, actor, action, new { count = issues.Count }, cancellationToken: cancellationToken);
		}

		private async Task<ImportDataset> RequireEditableAsync(int departmentId, string datasetId, CancellationToken cancellationToken)
		{
			var dataset = await _datasets.GetByIdAsync(departmentId, datasetId, cancellationToken) ?? throw new DepartmentImportException("import_dataset_not_found", 404);
			var status = (ImportDatasetStatus)dataset.Status;
			if (status == ImportDatasetStatus.Importing || status == ImportDatasetStatus.Imported || status == ImportDatasetStatus.Verified)
				throw new DepartmentImportException("import_dataset_locked", 409);
			return dataset;
		}
	}
}
