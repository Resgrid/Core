using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Onboarding.Writers;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// L7 department checks and L8 consequences (wizard plan sections 9, 11.2.3, 12), plus the gates, capacity and
	/// approval digest the approval (L9) and the commit engine share. Planning is pure: it reads the department and
	/// writes only staged-row decisions and issues. It runs after every L1–L6 validation so the preview never shows a
	/// plan older than the staged rows.
	/// </summary>
	public class ImportPlanService : IImportPlanService
	{
		public const string EngineVersion = "import-engine/1";
		private const int PageSize = 1000;

		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IImportDatasetsRepository _datasets;
		private readonly IImportSourcesRepository _sources;
		private readonly IImportStagedRowsRepository _rows;
		private readonly IImportIssuesRepository _issues;
		private readonly IImportExternalIdsRepository _externalIds;
		private readonly IImportColumnMapsRepository _columnMaps;
		private readonly IImportValueMapsRepository _valueMaps;
		private readonly IImportCatalog _catalog;
		private readonly IImportValidationService _validation;
		private readonly IEnumerable<IImportTargetWriter> _writers;
		private readonly IDepartmentsService _departments;
		private readonly ISubscriptionsService _subscriptions;
		private readonly IFeatureToggleService _flags;
		private readonly IDepartmentSettingsService _settings;
		private readonly IUserDefinedFieldsService _customFields;

		public ImportPlanService(IImportWorkspacesRepository workspaces, IImportDatasetsRepository datasets, IImportSourcesRepository sources,
			IImportStagedRowsRepository rows, IImportIssuesRepository issues, IImportExternalIdsRepository externalIds, IImportColumnMapsRepository columnMaps,
			IImportValueMapsRepository valueMaps, IImportCatalog catalog, IImportValidationService validation, IEnumerable<IImportTargetWriter> writers,
			IDepartmentsService departments, ISubscriptionsService subscriptions, IFeatureToggleService flags, IDepartmentSettingsService settings,
			IUserDefinedFieldsService customFields)
		{
			_workspaces = workspaces;
			_datasets = datasets;
			_sources = sources;
			_rows = rows;
			_issues = issues;
			_externalIds = externalIds;
			_columnMaps = columnMaps;
			_valueMaps = valueMaps;
			_catalog = catalog;
			_validation = validation;
			_writers = writers;
			_departments = departments;
			_subscriptions = subscriptions;
			_flags = flags;
			_settings = settings;
			_customFields = customFields;
		}

		public async Task<ImportDatasetPlanSummary> PlanDatasetAsync(int departmentId, string datasetId, Func<CancellationToken, Task<bool>> heartbeat,
			CancellationToken cancellationToken = default)
		{
			var summary = new ImportDatasetPlanSummary { DatasetId = datasetId };
			var dataset = await _datasets.GetByIdAsync(departmentId, datasetId, cancellationToken);
			if (dataset == null || !DepartmentImportStateMachine.IsLive(dataset) || !dataset.DestinationConfirmed || dataset.EntityKey == null)
				return summary;

			var status = (ImportDatasetStatus)dataset.Status;
			if (status == ImportDatasetStatus.NeedsMapping || status == ImportDatasetStatus.Failed || status == ImportDatasetStatus.Importing ||
				status == ImportDatasetStatus.Imported || status == ImportDatasetStatus.Verified)
				return summary;

			var workspace = await _workspaces.GetByIdAsync(departmentId, dataset.WorkspaceId, cancellationToken);
			var writer = _writers.FirstOrDefault(w => string.Equals(w.EntityKey, dataset.EntityKey, StringComparison.OrdinalIgnoreCase));
			var catalog = _catalog.GetVersion(workspace?.CatalogVersion) ?? _catalog;
			var contract = catalog.GetContract(dataset.EntityKey);
			if (workspace == null || writer == null || contract == null)
				return summary;

			var context = new ImportWriteContext
			{
				DepartmentId = departmentId,
				Department = await _departments.GetDepartmentByIdAsync(departmentId, false),
				Workspace = workspace,
				Dataset = dataset,
				// Replace plans matched rows as a reviewed update; what the file lacks is retired below.
				WriteMode = DepartmentImportService.EffectiveWriteMode(workspace) == ImportWriteMode.Replace
					? ImportWriteMode.ReviewedUpdate
					: DepartmentImportService.EffectiveWriteMode(workspace),
				Now = DateTime.UtcNow,
				Authority = ImportAuthority.ForAdministrator(departmentId, workspace.OwnerUserId)
			};

			var datasetIssues = new List<ImportIssue>();
			var rowIssues = new List<ImportIssue>();

			// ---- Gate: entitlement (flags, module, add-on) and the writer's own check ----------------------------------
			summary.GateCode = await CheckGateAsync(departmentId, dataset.EntityKey, workspace.CatalogVersion);
			if (summary.GateCode == null)
			{
				var gate = await writer.CheckGateAsync(context, cancellationToken);
				if (!gate.Allowed)
					summary.GateCode = gate.Code ?? "IMP-ENTITLEMENT-MISSING";
			}
			if (summary.GateCode != null)
				datasetIssues.Add(ImportIssueFactory.Create(dataset, null, null, null, summary.GateCode, ImportLadderLevel.L7Department, ImportIssueSeverity.Blocking));

			// ---- Kept custom fields need a definition the import may publish (ImportCustomFields) ------------------------
			if (contract.SupportsCustomFields)
			{
				var labels = await KeptCustomFieldLabelsAsync(dataset, cancellationToken);
				if (labels.Count > 0)
				{
					var (missing, canPublish) = await ImportCustomFields.CheckAsync(_customFields, departmentId, CustomFieldEntity(dataset.EntityKey), labels);
					if (missing.Count > 0 && !canPublish)
						datasetIssues.Add(ImportIssueFactory.Create(dataset, null, null, null, "IMP-CUSTOM-FIELDS-MISSING", ImportLadderLevel.L7Department,
							ImportIssueSeverity.Blocking, new Dictionary<string, string> { ["fields"] = string.Join(", ", missing.Take(20)) }));
				}
			}

			// ---- Rows: decisions against the department and the external-id map ----------------------------------------
			var sourceSystemKey = await SourceSystemKeyAsync(dataset, cancellationToken);
			var after = 0;
			var planned = new List<(ImportCanonicalRow Row, ImportPlannedRow Plan)>();
			var previous = new Dictionary<string, (ImportRowDecision, string)>(StringComparer.Ordinal);
			var replace = DepartmentImportService.EffectiveWriteMode(workspace) == ImportWriteMode.Replace;
			var namesInFile = new HashSet<string>(StringComparer.Ordinal);
			while (true)
			{
				var page = await _validation.ReadCanonicalRowsAsync(dataset, after, PageSize, cancellationToken);
				if (page.Count == 0)
					break;
				after = page[page.Count - 1].RowNumber;
				if (replace)
					foreach (var name in page.Select(r => ImportFieldConverter.Normalize(r.Text("name"))).Where(n => n.Length > 0))
						namesInFile.Add(name);

				var candidates = page.Where(r => r.ValidationState == ImportRowValidationState.Valid || r.ValidationState == ImportRowValidationState.Warning ||
					r.ValidationState == ImportRowValidationState.Blocked && r.Decision != ImportRowDecision.Undecided).ToList();
				// A row blocked only by an earlier plan (L7) is planned again; one blocked by L1–L6 is not.
				var ladderBlocked = await LadderBlockedRowIdsAsync(dataset, candidates, cancellationToken);
				candidates = candidates.Where(r => !ladderBlocked.Contains(r.StagedRowId)).ToList();

				var known = (await _externalIds.GetForSourceIdsAsync(departmentId, sourceSystemKey, dataset.EntityKey,
						candidates.Where(r => r.SourceKey != null).Select(r => r.SourceKey).Distinct().ToList(), cancellationToken))
					.Where(e => e.State == (int)ImportExternalIdState.Written || e.State == (int)ImportExternalIdState.Linked)
					.GroupBy(e => e.SourceId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

				foreach (var row in candidates)
				{
					previous[row.StagedRowId] = (row.Decision, row.TargetKey);
					row.Decision = ImportRowDecision.Undecided;
					row.TargetKey = null;
					row.TargetVersion = null;
					if (row.SourceKey != null && known.TryGetValue(row.SourceKey, out var external))
					{
						// Already written by an earlier run of this source: unchanged, or reviewed for updates.
						row.TargetKey = external.TargetKey;
						row.Decision = context.WriteMode == ImportWriteMode.ReviewedUpdate ? ImportRowDecision.Undecided : ImportRowDecision.Unchanged;
					}
				}

				var plan = await writer.PlanAsync(context, candidates, cancellationToken);
				foreach (var (key, count) in plan.Consequences)
					summary.Consequences[key] = (summary.Consequences.TryGetValue(key, out var c) ? c : 0) + count;
				summary.PersonnelSeats += plan.PersonnelSeats;
				summary.UnitSeats += plan.UnitSeats;

				var byId = candidates.ToDictionary(r => r.StagedRowId, StringComparer.Ordinal);
				foreach (var row in plan.Rows)
					if (byId.TryGetValue(row.StagedRowId, out var canonical))
					{
						planned.Add((canonical, row));
						rowIssues.AddRange(row.Issues.Select(i => ImportIssueFactory.ForPlan(dataset, i)));
					}

				if (heartbeat != null && !await heartbeat(cancellationToken))
					throw new OperationCanceledException("Import run lease lost.");
			}

			// ---- Replace (sections 5.2, 12.4): the records of this kind the file lacks are retired, each restorably ----------
			string retireKeys = null;
			if (replace)
				retireKeys = await PlanReplaceAsync(context, writer, dataset, planned.Select(p => p.Plan.TargetKey), namesInFile, summary, datasetIssues, cancellationToken);

			// ---- Consequences (L8): each needs an acknowledged choice before approval ---------------------------------
			foreach (var (key, count) in summary.Consequences)
			{
				var parameters = new Dictionary<string, string> { ["consequence"] = key, ["count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture) };
				if (key == ReplaceRetiresConsequence)
					parameters["keys"] = retireKeys;
				datasetIssues.Add(ImportIssueFactory.Create(dataset, null, null, key, "IMP-CONSEQUENCE-UNDECIDED", ImportLadderLevel.L8Consequences, ImportIssueSeverity.Blocking,
					parameters));
			}

			// ---- Persist: replace open L7/L8 issues (decisions survive by identity), then row plans --------------------
			await _issues.DeleteOpenSystemIssuesAsync(departmentId, dataset.Id, (int)ImportLadderLevel.L7Department, cancellationToken);
			var earlier = await _issues.GetForDatasetAsync(departmentId, dataset.Id, cancellationToken);
			// An acknowledged retire list that no longer matches is withdrawn: the new list is acknowledged again.
			foreach (var stale in earlier.Where(i => i.Code == "IMP-CONSEQUENCE-UNDECIDED" && i.FieldKey == ReplaceRetiresConsequence &&
				i.Status != (int)ImportIssueStatus.Open && RetireKeysOf(i) != retireKeys).ToList())
			{
				await _issues.DeleteAsync(stale, cancellationToken);
				earlier.Remove(stale);
			}
			var decided = earlier.Where(i => i.Status != (int)ImportIssueStatus.Open).Select(i => i.IdentityKey).ToHashSet(StringComparer.Ordinal);
			var fresh = datasetIssues.Concat(rowIssues).Where(i => !decided.Contains(i.IdentityKey)).GroupBy(i => i.IdentityKey).Select(g => g.First()).ToList();
			await _issues.InsertManyAsync(fresh, cancellationToken);

			var blockingByRow = fresh.Where(i => i.StagedRowId != null && i.Severity == (int)ImportIssueSeverity.Blocking)
				.Select(i => i.StagedRowId).ToHashSet(StringComparer.Ordinal);
			var decisionsChanged = false;
			var now = DateTime.UtcNow;
			foreach (var (row, plan) in planned)
			{
				var state = blockingByRow.Contains(row.StagedRowId)
					? ImportRowValidationState.Blocked
					: row.ValidationState == ImportRowValidationState.Blocked ? ImportRowValidationState.Valid : row.ValidationState;
				if (!previous.TryGetValue(row.StagedRowId, out var before) || before != (plan.Decision, plan.TargetKey))
					decisionsChanged = true;

				await _rows.SetPlanAsync(departmentId, row.StagedRowId, (int)plan.Decision, plan.TargetKey, plan.TargetVersion, (int)state, now, cancellationToken);
				summary.Decisions[plan.Decision.ToString()] = (summary.Decisions.TryGetValue(plan.Decision.ToString(), out var n) ? n : 0) + 1;
				if (state == ImportRowValidationState.Blocked)
					summary.Blocked++;
			}

			await RefreshDatasetAsync(dataset, decisionsChanged, fresh.Any(i => i.StagedRowId == null && i.Severity == (int)ImportIssueSeverity.Blocking), cancellationToken);
			summary.Planned = true;
			return summary;
		}

		/// <summary>Rows with an open blocking issue below L7 (parse, type, rules, uniqueness, references).</summary>
		private async Task<HashSet<string>> LadderBlockedRowIdsAsync(ImportDataset dataset, IReadOnlyCollection<ImportCanonicalRow> rows, CancellationToken cancellationToken)
		{
			if (!rows.Any(r => r.ValidationState == ImportRowValidationState.Blocked))
				return new HashSet<string>(StringComparer.Ordinal);

			var ids = rows.Where(r => r.ValidationState == ImportRowValidationState.Blocked).Select(r => r.StagedRowId).ToHashSet(StringComparer.Ordinal);
			return (await _issues.GetForDatasetAsync(dataset.DepartmentId, dataset.Id, cancellationToken))
				.Where(i => i.StagedRowId != null && ids.Contains(i.StagedRowId) && i.Status == (int)ImportIssueStatus.Open &&
					i.Severity == (int)ImportIssueSeverity.Blocking && i.Level < (int)ImportLadderLevel.L7Department)
				.Select(i => i.StagedRowId).ToHashSet(StringComparer.Ordinal);
		}

		private async Task RefreshDatasetAsync(ImportDataset dataset, bool decisionsChanged, bool datasetBlocked, CancellationToken cancellationToken)
		{
			var fresh = await _datasets.GetByIdAsync(dataset.DepartmentId, dataset.Id, cancellationToken) ?? dataset;
			var inputs = await _rows.GetDigestInputsAsync(fresh.DepartmentId, fresh.Id, cancellationToken);
			var counted = inputs.Where(r => !r.IsExample && r.ContentHash != null).ToList();
			fresh.BlockedCount = counted.Count(r => !r.Excluded && r.ValidationState == (int)ImportRowValidationState.Blocked);
			fresh.ReadyCount = counted.Count(r => !r.Excluded && (r.ValidationState == (int)ImportRowValidationState.Valid || r.ValidationState == (int)ImportRowValidationState.Warning));
			fresh.UpdatedOn = DateTime.UtcNow;

			var current = (ImportDatasetStatus)fresh.Status;
			if (current == ImportDatasetStatus.Ready || current == ImportDatasetStatus.NeedsAttention || current == ImportDatasetStatus.Approved ||
				current == ImportDatasetStatus.Analyzing)
			{
				var openBlocking = await _issues.CountOpenBlockingAsync(fresh.DepartmentId, fresh.WorkspaceId, fresh.Id, cancellationToken);
				var next = fresh.BlockedCount > 0 || datasetBlocked || openBlocking > 0 ? ImportDatasetStatus.NeedsAttention : ImportDatasetStatus.Ready;
				if (current == ImportDatasetStatus.Approved && next == ImportDatasetStatus.Ready && !decisionsChanged)
					next = ImportDatasetStatus.Approved;
				else if (current == ImportDatasetStatus.Approved)
					DepartmentImportStateMachine.InvalidateApproval(fresh);
				fresh.Status = (int)next;
			}

			if (!await _datasets.UpdateWithRevisionAsync(fresh, fresh.Revision, cancellationToken))
				Logging.LogInfo($"Import dataset {fresh.Id} changed during planning; counts left to the next run.");
		}

		private async Task<List<string>> KeptCustomFieldLabelsAsync(ImportDataset dataset, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(dataset.ColumnMapId))
				return new List<string>();
			var map = await _columnMaps.GetByIdAsync(dataset.DepartmentId, dataset.ColumnMapId, cancellationToken);
			var body = map == null ? null : JsonConvert.DeserializeObject<ImportColumnMapBody>(map.Body);
			return body?.Columns.Where(c => c.Kind == (int)ImportColumnBindingKind.KeepAsCustomField)
				.Select(c => string.IsNullOrWhiteSpace(c.CustomFieldLabel) ? c.SourceColumn : c.CustomFieldLabel)
				.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
		}

		public static UdfEntityType CustomFieldEntity(string entityKey) =>
			string.Equals(entityKey, "units", StringComparison.OrdinalIgnoreCase) ? UdfEntityType.Unit : UdfEntityType.Personnel;

		/// <summary>Kept-column labels per dataset, for the commit's custom-field definition step.</summary>
		public Task<List<string>> GetKeptCustomFieldLabelsAsync(ImportDataset dataset, CancellationToken cancellationToken = default) =>
			KeptCustomFieldLabelsAsync(dataset, cancellationToken);

		/// <summary>
		/// A file with no declared source belongs to the earlier import its workspace continues, else to this workspace's own
		/// instance (<see cref="ImportSourceKeys"/>).
		/// </summary>
		public Task<string> SourceSystemKeyAsync(ImportDataset dataset, CancellationToken cancellationToken = default) =>
			ImportSourceKeys.ForDatasetAsync(_sources, dataset, cancellationToken);

		/// <summary>The consequence a Replace's retire list is acknowledged under; its "keys" parameter freezes the list.</summary>
		public const string ReplaceRetiresConsequence = "replace.retires";

		/// <summary>The frozen retire list (record keys, comma-separated) of an acknowledged Replace consequence, or null.</summary>
		public static string RetireKeysOf(ImportIssue issue)
		{
			if (string.IsNullOrWhiteSpace(issue?.Parameters))
				return null;
			var parameters = JsonConvert.DeserializeObject<Dictionary<string, string>>(issue.Parameters);
			return parameters != null && parameters.TryGetValue("keys", out var keys) ? keys : null;
		}

		/// <summary>
		/// The records a Replace would retire: the department's live records of this kind that no row of the file names or
		/// targets (an excluded row still keeps its record). Blocks when the kind cannot be replaced, when another live data set
		/// has the same destination (one data set per Replace), or when a record cannot be retired (the rollback proof).
		/// </summary>
		private async Task<string> PlanReplaceAsync(ImportWriteContext context, IImportTargetWriter writer, ImportDataset dataset, IEnumerable<string> plannedTargets,
			IReadOnlyCollection<string> namesInFile, ImportDatasetPlanSummary summary, List<ImportIssue> datasetIssues, CancellationToken cancellationToken)
		{
			if (!(writer is IImportReplaceableWriter replaceable) || !replaceable.SupportsReplace)
			{
				datasetIssues.Add(ImportIssueFactory.Create(dataset, null, null, null, "IMP-REPLACE-UNSUPPORTED", ImportLadderLevel.L8Consequences, ImportIssueSeverity.Blocking));
				return null;
			}
			if ((await _datasets.GetAllForWorkspaceAsync(context.DepartmentId, dataset.WorkspaceId, cancellationToken))
				.Count(d => DepartmentImportStateMachine.IsLive(d) && string.Equals(d.EntityKey, dataset.EntityKey, StringComparison.OrdinalIgnoreCase)) > 1)
			{
				datasetIssues.Add(ImportIssueFactory.Create(dataset, null, null, null, "IMP-REPLACE-ONE-DATASET", ImportLadderLevel.L8Consequences, ImportIssueSeverity.Blocking));
				return null;
			}

			var existing = await writer.FindExistingAsync(context.DepartmentId, cancellationToken);
			var kept = plannedTargets.Where(k => k != null).Concat(namesInFile.Where(existing.ContainsKey).Select(n => existing[n])).ToList();
			var candidates = await replaceable.GetRetireCandidatesAsync(context, kept, cancellationToken);

			var blocked = candidates.Count(c => !c.CanRetire);
			if (blocked > 0)
				datasetIssues.Add(ImportIssueFactory.Create(dataset, null, null, null, "IMP-REPLACE-CANNOT-RETIRE", ImportLadderLevel.L8Consequences, ImportIssueSeverity.Blocking,
					new Dictionary<string, string> { ["count"] = blocked.ToString(System.Globalization.CultureInfo.InvariantCulture) }));

			var retire = candidates.Where(c => c.CanRetire).Select(c => c.TargetKey).OrderBy(k => k, StringComparer.Ordinal).ToList();
			if (retire.Count == 0)
				return null;
			summary.Consequences[ReplaceRetiresConsequence] = retire.Count;
			return string.Join(",", retire);
		}

		public async Task<string> CheckGateAsync(int departmentId, string entityKey, string catalogVersion)
		{
			var contract = (_catalog.GetVersion(catalogVersion) ?? _catalog).GetContract(entityKey);
			if (contract == null)
				return "IMP-ENTITLEMENT-MISSING";

			try
			{
				foreach (var flag in contract.Gate?.Flags ?? new List<string>())
					if ((await _flags.EvaluateFreshAsync(flag, departmentId))?.IsEnabled != true)
						return "IMP-ENTITLEMENT-MISSING";

				if (!string.IsNullOrWhiteSpace(contract.Gate?.Module))
				{
					var modules = await _settings.GetDepartmentModuleSettingsAsync(departmentId, true);
					var disabled = typeof(DepartmentModuleSettings).GetProperty(contract.Gate.Module + "Disabled");
					if (disabled == null || modules != null && (bool)disabled.GetValue(modules))
						return "IMP-ENTITLEMENT-MISSING";
				}

				// Add-on entitlements arrive with the waves that need them; until then a gated contract is refused (fail closed).
				if (!string.IsNullOrWhiteSpace(contract.Gate?.Addon))
					return "IMP-ENTITLEMENT-MISSING";
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return "IMP-ENTITLEMENT-MISSING";
			}

			return null;
		}

		public async Task<ImportCapacity> ReadCapacityAsync(int departmentId)
		{
			try
			{
				// Fresh: two Billing API calls, once at preview and once at commit start (section 11.6).
				var plan = await _subscriptions.GetCurrentPlanForDepartmentAsync(departmentId, true);
				var counts = await _subscriptions.GetPlanCountsForDepartmentAsync(departmentId);
				if (plan == null || counts == null)
					return new ImportCapacity();

				if (plan.PlanId >= 36)
				{
					var remaining = Math.Max(0, plan.GetLimitForTypeAsInt(PlanLimitTypes.Entities) - counts.UsersCount - counts.UnitsCount);
					return new ImportCapacity { Personnel = remaining, Units = remaining, Shared = true };
				}

				return new ImportCapacity
				{
					Personnel = Math.Max(0, plan.GetLimitForTypeAsInt(PlanLimitTypes.Personnel) - counts.UsersCount),
					Units = Math.Max(0, plan.GetLimitForTypeAsInt(PlanLimitTypes.Units) - counts.UnitsCount)
				};
			}
			catch (Exception ex)
			{
				// Unknown capacity writes no new personnel or units (fail closed).
				Logging.LogException(ex);
				return new ImportCapacity();
			}
		}

		public async Task<string> ComputeApprovalDigestAsync(int departmentId, IReadOnlyList<string> datasetIds, ImportWriteMode writeMode, CancellationToken cancellationToken = default)
		{
			var builder = new StringBuilder();
			builder.Append(EngineVersion).Append('|').Append((int)writeMode).Append('\n');

			foreach (var datasetId in datasetIds.OrderBy(i => i, StringComparer.Ordinal))
			{
				var dataset = await _datasets.GetByIdAsync(departmentId, datasetId, cancellationToken);
				if (dataset == null)
				{
					builder.Append("missing:").Append(datasetId).Append('\n');
					continue;
				}

				var workspace = await _workspaces.GetByIdAsync(departmentId, dataset.WorkspaceId, cancellationToken);
				var contract = (_catalog.GetVersion(workspace?.CatalogVersion) ?? _catalog).GetContract(dataset.EntityKey);
				builder.Append(dataset.Id).Append('|').Append(dataset.EntityKey).Append('|').Append(contract?.Version).Append('|')
					.Append(workspace?.CatalogVersion).Append('|').Append(dataset.ColumnMapId).Append('\n');

				var map = string.IsNullOrWhiteSpace(dataset.ColumnMapId) ? null : await _columnMaps.GetByIdAsync(departmentId, dataset.ColumnMapId, cancellationToken);
				builder.Append(Hash(map?.Body ?? string.Empty)).Append('\n');

				foreach (var value in (await _valueMaps.GetForDatasetAsync(departmentId, dataset.Id, cancellationToken))
						 .OrderBy(v => v.FieldKey, StringComparer.Ordinal).ThenBy(v => v.SourceValueHash, StringComparer.Ordinal))
					builder.Append(value.FieldKey).Append('|').Append(value.SourceValueHash).Append('|').Append(value.Action).Append('|').Append(value.TargetCode).Append('\n');

				foreach (var row in await _rows.GetDigestInputsAsync(departmentId, dataset.Id, cancellationToken))
					builder.Append(row.RowNumber).Append('|').Append(row.ContentHash).Append('|').Append(row.ValidationState).Append('|')
						.Append(row.Excluded ? 1 : 0).Append('|').Append(row.Decision).Append('|').Append(row.TargetKey).Append('\n');
			}

			return Hash(builder.ToString());
		}

		private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
	}
}
