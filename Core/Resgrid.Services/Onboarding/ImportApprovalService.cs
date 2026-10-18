using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Preview and approval (plan MW-08, sections 10.2, 11.6, 11.8): the wave preview with its consequences and seats,
	/// approval with an acknowledged consequence set, one fresh capacity read and the approval digest, the second
	/// approver, and rollback requests inside the rollback window (decision D7). Step-up is enforced by the controller.
	/// </summary>
	public class ImportApprovalService : IImportApprovalService
	{
		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IImportDatasetsRepository _datasets;
		private readonly IImportStagedRowsRepository _rows;
		private readonly IImportIssuesRepository _issues;
		private readonly IImportRunsRepository _runs;
		private readonly IImportChangeJournalRepository _journal;
		private readonly IImportCatalog _catalog;
		private readonly IImportPlanService _plan;
		private readonly IImportRunQueue _queue;
		private readonly IImportTimelineService _timeline;
		private readonly IDepartmentImportAccessService _access;
		private readonly IDepartmentSettingsService _settings;
		private readonly ImportEffectsRunner _effects;
		private readonly IImportSupportGrantsRepository _grants;
		private readonly IImportConfigurationService _configuration;
		private readonly IImportConfigurationChangesRepository _configurationChanges;

		public ImportApprovalService(IImportWorkspacesRepository workspaces, IImportDatasetsRepository datasets, IImportStagedRowsRepository rows,
			IImportIssuesRepository issues, IImportRunsRepository runs, IImportChangeJournalRepository journal, IImportCatalog catalog, IImportPlanService plan,
			IImportRunQueue queue, IImportTimelineService timeline, IDepartmentImportAccessService access, IDepartmentSettingsService settings,
			ImportEffectsRunner effects, IImportSupportGrantsRepository grants, IImportConfigurationService configuration,
			IImportConfigurationChangesRepository configurationChanges)
		{
			_configuration = configuration;
			_configurationChanges = configurationChanges;
			_grants = grants;
			_workspaces = workspaces;
			_datasets = datasets;
			_rows = rows;
			_issues = issues;
			_runs = runs;
			_journal = journal;
			_catalog = catalog;
			_plan = plan;
			_queue = queue;
			_timeline = timeline;
			_access = access;
			_settings = settings;
			_effects = effects;
		}

		public async Task<ImportPreview> GetPreviewAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			var workspace = await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			var preview = new ImportPreview { Workspace = workspace };
			var runs = await _runs.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			// A staff-run rollback waiting for signatures is pending too: under department sign-off it is approved from here.
			preview.PendingRun = runs.Where(r => r.Kind == (int)ImportRunKind.Commit && DepartmentImportStateMachine.OpenRunStatuses.Contains((ImportRunStatus)r.Status) ||
					r.Kind == (int)ImportRunKind.Rollback && r.Status == (int)ImportRunStatus.AwaitingChecker && Authority(r)?.IsStaff == true)
				.OrderByDescending(r => r.RequestedOn).FirstOrDefault();
			var pendingAuthority = Authority(preview.PendingRun);
			preview.PendingRunIsStaff = pendingAuthority?.IsStaff == true;
			preview.PendingRunWaitsForStaff = preview.PendingRunIsStaff && preview.PendingRun.Status == (int)ImportRunStatus.AwaitingChecker &&
				string.IsNullOrWhiteSpace(pendingAuthority.StaffChecker);
			preview.RequiresSecondApprover = await RequiresSecondApproverAsync(departmentId, workspace, ImportWriteMode.CreateOnly);

			var catalog = _catalog.GetVersion(workspace.CatalogVersion) ?? _catalog;
			var order = catalog.InDependencyOrder().Select((c, i) => (c.Key, i)).ToDictionary(x => x.Key, x => x.i, StringComparer.OrdinalIgnoreCase);
			var datasets = (await _datasets.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
				.Where(d => DepartmentImportStateMachine.IsLive(d) && d.EntityKey != null)
				.OrderBy(d => order.TryGetValue(d.EntityKey, out var i) ? i : int.MaxValue).ThenBy(d => d.CreatedOn).ToList();

			foreach (var dataset in datasets)
			{
				var inputs = (await _rows.GetDigestInputsAsync(departmentId, dataset.Id, cancellationToken)).Where(r => !r.IsExample && r.ContentHash != null).ToList();
				int Count(ImportRowDecision decision) => inputs.Count(r => !r.Excluded && r.ValidationState != (int)ImportRowValidationState.Blocked && r.Decision == (int)decision);
				var item = new ImportPreviewDataset
				{
					DatasetId = dataset.Id,
					Name = dataset.Name,
					EntityKey = dataset.EntityKey,
					Status = dataset.Status,
					Wave = dataset.Wave,
					Create = Count(ImportRowDecision.Create),
					Update = Count(ImportRowDecision.Update),
					Link = Count(ImportRowDecision.Link),
					Unchanged = Count(ImportRowDecision.Unchanged),
					Skip = Count(ImportRowDecision.Skip),
					InviteToJoin = Count(ImportRowDecision.InviteToJoin),
					Reactivate = Count(ImportRowDecision.Reactivate),
					Blocked = inputs.Count(r => !r.Excluded && r.ValidationState == (int)ImportRowValidationState.Blocked),
					Excluded = inputs.Count(r => r.Excluded || r.ValidationState == (int)ImportRowValidationState.Excluded),
					OpenBlockingIssues = await _issues.CountOpenBlockingAsync(departmentId, workspaceId, dataset.Id, cancellationToken)
				};
				preview.Datasets.Add(item);

				if (string.Equals(dataset.EntityKey, "personnel", StringComparison.OrdinalIgnoreCase))
					preview.PersonnelSeatsNeeded += item.Create + item.Reactivate;
				if (string.Equals(dataset.EntityKey, "units", StringComparison.OrdinalIgnoreCase))
					preview.UnitSeatsNeeded += item.Create;

				foreach (var issue in (await _issues.GetForDatasetAsync(departmentId, dataset.Id, cancellationToken)).Where(i => i.Code == "IMP-CONSEQUENCE-UNDECIDED"))
				{
					var parameters = string.IsNullOrWhiteSpace(issue.Parameters) ? new Dictionary<string, string>() : JsonConvert.DeserializeObject<Dictionary<string, string>>(issue.Parameters);
					preview.Consequences.Add(new ImportPreviewConsequence
					{
						Key = issue.FieldKey,
						DatasetId = dataset.Id,
						Count = parameters.TryGetValue("count", out var count) && int.TryParse(count, out var n) ? n : 0,
						Acknowledged = issue.Status != (int)ImportIssueStatus.Open,
						IssueId = issue.Id
					});
				}
			}

			var configuration = await _configuration.GetPendingAsync(departmentId, workspaceId, cancellationToken);
			preview.Configuration = configuration.Changes;
			preview.ConfigurationIssues = configuration.Issues;

			preview.Capacity = await _plan.ReadCapacityAsync(departmentId);
			var ready = preview.Datasets.Where(d => d.Status == (int)ImportDatasetStatus.Ready || d.Status == (int)ImportDatasetStatus.Approved).ToList();
			if (preview.PendingRun != null)
				preview.Blockers.Add("PreviewBlockerRunOpen");
			if (preview.Datasets.Any(d => d.Status == (int)ImportDatasetStatus.Analyzing))
				preview.Blockers.Add("PreviewBlockerRecalculating");
			if (ready.Count == 0 && preview.Configuration.Count == 0)
				preview.Blockers.Add("PreviewBlockerNothingReady");
			if (preview.ConfigurationIssues.Count > 0)
				preview.Blockers.Add("PreviewBlockerConfigurationIssues");
			if (!preview.Capacity.Known && (preview.PersonnelSeatsNeeded > 0 || preview.UnitSeatsNeeded > 0))
				preview.Blockers.Add("PreviewBlockerCapacityUnknown");
			else if (!preview.Capacity.Covers(preview.PersonnelSeatsNeeded, preview.UnitSeatsNeeded) && (preview.PersonnelSeatsNeeded > 0 || preview.UnitSeatsNeeded > 0))
				preview.Blockers.Add("PreviewBlockerCapacity");
			preview.CanApprove = preview.Blockers.Count == 0;
			return preview;
		}

		public async Task<ImportRun> ApproveAsync(int departmentId, string userId, string workspaceId, ImportApprovalRequest request, CancellationToken cancellationToken = default)
		{
			if (!await _access.CanManageAsync(departmentId, userId))
				throw new DepartmentImportException("import_not_allowed", 403);
			return await CreateCommitRunAsync(departmentId, userId, ImportAuthority.ForAdministrator(departmentId, userId), workspaceId, request, false, cancellationToken);
		}

		public async Task<ImportRun> RequestStaffCommitAsync(int departmentId, ImportAuthority authority, string workspaceId, ImportApprovalRequest request,
			CancellationToken cancellationToken = default)
		{
			if (authority == null || !authority.IsStaff || string.IsNullOrWhiteSpace(authority.StaffOperator))
				throw new DepartmentImportException("import_not_allowed", 403);
			// The authorizing administrator must still be one (L7): staff never act without a department authority behind them.
			if (!await _access.CanManageAsync(departmentId, authority.OnBehalfOfUserId))
				throw new DepartmentImportException("IMP-APPROVER-NOT-AUTHORIZED", 409);
			// Two-person control: a staff-requested import always waits for a different staff checker (section 17.8).
			return await CreateCommitRunAsync(departmentId, "staff:" + authority.StaffOperator, authority, workspaceId, request, true, cancellationToken);
		}

		private async Task<ImportRun> CreateCommitRunAsync(int departmentId, string userId, ImportAuthority authority, string workspaceId, ImportApprovalRequest request,
			bool forceChecker, CancellationToken cancellationToken)
		{
			request ??= new ImportApprovalRequest();

			var workspace = await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			if (DepartmentImportStateMachine.IsTerminal((ImportWorkspaceStatus)workspace.Status))
				throw new DepartmentImportException("import_workspace_closed", 409);
			if ((await _runs.GetOpenForDepartmentAsync(departmentId, cancellationToken)).Any(r => r.Kind == (int)ImportRunKind.Commit || r.Kind == (int)ImportRunKind.Rollback))
				throw new DepartmentImportException("import_run_open", 409);
			// The rows were planned for the workspace's write mode: approving another would commit decisions nobody previewed.
			if (request.WriteMode != DepartmentImportService.EffectiveWriteMode(workspace))
				throw new DepartmentImportException("import_write_mode_stale", 409);
			if (request.WriteMode == ImportWriteMode.Replace)
				await RequireReplaceAuthorityAsync(departmentId, workspaceId, authority, cancellationToken);

			var live = (await _datasets.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).Where(DepartmentImportStateMachine.IsLive).ToList();
			var chosen = request.DatasetIds == null || request.DatasetIds.Count == 0
				? live.Where(d => d.Status == (int)ImportDatasetStatus.Ready || d.Status == (int)ImportDatasetStatus.Approved).ToList()
				: live.Where(d => request.DatasetIds.Contains(d.Id, StringComparer.OrdinalIgnoreCase)).ToList();
			// Accepted configuration values go with every wave (section 7.5); a wave may be configuration alone.
			var configuration = await _configuration.GetPendingAsync(departmentId, workspaceId, cancellationToken);
			if (chosen.Count == 0 && configuration.Changes.Count == 0)
				throw new DepartmentImportException("import_nothing_ready");
			if (configuration.Issues.Count > 0)
				throw new DepartmentImportException("import_configuration_issues", 409);
			if (chosen.Any(d => d.Status != (int)ImportDatasetStatus.Ready && d.Status != (int)ImportDatasetStatus.Approved))
				throw new DepartmentImportException("import_dataset_not_ready", 409);
			// Replace is scoped to one kind of record, from one data set (section 12.4).
			if (request.WriteMode == ImportWriteMode.Replace && chosen.Count != 1)
				throw new DepartmentImportException("import_replace_one_dataset", 409);

			// Writing a dataset whose references point at another dataset of this workspace needs that one in the wave or written before.
			var catalog = _catalog.GetVersion(workspace.CatalogVersion) ?? _catalog;
			foreach (var dataset in chosen)
				foreach (var dependency in catalog.GetContract(dataset.EntityKey)?.DependsOn ?? new List<string>())
					if (live.Any(d => string.Equals(d.EntityKey, dependency, StringComparison.OrdinalIgnoreCase) && !chosen.Contains(d) &&
						d.Status != (int)ImportDatasetStatus.Imported && d.Status != (int)ImportDatasetStatus.Verified))
						throw new DepartmentImportException("import_dependency_not_in_wave", 409);

			// L8: the consequences the administrator acknowledged are decided; any left open blocks.
			var now = DateTime.UtcNow;
			foreach (var dataset in chosen)
				foreach (var issue in (await _issues.GetForDatasetAsync(departmentId, dataset.Id, cancellationToken))
						 .Where(i => i.Code == "IMP-CONSEQUENCE-UNDECIDED" && i.Status == (int)ImportIssueStatus.Open && request.AcknowledgedConsequences.Contains(i.FieldKey)))
				{
					issue.Status = (int)ImportIssueStatus.Accepted;
					issue.ResolutionKind = (int)ImportIssueResolutionKind.WarningAccepted;
					issue.ResolvedByKind = (int)ImportActorKind.Administrator;
					issue.ResolvedBy = userId;
					issue.ResolvedOn = now;
					issue.UpdatedOn = now;
					await _issues.UpdateAsync(issue, cancellationToken, true);
				}

			foreach (var dataset in chosen)
				if (await _issues.CountOpenBlockingAsync(departmentId, workspaceId, dataset.Id, cancellationToken) > 0)
					throw new DepartmentImportException("import_blocking_issues", 409);

			// L7: one fresh capacity read; unknown capacity approves nothing that needs seats (section 11.6).
			var personnelSeats = 0;
			var unitSeats = 0;
			foreach (var dataset in chosen)
			{
				var creates = (await _rows.GetDigestInputsAsync(departmentId, dataset.Id, cancellationToken))
					.Count(r => !r.Excluded && !r.IsExample && (r.Decision == (int)ImportRowDecision.Create || r.Decision == (int)ImportRowDecision.Reactivate) &&
						r.ValidationState != (int)ImportRowValidationState.Blocked);
				if (string.Equals(dataset.EntityKey, "personnel", StringComparison.OrdinalIgnoreCase)) personnelSeats += creates;
				if (string.Equals(dataset.EntityKey, "units", StringComparison.OrdinalIgnoreCase)) unitSeats += creates;
			}
			if (personnelSeats > 0 || unitSeats > 0)
			{
				var capacity = await _plan.ReadCapacityAsync(departmentId);
				if (!capacity.Known)
					throw new DepartmentImportException("IMP-PLAN-CAPACITY-UNKNOWN", 409);
				if (!capacity.Covers(personnelSeats, unitSeats))
					throw new DepartmentImportException("IMP-PLAN-CAPACITY", 409);
			}

			var ids = chosen.Select(d => d.Id).ToList();
			var requireChecker = forceChecker || await RequiresSecondApproverAsync(departmentId, workspace, request.WriteMode);
			var run = new ImportRun
			{
				DepartmentId = departmentId,
				WorkspaceId = workspaceId,
				Kind = (int)ImportRunKind.Commit,
				DatasetIds = JsonConvert.SerializeObject(ids),
				WriteMode = (int)request.WriteMode,
				ApprovalDigest = ImportConfigurationService.CombineDigest(await _plan.ComputeApprovalDigestAsync(departmentId, ids, request.WriteMode, cancellationToken),
					configuration.Changes),
				RequestedBy = userId,
				RequestedOn = now,
				ApprovedBy = userId,
				ApprovedOn = now,
				Authority = JsonConvert.SerializeObject(authority),
				Versions = JsonConvert.SerializeObject(new ImportRunVersions
				{
					Engine = ImportPlanService.EngineVersion,
					Catalog = workspace.CatalogVersion,
					Contracts = chosen.GroupBy(d => d.EntityKey).ToDictionary(g => g.Key, g => g.First().ContractVersion)
				}),
				ConsequenceDecisions = JsonConvert.SerializeObject(new ImportConsequenceDecisions
				{
					SuppressExpiredCertificationNotices = request.SuppressExpiredCertificationNotices,
					RelaxCertificationEnforcement = request.RelaxCertificationEnforcement,
					Acknowledged = request.AcknowledgedConsequences ?? new List<string>()
				}),
				CorrelationId = Guid.NewGuid().ToString("N"),
				UpdatedOn = now
			};

			if (requireChecker)
			{
				run.Status = (int)ImportRunStatus.AwaitingChecker;
				await _runs.InsertAsync(run, cancellationToken, true);
			}
			else
			{
				await _queue.QueueAsync(run, cancellationToken);
			}

			foreach (var dataset in chosen)
			{
				dataset.Status = (int)ImportDatasetStatus.Approved;
				dataset.ApprovedRunId = run.Id;
				dataset.UpdatedOn = now;
				if (!await _datasets.UpdateWithRevisionAsync(dataset, dataset.Revision, cancellationToken))
					throw new DepartmentImportException("import_dataset_changed", 409);
			}

			foreach (var change in configuration.Changes)
			{
				change.ApprovedRunId = run.Id;
				change.UpdatedOn = now;
				if (!await _configurationChanges.UpdateWithRevisionAsync(change, change.Revision, cancellationToken))
					throw new DepartmentImportException("import_configuration_changed", 409);
			}

			_effects.Audit(departmentId, userId, AuditLogTypes.DepartmentImportApproved,
				JsonConvert.SerializeObject(new { runId = run.Id, workspaceId, datasets = ids.Count, configuration = configuration.Changes.Count, writeMode = request.WriteMode.ToString(), awaitingChecker = requireChecker }));
			await _timeline.RecordAsync(departmentId, workspaceId, ImportActorKind.Administrator, userId, requireChecker ? "ApprovalAwaitingChecker" : "Approved",
				new { runId = run.Id, datasets = ids.Count, configuration = configuration.Changes.Count, consequences = request.AcknowledgedConsequences?.Count ?? 0 }, run.CorrelationId, cancellationToken);
			return run;
		}

		public async Task<ImportRun> ApproveAsCheckerAsync(int departmentId, string userId, string runId, CancellationToken cancellationToken = default)
		{
			if (!await _access.CanManageAsync(departmentId, userId))
				throw new DepartmentImportException("import_not_allowed", 403);

			var run = await _runs.GetByIdAsync(departmentId, runId, cancellationToken) ?? throw new DepartmentImportException("import_run_not_found", 404);
			var authority = Authority(run);
			// Department sign-off covers staff-run rollbacks as well as commits; an administrator's own rollback needs no checker.
			var signable = run.Kind == (int)ImportRunKind.Commit || run.Kind == (int)ImportRunKind.Rollback && authority?.IsStaff == true;
			if (!signable || run.Status != (int)ImportRunStatus.AwaitingChecker)
				throw new DepartmentImportException("import_run_not_awaiting_checker", 409);
			if (string.Equals(run.ApprovedBy, userId, StringComparison.OrdinalIgnoreCase))
				// Two people, not one person twice (section 10.2, L9 "second approver distinct").
				throw new DepartmentImportException("import_checker_must_differ", 409);
			// A staff-run import needs its staff checker first; the department's sign-off is the third signature (section 17.8).
			if (authority?.IsStaff == true && string.IsNullOrWhiteSpace(authority.StaffChecker))
				throw new DepartmentImportException("import_run_not_awaiting_checker", 409);

			if (run.Kind == (int)ImportRunKind.Commit)
			{
				var ids = JsonConvert.DeserializeObject<List<string>>(run.DatasetIds ?? "[]");
				var digest = ImportConfigurationService.CombineDigest(await _plan.ComputeApprovalDigestAsync(departmentId, ids, (ImportWriteMode)run.WriteMode, cancellationToken),
					await _configurationChanges.GetForRunAsync(departmentId, run.Id, cancellationToken));
				if (digest != run.ApprovalDigest)
					throw new DepartmentImportException("IMP-APPROVAL-STALE", 409);
			}

			run.SecondApprovedBy = userId;
			run.SecondApprovedOn = DateTime.UtcNow;
			run.Status = (int)ImportRunStatus.Queued;
			run.UpdatedOn = DateTime.UtcNow;
			await _runs.UpdateAsync(run, cancellationToken, true);
			await _queue.NudgeAsync(run);

			await _timeline.RecordAsync(departmentId, run.WorkspaceId, ImportActorKind.Administrator, userId, "ApprovalChecked", new { runId }, run.CorrelationId, cancellationToken);
			return run;
		}

		private static ImportAuthority Authority(ImportRun run) =>
			string.IsNullOrWhiteSpace(run?.Authority) ? null : JsonConvert.DeserializeObject<ImportAuthority>(run.Authority);

		public async Task<ImportRollbackPreview> GetRollbackPreviewAsync(int departmentId, string commitRunId, CancellationToken cancellationToken = default)
		{
			var run = await _runs.GetByIdAsync(departmentId, commitRunId, cancellationToken) ?? throw new DepartmentImportException("import_run_not_found", 404);
			var preview = new ImportRollbackPreview { CommitRun = run };
			var (allowed, code, windowEnds) = await RollbackAllowedAsync(departmentId, run, cancellationToken);
			preview.CanRollback = allowed;
			preview.RefusalCode = code;
			preview.WindowEndsOn = windowEnds;

			foreach (var entry in await _journal.GetForRunDescendingAsync(departmentId, run.Id, cancellationToken))
			{
				if (!preview.Entries.TryGetValue(entry.EntityType, out var operations))
					preview.Entries[entry.EntityType] = operations = new Dictionary<string, int>();
				var operation = ((ImportJournalOperation)entry.Operation).ToString();
				operations[operation] = (operations.TryGetValue(operation, out var n) ? n : 0) + 1;
			}

			return preview;
		}

		public async Task<ImportRun> RequestRollbackAsync(int departmentId, string userId, string commitRunId, CancellationToken cancellationToken = default)
		{
			if (!await _access.CanManageAsync(departmentId, userId))
				throw new DepartmentImportException("import_not_allowed", 403);

			var commit = await _runs.GetByIdAsync(departmentId, commitRunId, cancellationToken) ?? throw new DepartmentImportException("import_run_not_found", 404);
			var (allowed, code, _) = await RollbackAllowedAsync(departmentId, commit, cancellationToken);
			if (!allowed)
				throw new DepartmentImportException(code, 409);

			var run = await _queue.QueueAsync(new ImportRun
			{
				DepartmentId = departmentId,
				WorkspaceId = commit.WorkspaceId,
				Kind = (int)ImportRunKind.Rollback,
				ParentRunId = commit.Id,
				DatasetIds = commit.DatasetIds,
				WriteMode = commit.WriteMode,
				RequestedBy = userId,
				ApprovedBy = userId,
				ApprovedOn = DateTime.UtcNow,
				Authority = JsonConvert.SerializeObject(ImportAuthority.ForAdministrator(departmentId, userId))
			}, cancellationToken);

			await _timeline.RecordAsync(departmentId, commit.WorkspaceId, ImportActorKind.Administrator, userId, "RollbackRequested",
				new { runId = run.Id, commitRunId }, run.CorrelationId, cancellationToken);
			return run;
		}

		private async Task<(bool Allowed, string Code, DateTime? WindowEnds)> RollbackAllowedAsync(int departmentId, ImportRun commit, CancellationToken cancellationToken)
		{
			// A cancelled commit keeps what it wrote before the chunk boundary it stopped at; that part rolls back like a finished one.
			var finished = commit.Kind == (int)ImportRunKind.Commit && (commit.Status == (int)ImportRunStatus.Completed ||
				commit.Status == (int)ImportRunStatus.Cancelled && (await _journal.GetForRunDescendingAsync(departmentId, commit.Id, cancellationToken)).Count > 0);
			if (!finished)
				return (false, "import_rollback_commit_not_completed", null);

			var runs = await _runs.GetAllForWorkspaceAsync(departmentId, commit.WorkspaceId, cancellationToken);
			if (runs.Any(r => r.Kind == (int)ImportRunKind.Rollback && r.ParentRunId == commit.Id))
				return (false, "import_rollback_exists", null);
			if ((await _runs.GetOpenForDepartmentAsync(departmentId, cancellationToken)).Any(r => r.Kind == (int)ImportRunKind.Commit || r.Kind == (int)ImportRunKind.Rollback))
				return (false, "import_run_open", null);

			// Decision D7: a new department may roll back until activation plus the window; a live one for the window after the commit.
			var workspace = await _workspaces.GetByIdAsync(departmentId, commit.WorkspaceId, cancellationToken);
			DateTime? windowEnds = workspace?.Mode == (int)ImportWorkspaceMode.NewDepartment
				? workspace.ActivatedOn?.AddDays(ImportConfig.RollbackWindowDays)
				: (commit.CompletedOn ?? commit.UpdatedOn).AddDays(ImportConfig.RollbackWindowDays);
			if (windowEnds.HasValue && windowEnds.Value < DateTime.UtcNow)
				return (false, "import_rollback_window_closed", windowEnds);

			return (true, null, windowEnds);
		}

		/// <summary>
		/// Replace is support-assisted (section 5.2): an administrator's approval needs Resgrid support to hold Prepare or more
		/// on the workspace; a staff request needs an Operate grant whose allowed actions include Replace. Either way a second
		/// approval follows (RequiresSecondApproverAsync, staff maker-checker).
		/// </summary>
		private async Task RequireReplaceAuthorityAsync(int departmentId, string workspaceId, ImportAuthority authority, CancellationToken cancellationToken)
		{
			var grant = DepartmentImportSupportService.ActiveGrant(await _grants.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken));
			if (authority?.IsStaff == true)
			{
				if (grant == null || grant.Level != (int)ImportSupportAccessLevel.Operate || !((ImportOperateActions)grant.AllowedActions).HasFlag(ImportOperateActions.Replace))
					throw new DepartmentImportException("import_replace_not_allowed", 403);
				return;
			}

			if (grant == null || grant.Level < (int)ImportSupportAccessLevel.Prepare)
				throw new DepartmentImportException("import_replace_needs_support", 409);
		}

		private async Task<bool> RequiresSecondApproverAsync(int departmentId, ImportWorkspace workspace, ImportWriteMode mode)
		{
			if (workspace.RequireSecondApprover || mode == ImportWriteMode.Replace)
				return true;
			var config = await _settings.GetDepartmentImportConfigAsync(departmentId);
			return config?.RequireSecondApprover == true;
		}

		private async Task<ImportWorkspace> RequireWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken) =>
			await _workspaces.GetByIdAsync(departmentId, workspaceId, cancellationToken) ?? throw new DepartmentImportException("import_workspace_not_found", 404);
	}
}
