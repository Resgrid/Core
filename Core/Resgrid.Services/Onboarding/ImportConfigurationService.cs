using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Localization;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Onboarding.Writers;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// The Configure step and the configuration part of a commit (wizard plan sections 6.3 step 9, 7.2 and 7.5).
	/// Suggestions are composed on every read and never stored:
	/// <list type="bullet">
	/// <item>modules the operating profile's Admin Assist packs work in, when they are turned off (Preset);</item>
	/// <item>a few names departments of one kind commonly use (search and rescue "Missions", security "Officers") when the
	/// term still has Resgrid's name (Preset);</item>
	/// <item>under an upstream CAD, call creation for department and group admins only (Preset);</item>
	/// <item>modules the workspace's data sets need, when they are turned off (ImportedData);</item>
	/// <item>the map centre at the middle of the imported stations, when the department has none (ImportedData).</item>
	/// </list>
	/// The administrator accepts, edits or rejects each one, or sets any item; only those decisions are stored. Accepted
	/// values that differ from the department's configuration are approved with the next wave and written first in its
	/// commit run, each before-value journaled before the write.
	/// </summary>
	public class ImportConfigurationService : IImportConfigurationService
	{
		private static readonly ResourceManager Strings =
			new ResourceManager("Resgrid.Localization.Areas.User.DepartmentImport.DepartmentImport", typeof(SupportedLocales).Assembly);

		/// <summary>Admin Assist product areas → the module switch that turns the area on.</summary>
		private static readonly IReadOnlyDictionary<string, string> AreaModules = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["shifts"] = "Shifts",
			["training"] = "Training",
			["mapping"] = "Mapping",
			["checklists"] = "Checklists",
			["maintenance"] = "Maintenance",
			["inventory"] = "Inventory",
			["knowledge"] = "Documents",
			["records"] = "Logs",
			["communication"] = "Messaging",
			["business"] = "BusinessOperations"
		};

		/// <summary>Archetype → term key → the DepartmentImport resource with the suggested name (in the administrator's language).</summary>
		private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ArchetypeTerms =
			new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
			{
				["sar"] = new Dictionary<string, string> { [DepartmentTerms.Calls] = "Preset.Term.sar.calls", [DepartmentTerms.Units] = "Preset.Term.sar.units" },
				["security"] = new Dictionary<string, string> { [DepartmentTerms.Calls] = "Preset.Term.security.calls", [DepartmentTerms.Personnel] = "Preset.Term.security.personnel" },
				["industrial"] = new Dictionary<string, string> { [DepartmentTerms.Calls] = "Preset.Term.industrial.calls" }
			};

		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IImportDatasetsRepository _datasets;
		private readonly IImportRunsRepository _runs;
		private readonly IImportConfigurationChangesRepository _changes;
		private readonly IImportArtifactsRepository _artifacts;
		private readonly IImportCatalog _catalog;
		private readonly IDepartmentImportAccessService _access;
		private readonly IDepartmentSettingsService _settings;
		private readonly IAdminAssistCatalog _assistCatalog;
		private readonly IAdminAssistAccessService _assistAccess;
		private readonly IPersonnelRolesService _roles;
		private readonly ImportConfigurationTargets _targets;
		private readonly IImportTimelineService _timeline;
		private readonly ImportEffectsRunner _effects;
		/// <summary>Lazy: reading staged rows builds the whole writer graph, and most Configure reads never need it.</summary>
		private readonly Lazy<IImportValidationService> _validation;
		private readonly IImportSourcesRepository _sources;

		/// <summary>Staged station rows read for the map centre suggestion, per data set (stations lists are small; this bounds a huge one).</summary>
		public const int StationRowsRead = 2000;

		public ImportConfigurationService(IImportWorkspacesRepository workspaces, IImportDatasetsRepository datasets, IImportRunsRepository runs,
			IImportConfigurationChangesRepository changes, IImportArtifactsRepository artifacts, IImportCatalog catalog, IDepartmentImportAccessService access,
			IDepartmentSettingsService settings, IAdminAssistCatalog assistCatalog, IAdminAssistAccessService assistAccess, IPersonnelRolesService roles,
			ImportConfigurationTargets targets, IImportTimelineService timeline, ImportEffectsRunner effects, Lazy<IImportValidationService> validation,
			IImportSourcesRepository sources)
		{
			_validation = validation;
			_sources = sources;
			_workspaces = workspaces;
			_datasets = datasets;
			_runs = runs;
			_changes = changes;
			_artifacts = artifacts;
			_catalog = catalog;
			_access = access;
			_settings = settings;
			_assistCatalog = assistCatalog;
			_assistAccess = assistAccess;
			_roles = roles;
			_targets = targets;
			_timeline = timeline;
			_effects = effects;
		}

		// ---- The Configure step -------------------------------------------------------------------------------------

		public async Task<ImportConfigurationView> GetAsync(int departmentId, string userId, string workspaceId, CancellationToken cancellationToken = default)
		{
			if (!await _access.CanManageAsync(departmentId, userId))
				throw new DepartmentImportException("import_not_allowed", 403);
			return await BuildViewAsync(departmentId, await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken), userId, true, cancellationToken);
		}

		public async Task<ImportConfigurationView> GetForStaffAsync(int departmentId, string workspaceId, bool stagedValuesVisible,
			CancellationToken cancellationToken = default) =>
			await BuildViewAsync(departmentId, await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken), null, stagedValuesVisible, cancellationToken);

		/// <summary>The view for an administrator, or for staff when <paramref name="userId"/> is null (no permission preview).</summary>
		private async Task<ImportConfigurationView> BuildViewAsync(int departmentId, ImportWorkspace workspace, string userId, bool stagedValues,
			CancellationToken cancellationToken)
		{
			var workspaceId = workspace.Id;
			var profile = await _settings.GetOperatingProfileAsync(departmentId);
			var current = await _targets.ReadAsync(departmentId, ImportConfigurationCatalog.All, false, cancellationToken);
			var suggestions = await SuggestAsync(departmentId, workspace, profile, current, stagedValues, cancellationToken);
			var changes = (await _changes.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
				.GroupBy(c => c.ItemKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
			var view = new ImportConfigurationView
			{
				Workspace = workspace,
				Profile = IsSaved(profile) ? profile : null,
				CanEdit = !DepartmentImportStateMachine.IsTerminal((ImportWorkspaceStatus)workspace.Status) && !await HasOpenRunAsync(departmentId, workspaceId, cancellationToken),
				PermissionPreviewAvailable = userId != null && await PermissionPreviewAvailableAsync(departmentId, userId, cancellationToken),
				Roles = (await _roles.GetRolesForDepartmentAsync(departmentId) ?? new List<PersonnelRole>()).OrderBy(r => r.Name).ToList(),
				Logos = (await _artifacts.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
					.Where(a => a.Kind == (int)ImportArtifactKind.Logo && a.UploadState == (int)ImportArtifactUploadState.Completed && !a.PurgedOn.HasValue)
					.OrderByDescending(a => a.CreatedOn).ToList()
			};

			var issues = await IssuesAsync(departmentId, changes.Values.ToList(), current, view.PermissionPreviewAvailable, view.Roles, view.Logos, cancellationToken);
			// Record numbering shows only while Records is on, unless decisions were made while it was (they can then be undone).
			var recordsAvailable = await _targets.RecordsAvailableAsync(departmentId);
			foreach (var group in ImportConfigurationGroups.All)
			{
				if (group == ImportConfigurationGroups.Records && !recordsAvailable &&
					!changes.Keys.Any(k => ImportConfigurationCatalog.Find(k)?.Group == ImportConfigurationGroups.Records))
					continue;

				var groupView = new ImportConfigurationGroupView { Key = group };
				foreach (var item in ImportConfigurationCatalog.All.Where(i => i.Group == group))
				{
					changes.TryGetValue(item.Key, out var change);
					suggestions.TryGetValue(item.Key, out var suggestion);
					var currentValue = current.TryGetValue(item.Key, out var value) ? value : string.Empty;
					var row = new ImportConfigurationRowView
					{
						Item = item,
						Current = currentValue,
						Suggested = suggestion.Value,
						SuggestedOrigin = suggestion.Value == null ? (ImportConfigurationOrigin?)null : suggestion.Origin,
						SuggestedOriginKey = suggestion.OriginKey,
						Change = change,
						Pending = IsPending(change, currentValue),
						AwaitingDecision = suggestion.Value != null && change == null,
						IssueCode = issues.TryGetValue(item.Key, out var code) ? code : null
					};
					groupView.Rows.Add(row);
				}

				groupView.Pending = groupView.Rows.Count(r => r.Pending);
				groupView.Suggestions = groupView.Rows.Count(r => r.AwaitingDecision);
				view.Groups.Add(groupView);
			}

			view.Pending = view.Groups.Sum(g => g.Pending);
			view.Suggestions = view.Groups.Sum(g => g.Suggestions);
			return view;
		}

		public async Task<ImportConfigurationChange> DecideAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId,
			ImportConfigurationDecision decision, CancellationToken cancellationToken = default)
		{
			var workspace = await RequireEditableAsync(departmentId, actor, actorKind, workspaceId, cancellationToken);
			var item = ImportConfigurationCatalog.Find(decision?.ItemKey) ?? throw new DepartmentImportException("IMP-CONFIG-UNKNOWN");
			var staff = actorKind == ImportActorKind.ResgridStaff;
			// Who can do what stays the department's call: its impact preview is the administrator's (section 8.1 A).
			if (staff && item.Group == ImportConfigurationGroups.Access)
				throw new DepartmentImportException("IMP-CONFIG-STAFF-PERMISSION", 403);
			if (item.Group == ImportConfigurationGroups.Records && !await _targets.RecordsAvailableAsync(departmentId))
				throw new DepartmentImportException("IMP-CONFIG-UNAVAILABLE", 409);

			var current = await _targets.ReadAsync(departmentId, ImportConfigurationCatalog.All, false, cancellationToken);
			var profile = await _settings.GetOperatingProfileAsync(departmentId);
			// Whoever decides may see staged values: an administrator, or staff at Prepare or more.
			var suggestions = await SuggestAsync(departmentId, workspace, profile, current, true, cancellationToken);
			suggestions.TryGetValue(item.Key, out var suggestion);

			string value;
			if (decision.Accept)
			{
				var raw = decision.UseSuggestion ? suggestion.Value ?? throw new DepartmentImportException("IMP-CONFIG-NO-SUGGESTION") : decision.Value;
				var (canonical, code) = ImportConfigurationCatalog.Canonicalize(item, raw);
				if (code != null)
					throw new DepartmentImportException(code);
				value = canonical;
				await RequireWritableAsync(departmentId, actor, workspaceId, item, value, cancellationToken);
			}
			else
			{
				value = suggestion.Value ?? ImportConfigurationCatalog.Canonicalize(item, decision.Value).Value ?? string.Empty;
			}

			var fromSuggestion = suggestion.Value != null && string.Equals(suggestion.Value, value, StringComparison.Ordinal);
			var now = DateTime.UtcNow;
			var existing = (await _changes.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).FirstOrDefault(c => c.ItemKey == item.Key);
			// Staff prepare drafts; a value the department decided is the department's, whatever staff think of it (they can message).
			if (staff && existing != null && !existing.StaffAuthored && existing.State != (int)ImportConfigurationChangeState.Applied &&
				existing.State != (int)ImportConfigurationChangeState.RolledBack)
				throw new DepartmentImportException("IMP-CONFIG-DEPARTMENT-DECISION", 409);
			var change = existing ?? new ImportConfigurationChange
			{
				DepartmentId = departmentId,
				WorkspaceId = workspaceId,
				ItemKey = item.Key,
				CreatedBy = actor,
				CreatedOn = now
			};
			change.Origin = (int)(fromSuggestion ? suggestion.Origin : staff ? ImportConfigurationOrigin.Staff : ImportConfigurationOrigin.Administrator);
			change.OriginKey = fromSuggestion ? suggestion.OriginKey : null;
			change.Value = value;
			change.State = (int)(decision.Accept ? ImportConfigurationChangeState.Accepted : ImportConfigurationChangeState.Rejected);
			change.ApprovedRunId = null;
			change.FailureCode = null;
			// The last decider owns it: an administrator deciding over a staff draft makes it the department's own.
			change.StaffAuthored = staff;
			change.UpdatedBy = actor;
			change.UpdatedOn = now;

			if (existing == null)
				await _changes.InsertAsync(change, cancellationToken, true);
			else if (!await _changes.UpdateWithRevisionAsync(change, existing.Revision, cancellationToken))
				throw new DepartmentImportException("import_configuration_changed", 409);

			await _workspaces.TouchAsync(departmentId, workspaceId, now, cancellationToken);
			await _timeline.RecordAsync(departmentId, workspaceId, actorKind, actor, decision.Accept ? "ConfigurationAccepted" : "ConfigurationRejected",
				new { item = item.Key, origin = ((ImportConfigurationOrigin)change.Origin).ToString() }, null, cancellationToken);
			return change;
		}

		public async Task<int> AcceptSuggestionsAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, string group,
			CancellationToken cancellationToken = default)
		{
			var view = actorKind == ImportActorKind.Administrator
				? await GetAsync(departmentId, actor, workspaceId, cancellationToken)
				: await GetForStaffAsync(departmentId, workspaceId, true, cancellationToken);
			if (!view.CanEdit)
				throw new DepartmentImportException("import_run_open", 409);

			var accepted = 0;
			foreach (var row in view.Groups.Where(g => string.IsNullOrWhiteSpace(group) || g.Key == group).SelectMany(g => g.Rows).Where(r => r.AwaitingDecision))
			{
				// A permission change needs its impact preview; one this administrator cannot see waits for a single decision.
				if (row.Item.Group == ImportConfigurationGroups.Access && !view.PermissionPreviewAvailable)
					continue;
				await DecideAsync(departmentId, actor, actorKind, workspaceId, new ImportConfigurationDecision { ItemKey = row.Item.Key, Accept = true, UseSuggestion = true },
					cancellationToken);
				accepted++;
			}

			return accepted;
		}

		public async Task<bool> ResetAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, string itemKey,
			CancellationToken cancellationToken = default)
		{
			await RequireEditableAsync(departmentId, actor, actorKind, workspaceId, cancellationToken);
			var change = (await _changes.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).FirstOrDefault(c => c.ItemKey == itemKey);
			if (change == null)
				return false;
			// What a run wrote stays on the record (its journal is the rollback's source); a new value can still be accepted over it.
			if (change.State == (int)ImportConfigurationChangeState.Applied)
				throw new DepartmentImportException("import_configuration_applied", 409);
			// Staff take back their own drafts; the department's decisions are the department's.
			if (actorKind == ImportActorKind.ResgridStaff && !change.StaffAuthored)
				throw new DepartmentImportException("IMP-CONFIG-NOT-STAFF-DECISION", 403);

			await _changes.DeleteAsync(change, cancellationToken);
			await _timeline.RecordAsync(departmentId, workspaceId, actorKind, actor, "ConfigurationReset", new { item = itemKey }, null, cancellationToken);
			return true;
		}

		public async Task<ImportArtifact> UploadLogoAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, string fileName,
			string contentType, byte[] data, CancellationToken cancellationToken = default)
		{
			await RequireEditableAsync(departmentId, actor, actorKind, workspaceId, cancellationToken);
			if (data == null || data.Length == 0 || data.Length > DepartmentLogoRenditions.MaxBytes)
				throw new DepartmentImportException("IMP-CONFIG-LOGO-REJECTED");

			try
			{
				// Decodes the image exactly as the write will (PNG or JPEG, long edge at least 200 px); nothing is kept from this.
				DepartmentLogoRenditions.Build(data);
			}
			catch (DepartmentLogoRejectedException)
			{
				throw new DepartmentImportException("IMP-CONFIG-LOGO-REJECTED");
			}

			var now = DateTime.UtcNow;
			var config = await _settings.GetDepartmentImportConfigAsync(departmentId);
			var name = System.IO.Path.GetFileName(fileName ?? "logo");
			var artifact = new ImportArtifact
			{
				DepartmentId = departmentId,
				WorkspaceId = workspaceId,
				Kind = (int)ImportArtifactKind.Logo,
				OriginalName = name.Length > 200 ? name.Substring(0, 200) : name,
				DeclaredContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType.Length > 128 ? contentType.Substring(0, 128) : contentType,
				ExpectedSizeBytes = data.Length,
				Sha256Verified = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
				// Decoded and re-encoded into renditions when written; the uploaded bytes are never served (section 14.1).
				ScanState = (int)ImportArtifactScanState.NotRequired,
				UploadState = (int)ImportArtifactUploadState.Completed,
				UploadedParts = "[]",
				Classification = (int)ImportFieldSensitivity.Internal,
				StaffAuthored = actorKind == ImportActorKind.ResgridStaff,
				CreatedBy = actor,
				CreatedOn = now,
				UpdatedOn = now,
				CompletedOn = now,
				RetentionDeadline = now.Add(config.ArtifactRetentionAfterFailure())
			};

			await _targets.StoreLogoAsync(artifact, data, cancellationToken);
			await _artifacts.InsertAsync(artifact, cancellationToken, true);
			await _timeline.RecordAsync(departmentId, workspaceId, actorKind, actor, "LogoUploaded", new { artifactId = artifact.Id }, null, cancellationToken);
			return artifact;
		}

		// ---- Approval -----------------------------------------------------------------------------------------------

		public async Task<ImportConfigurationPending> GetPendingAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default)
		{
			var result = new ImportConfigurationPending();
			var all = await _changes.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			result.Decided = all.Count;
			var changes = all.Where(c => c.State == (int)ImportConfigurationChangeState.Accepted && ImportConfigurationCatalog.Find(c.ItemKey) != null).ToList();
			if (changes.Count == 0)
				return result;

			var current = await _targets.ReadAsync(departmentId, ImportConfigurationCatalog.All, false, cancellationToken);
			result.Changes = changes.Where(c => IsPending(c, current.TryGetValue(c.ItemKey, out var value) ? value : string.Empty))
				.OrderBy(c => ImportConfigurationCatalog.Find(c.ItemKey).Order).ToList();
			if (result.Changes.Count == 0)
				return result;

			var roles = await _roles.GetRolesForDepartmentAsync(departmentId) ?? new List<PersonnelRole>();
			var logos = (await _artifacts.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).Where(a => a.Kind == (int)ImportArtifactKind.Logo).ToList();
			// Approval re-checks values, not who previewed them: the preview requirement was met when each permission was accepted.
			result.Issues = await IssuesAsync(departmentId, result.Changes, current, true, roles, logos, cancellationToken);
			return result;
		}

		/// <summary>
		/// The approval digest of a wave with configuration (section 10.2): the data set digest alone when there is none, so an
		/// approval without configuration keeps its digest. Covers each value's item and canonical form, never its state.
		/// </summary>
		public static string CombineDigest(string datasetDigest, IEnumerable<ImportConfigurationChange> configuration)
		{
			var items = (configuration ?? Enumerable.Empty<ImportConfigurationChange>()).OrderBy(c => c.ItemKey, StringComparer.Ordinal).ToList();
			if (items.Count == 0)
				return datasetDigest;

			var builder = new StringBuilder(datasetDigest).Append("\nconfiguration\n");
			foreach (var change in items)
				builder.Append(change.Id).Append('|').Append(change.ItemKey).Append('|').Append(Hash(change.Value ?? string.Empty)).Append('\n');
			return Hash(builder.ToString());
		}

		// ---- Commit -------------------------------------------------------------------------------------------------

		public async Task<ImportConfigurationApplyResult> ApplyAsync(ImportRun run, string actingUserId, Func<ImportChangeJournalEntry, CancellationToken, Task> journal,
			CancellationToken cancellationToken = default)
		{
			var result = new ImportConfigurationApplyResult();
			var changes = (await _changes.GetForRunAsync(run.DepartmentId, run.Id, cancellationToken))
				.Where(c => c.State == (int)ImportConfigurationChangeState.Accepted && ImportConfigurationCatalog.Find(c.ItemKey) != null)
				.OrderBy(c => ImportConfigurationCatalog.Find(c.ItemKey).Order).ToList();
			if (changes.Count == 0)
				return result;

			var items = changes.Select(c => ImportConfigurationCatalog.Find(c.ItemKey)).ToList();
			var current = await _targets.ReadAsync(run.DepartmentId, items, true, cancellationToken);
			var now = DateTime.UtcNow;

			foreach (var change in changes)
			{
				var item = ImportConfigurationCatalog.Find(change.ItemKey);
				var before = current.TryGetValue(item.Key, out var value) ? value : string.Empty;
				if (string.Equals(before, change.Value ?? string.Empty, StringComparison.Ordinal))
				{
					// Someone set it the same way since approval: nothing to write or undo.
					result.Unchanged++;
					await MarkAsync(change, ImportConfigurationChangeState.Applied, run.Id, null, now, cancellationToken);
					continue;
				}

				// Journaled before the write: a slice that stops between the two leaves an entry rollback sees as already undone.
				await journal(new ImportChangeJournalEntry
				{
					EntityType = ImportConfigurationCatalog.JournalEntityType,
					TargetKey = item.Key,
					Operation = (int)ImportJournalOperation.Setting,
					BeforeValues = JsonConvert.SerializeObject(new ConfigurationJournalValues { Before = before, After = change.Value ?? string.Empty })
				}, cancellationToken);

				ImportConfigurationWriteResult written;
				try
				{
					written = await _targets.WriteAsync(run.DepartmentId, actingUserId, run.WorkspaceId, item, change.Value, cancellationToken);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException) && !ImportCommitHandler.IsTransient(ex))
				{
					Logging.LogException(ex, $"Department import commit {run.Id}: configuration item {item.Key} was not written", run.CorrelationId);
					written = ImportConfigurationWriteResult.Refused("IMP-CONFIG-WRITE-FAILED");
				}

				if (written.Written)
				{
					result.Written++;
					result.Groups.Add(item.Group);
					current[item.Key] = change.Value ?? string.Empty;
					await MarkAsync(change, ImportConfigurationChangeState.Applied, run.Id, null, now, cancellationToken);
				}
				else
				{
					result.Failed++;
					await MarkAsync(change, ImportConfigurationChangeState.Failed, null, written.Code, now, cancellationToken);
				}
			}

			await RunEffectsAsync(run.DepartmentId, result.Groups, cancellationToken);
			if (result.Written > 0)
			{
				// One summary audit per run (section 11.2.2); the journal holds each item's before and after values.
				var keys = changes.Where(c => c.State == (int)ImportConfigurationChangeState.Applied && c.AppliedRunId == run.Id).Select(c => c.ItemKey).ToList();
				_effects.Audit(run.DepartmentId, actingUserId, AuditLogTypes.DepartmentSettingsChanged,
					JsonConvert.SerializeObject(new { runId = run.Id, workspaceId = run.WorkspaceId, source = "department-import", items = keys.Where(k => !k.StartsWith("permission.", StringComparison.Ordinal)) }));
				if (result.Groups.Contains(ImportConfigurationGroups.Access))
					_effects.Audit(run.DepartmentId, actingUserId, AuditLogTypes.PermissionsChanged,
						JsonConvert.SerializeObject(new { runId = run.Id, workspaceId = run.WorkspaceId, source = "department-import", items = keys.Where(k => k.StartsWith("permission.", StringComparison.Ordinal)) }));
			}

			await _timeline.RecordAsync(run.DepartmentId, run.WorkspaceId, ImportActorKind.System, null, "ConfigurationApplied",
				new { runId = run.Id, written = result.Written, unchanged = result.Unchanged, failed = result.Failed }, run.CorrelationId, cancellationToken);
			return result;
		}

		public async Task<(long Expected, long Actual)> VerifyAsync(ImportRun run, CancellationToken cancellationToken = default)
		{
			var applied = (await _changes.GetForRunAsync(run.DepartmentId, run.Id, cancellationToken))
				.Where(c => c.State == (int)ImportConfigurationChangeState.Applied && c.AppliedRunId == run.Id && ImportConfigurationCatalog.Find(c.ItemKey) != null).ToList();
			if (applied.Count == 0)
				return (0, 0);

			var current = await _targets.ReadAsync(run.DepartmentId, applied.Select(c => ImportConfigurationCatalog.Find(c.ItemKey)), true, cancellationToken);
			var actual = applied.Count(c => ReadsBack(ImportConfigurationCatalog.Find(c.ItemKey), current.TryGetValue(c.ItemKey, out var value) ? value : string.Empty, c.Value));
			return (applied.Count, actual);
		}

		// ---- Rollback -----------------------------------------------------------------------------------------------

		public async Task<ImportRollbackEntryResult> RollbackAsync(ImportRun rollbackRun, string actingUserId, ImportChangeJournalEntry entry,
			CancellationToken cancellationToken = default)
		{
			var result = new ImportRollbackEntryResult { JournalEntryId = entry.Id };
			var item = ImportConfigurationCatalog.Find(entry.TargetKey);
			var values = string.IsNullOrWhiteSpace(entry.BeforeValues) ? null : JsonConvert.DeserializeObject<ConfigurationJournalValues>(entry.BeforeValues);
			if (item == null || values == null)
			{
				result.Outcome = ImportRollbackOutcome.ManualReconciliation;
				result.Code = "IMP-ROLLBACK-NO-WRITER";
				return result;
			}

			var (outcome, code) = await _targets.RestoreAsync(entry.DepartmentId, actingUserId, entry.WorkspaceId, item, values.Before, values.After, cancellationToken);
			result.Outcome = outcome;
			result.Code = code;

			if (outcome == ImportRollbackOutcome.Restored || outcome == ImportRollbackOutcome.None && code == null)
			{
				var change = (await _changes.GetAllForWorkspaceAsync(entry.DepartmentId, entry.WorkspaceId, cancellationToken))
					.FirstOrDefault(c => c.ItemKey == item.Key && c.AppliedRunId == entry.RunId && c.State == (int)ImportConfigurationChangeState.Applied);
				if (change != null)
					await MarkAsync(change, ImportConfigurationChangeState.RolledBack, change.AppliedRunId, null, DateTime.UtcNow, cancellationToken);
			}

			if (outcome == ImportRollbackOutcome.Restored)
				await RunEffectsAsync(entry.DepartmentId, new HashSet<string> { item.Group }, cancellationToken);
			return result;
		}

		// ---- Helpers ------------------------------------------------------------------------------------------------

		private sealed class ConfigurationJournalValues
		{
			public string Before { get; set; }
			public string After { get; set; }
		}

		private async Task RunEffectsAsync(int departmentId, ICollection<string> groups, CancellationToken cancellationToken)
		{
			if (groups.Count == 0)
				return;
			// Permissions change who sees whom: refresh the visibility caches as the Security screen does.
			if (groups.Contains(ImportConfigurationGroups.Access))
				await _effects.RunChunkEffectsAsync(departmentId, ImportEffects.PersonnelVisibility | ImportEffects.UnitVisibility | ImportEffects.GroupVisibility);
			// The apps read names, modules and map settings at sign-in and on this event; caches are cleared after it.
			await _effects.RunRunEffectsAsync(departmentId, ImportEffects.AppsResync, cancellationToken);
		}

		private async Task MarkAsync(ImportConfigurationChange change, ImportConfigurationChangeState state, string appliedRunId, string failureCode, DateTime now,
			CancellationToken cancellationToken)
		{
			change.State = (int)state;
			change.AppliedRunId = appliedRunId;
			change.AppliedOn = state == ImportConfigurationChangeState.Applied ? now : change.AppliedOn;
			change.FailureCode = failureCode;
			change.UpdatedOn = now;
			await _changes.UpdateWithRevisionAsync(change, change.Revision, cancellationToken);
		}

		private static bool IsPending(ImportConfigurationChange change, string current) =>
			change != null && change.State == (int)ImportConfigurationChangeState.Accepted &&
			!string.Equals(change.Value ?? string.Empty, current ?? string.Empty, StringComparison.Ordinal);

		/// <summary>An applied value still in place: next numbers only move on from the floor, a logo reads back as present.</summary>
		private static bool ReadsBack(ImportConfigurationItem item, string current, string value)
		{
			if (item.Kind == ImportConfigurationValueKind.Logo)
				return string.IsNullOrEmpty(value) ? current.Length == 0 : current == ImportConfigurationTargets.LogoPresent;
			if (item.Key.EndsWith(".next", StringComparison.Ordinal))
				return ImportCanonical.ToInt(current) >= ImportCanonical.ToInt(value);
			return string.Equals(current, value ?? string.Empty, StringComparison.Ordinal);
		}

		/// <summary>Origin key prefix of a product's word: "product." and the product keys that use it, comma-separated.</summary>
		public const string ProductOriginPrefix = "product.";

		private static int Strength(ImportConfigurationOrigin origin) => origin switch
		{
			ImportConfigurationOrigin.ImportedData => 3,
			ImportConfigurationOrigin.Product => 2,
			_ => 1
		};

		private static bool IsSaved(DepartmentOperatingProfile profile) =>
			profile != null && (profile.Revision > 0 || profile.ReviewedOnUtc.HasValue);

		private async Task<Dictionary<string, (string Value, ImportConfigurationOrigin Origin, string OriginKey)>> SuggestAsync(int departmentId, ImportWorkspace workspace,
			DepartmentOperatingProfile profile, IReadOnlyDictionary<string, string> current, bool stagedValues, CancellationToken cancellationToken)
		{
			var suggestions = new Dictionary<string, (string, ImportConfigurationOrigin, string)>(StringComparer.Ordinal);
			void Suggest(string key, string value, ImportConfigurationOrigin origin, string originKey)
			{
				var item = ImportConfigurationCatalog.Find(key);
				var canonical = ImportConfigurationCatalog.Canonicalize(item, value).Value;
				if (canonical == null || current.TryGetValue(key, out var now) && string.Equals(now, canonical, StringComparison.Ordinal))
					return;
				// The more specific reason wins: imported data over the product the department used, over a preset of its kind.
				if (!suggestions.TryGetValue(key, out var held) || Strength(origin) > Strength(held.Item2) || origin == ImportConfigurationOrigin.ImportedData)
					suggestions[key] = (canonical, origin, originKey);
			}

			var catalog = _catalog.GetVersion(workspace.CatalogVersion) ?? _catalog;

			if (IsSaved(profile))
			{
				var archetypes = profile.Archetypes ?? new List<string>();
				foreach (var pack in (_assistCatalog.Packs ?? new List<OperatingPack>()).Where(p => archetypes.Contains(p.Id, StringComparer.Ordinal)))
					foreach (var area in pack.AreaIds ?? new List<string>())
						if (AreaModules.TryGetValue(area, out var module))
							Suggest("modules." + module, "true", ImportConfigurationOrigin.Preset, "pack." + pack.Id);

				// A term is suggested only when every archetype of the profile that names it agrees, and only over Resgrid's own name.
				var culture = CultureInfo.CurrentUICulture;
				foreach (var term in DepartmentTerms.All)
				{
					if (current.TryGetValue("terminology." + term.Key, out var named) && named.Length > 0)
						continue;
					var texts = archetypes.Where(ArchetypeTerms.ContainsKey).Select(a => (Archetype: a, Key: ArchetypeTerms[a].TryGetValue(term.Key, out var k) ? k : null))
						.Where(x => x.Key != null).Select(x => (x.Archetype, Text: Strings.GetString(x.Key, culture) ?? Strings.GetString(x.Key, CultureInfo.InvariantCulture)))
						.Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
					if (texts.Count > 0 && texts.Select(x => x.Text).Distinct(StringComparer.Ordinal).Count() == 1)
						Suggest("terminology." + term.Key, texts[0].Text, ImportConfigurationOrigin.Preset, "pack." + texts[0].Archetype);
				}

				// Under an upstream CAD the CAD takes calls (section 7.2); members keep updating them.
				if (string.Equals(profile.DispatchModel, "external", StringComparison.Ordinal))
					Suggest("permission." + nameof(PermissionTypes.CreateCall),
						ImportConfigurationCatalog.Permission((int)PermissionActions.DepartmentAndGroupAdmins, false, null), ImportConfigurationOrigin.Preset, "dispatch.external");
			}

			// The words of the products the department is moving from (their packs' glossaries, section 7.4): only over Resgrid's
			// own name, only in the reader's language, and not where two of the products disagree.
			var products = (await _sources.GetAllForWorkspaceAsync(departmentId, workspace.Id, cancellationToken) ?? new List<ImportSource>())
				.Where(s => !string.Equals(s.ProductKey, ImportSourceProducts.Continuation, StringComparison.OrdinalIgnoreCase))
				.Select(s => catalog.GetPack(s.ProductKey)).Where(p => p != null).ToList();
			foreach (var named in ImportGlossary.Names(products, ImportGlossary.LanguageOf(CultureInfo.CurrentUICulture)))
			{
				if (current.TryGetValue("terminology." + named.Key, out var existing) && existing.Length > 0 ||
					string.Equals(named.Value.Text, DepartmentTerms.Find(named.Key)?.DefaultName, StringComparison.OrdinalIgnoreCase))
					continue;
				Suggest("terminology." + named.Key, named.Value.Text, ImportConfigurationOrigin.Product,
					ProductOriginPrefix + string.Join(",", named.Value.Products.Take(4)));
			}

			// Data sets of a module that is turned off would be refused by the entitlement gate (L6).
			var live = (await _datasets.GetAllForWorkspaceAsync(departmentId, workspace.Id, cancellationToken))
				.Where(d => DepartmentImportStateMachine.IsLive(d) && d.EntityKey != null).ToList();
			foreach (var dataset in live)
			{
				var module = catalog.GetContract(dataset.EntityKey)?.Gate?.Module;
				if (!string.IsNullOrWhiteSpace(module) && ImportConfigurationCatalog.Modules.Contains(module))
					Suggest("modules." + module, "true", ImportConfigurationOrigin.ImportedData, dataset.EntityKey);
			}

			// Maps open where the department works: the middle of the stations it is importing, when it has no centre yet.
			if (stagedValues && (!current.TryGetValue("map.center", out var centre) || string.IsNullOrEmpty(centre)))
			{
				var stations = await StationCoordinatesAsync(live, cancellationToken);
				if (stations.Count > 0)
				{
					var (latitude, longitude) = Middle(stations);
					Suggest("map.center", ImportConfigurationCatalog.Coordinates(latitude, longitude), ImportConfigurationOrigin.ImportedData,
						"stations:" + stations.Count.ToString(CultureInfo.InvariantCulture));
				}
			}

			return suggestions;
		}

		/// <summary>
		/// Coordinates of the stations in the workspace's live groups data sets: rows that passed the checks and are not left out,
		/// typed as stations when any row is (otherwise every group with a position). (0, 0) is a missing position, not a station.
		/// </summary>
		private async Task<List<(double Latitude, double Longitude)>> StationCoordinatesAsync(IReadOnlyList<ImportDataset> live, CancellationToken cancellationToken)
		{
			var rows = new List<(double Latitude, double Longitude, bool Station)>();
			foreach (var dataset in live.Where(d => string.Equals(d.EntityKey, "groups", StringComparison.OrdinalIgnoreCase)))
			{
				try
				{
					var after = 0;
					var read = 0;
					while (read < StationRowsRead)
					{
						var page = await _validation.Value.ReadCanonicalRowsAsync(dataset, after, Math.Min(500, StationRowsRead - read), cancellationToken);
						if (page == null || page.Count == 0)
							break;
						read += page.Count;
						after = page[page.Count - 1].RowNumber;

						foreach (var row in page.Where(r => (r.ValidationState == ImportRowValidationState.Valid || r.ValidationState == ImportRowValidationState.Warning) &&
							r.Decision != ImportRowDecision.Skip))
						{
							if (!double.TryParse(row.Text("latitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
								!double.TryParse(row.Text("longitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
								continue;
							if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180 || latitude == 0 && longitude == 0)
								continue;
							rows.Add((latitude, longitude, string.Equals(row.Text("type"), "station", StringComparison.Ordinal)));
						}
					}
				}
				catch (ImportArtifactIntegrityException)
				{
					// The file behind the data set is gone (purged after success): no staged values to read.
				}
			}

			var stations = rows.Where(r => r.Station).ToList();
			return (stations.Count > 0 ? stations : rows).Select(r => (r.Latitude, r.Longitude)).ToList();
		}

		/// <summary>
		/// The middle of a set of positions: the median latitude and longitude, so a station or two in the wrong place does not
		/// pull the map away from the rest. Positions on both sides of the 180th meridian are taken the short way round.
		/// </summary>
		public static (double Latitude, double Longitude) Middle(IReadOnlyList<(double Latitude, double Longitude)> points)
		{
			if (points == null || points.Count == 0)
				throw new ArgumentException("No positions.", nameof(points));

			static double Median(List<double> values)
			{
				values.Sort();
				var middle = values.Count / 2;
				return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
			}

			var latitude = Median(points.Select(p => p.Latitude).ToList());
			var longitudes = points.Select(p => p.Longitude).ToList();
			var wrapped = longitudes.Max() - longitudes.Min() > 180;
			var longitude = Median(wrapped ? longitudes.Select(l => l < 0 ? l + 360 : l).ToList() : longitudes);
			if (longitude > 180)
				longitude -= 360;
			return (Math.Round(latitude, 6), Math.Round(longitude, 6));
		}

		/// <summary>Value-free reasons accepted values cannot be written as they stand.</summary>
		private async Task<Dictionary<string, string>> IssuesAsync(int departmentId, IReadOnlyList<ImportConfigurationChange> changes, IReadOnlyDictionary<string, string> current,
			bool permissionPreviewAvailable, IReadOnlyList<PersonnelRole> roles, IReadOnlyList<ImportArtifact> logos, CancellationToken cancellationToken)
		{
			var issues = new Dictionary<string, string>(StringComparer.Ordinal);
			var accepted = changes.Where(c => c.State == (int)ImportConfigurationChangeState.Accepted).ToDictionary(c => c.ItemKey, c => c.Value ?? string.Empty, StringComparer.Ordinal);
			string Current(string key) => current.TryGetValue(key, out var v) ? v : string.Empty;
			string Proposed(string key) => accepted.TryGetValue(key, out var v) ? v : Current(key);
			bool Changes(string key) => accepted.TryGetValue(key, out var v) && !string.Equals(v, Current(key), StringComparison.Ordinal);
			var recordsAvailable = !accepted.Keys.Any(k => ImportConfigurationCatalog.Find(k)?.Group == ImportConfigurationGroups.Records) ||
				await _targets.RecordsAvailableAsync(departmentId);

			// Record types share a sequence when the pattern leaves out {PREFIX}, or when they write the same prefix: one next number each.
			var recordsPattern = Proposed("records.pattern");
			string SequenceOf(string type) => RecordNumberFormat.UsesToken(recordsPattern, RecordNumberFormat.PrefixToken)
				? Proposed("records." + ImportConfigurationCatalog.RecordTypeSlug(type) + ".prefix")
				: string.Empty;
			var sharedNext = ImportConfigurationCatalog.NumberedRecordTypes
				.Select(t => (Key: "records." + ImportConfigurationCatalog.RecordTypeSlug(t) + ".next", Sequence: SequenceOf(t)))
				.Where(x => Changes(x.Key)).GroupBy(x => x.Sequence, StringComparer.Ordinal)
				.Where(g => g.Select(x => accepted[x.Key]).Distinct(StringComparer.Ordinal).Count() > 1)
				.SelectMany(g => g.Select(x => x.Key)).ToHashSet(StringComparer.Ordinal);

			foreach (var (key, value) in accepted)
			{
				var item = ImportConfigurationCatalog.Find(key);
				if (item == null || string.Equals(value, Current(key), StringComparison.Ordinal))
					continue;
				if (item.Group == ImportConfigurationGroups.Records && !recordsAvailable)
				{
					issues[key] = "IMP-CONFIG-UNAVAILABLE";
					continue;
				}

				switch (item.Kind)
				{
					case ImportConfigurationValueKind.Permission:
					{
						var permission = ImportConfigurationCatalog.ReadJson<ImportConfigurationPermission>(value);
						if (!permissionPreviewAvailable)
							issues[key] = "IMP-CONFIG-PREVIEW-UNAVAILABLE";
						else if (permission?.Roles?.Any(r => roles.All(x => x.PersonnelRoleId != r)) == true)
							issues[key] = "IMP-CONFIG-ROLES-MISSING";
						break;
					}

					case ImportConfigurationValueKind.Logo:
						if (value.Length > 0 && logos.All(l => !string.Equals(l.Id, value, StringComparison.OrdinalIgnoreCase) || l.PurgedOn.HasValue))
							issues[key] = "IMP-CONFIG-LOGO-MISSING";
						break;

					case ImportConfigurationValueKind.Integer when item.Group == ImportConfigurationGroups.Records && ImportConfigurationCatalog.NumberedRecordTypes.Contains(item.Target):
					{
						var prefixKey = "records." + ImportConfigurationCatalog.RecordTypeSlug(item.Target) + ".prefix";
						if (RecordNumberFormat.UsesToken(recordsPattern, RecordNumberFormat.GroupToken))
							issues[key] = "IMP-CONFIG-RECORDS-PER-GROUP";
						else if (sharedNext.Contains(key))
							issues[key] = "IMP-CONFIG-RECORDS-SHARED";
						else if (!Changes("records.pattern") && !Changes("records.year_start") && !Changes(prefixKey) &&
							ImportCanonical.ToInt(value) < ImportCanonical.ToInt(Current(key)))
							issues[key] = "IMP-CONFIG-BELOW-CURRENT";
						break;
					}

					case ImportConfigurationValueKind.Integer when item.Group == ImportConfigurationGroups.Numbering && item.Target != "calls" ||
						item.Group == ImportConfigurationGroups.Records && ImportConfigurationCatalog.RecordsDocumentKinds.Contains(item.Target):
					{
						// A document kind's width and next number belong to its own pattern; built-in numbers take neither.
						var patternKey = key.Substring(0, key.LastIndexOf('.')) + ".pattern";
						var pattern = accepted.TryGetValue(patternKey, out var p) ? p : Current(patternKey);
						if (pattern.Length == 0)
							issues[key] = "IMP-CONFIG-NEEDS-PATTERN";
						else if (key.EndsWith(".next", StringComparison.Ordinal) && !accepted.ContainsKey(patternKey) &&
							ImportCanonical.ToInt(value) < ImportCanonical.ToInt(Current(key)))
							issues[key] = "IMP-CONFIG-BELOW-CURRENT";
						break;
					}

					case ImportConfigurationValueKind.Integer when key == "numbering.calls.next":
						if (!accepted.ContainsKey("numbering.calls.pattern") && !accepted.ContainsKey("numbering.calls.year_start") &&
							ImportCanonical.ToInt(value) < ImportCanonical.ToInt(Current(key)))
							issues[key] = "IMP-CONFIG-BELOW-CURRENT";
						break;
				}
			}

			return issues;
		}

		/// <summary>The value can be accepted: its roles exist, its logo was uploaded here, a permission's impact can be previewed.</summary>
		private async Task RequireWritableAsync(int departmentId, string userId, string workspaceId, ImportConfigurationItem item, string value, CancellationToken cancellationToken)
		{
			switch (item.Kind)
			{
				case ImportConfigurationValueKind.Permission:
				{
					if (!await PermissionPreviewAvailableAsync(departmentId, userId, cancellationToken))
						throw new DepartmentImportException("IMP-CONFIG-PREVIEW-UNAVAILABLE", 409);
					var permission = ImportConfigurationCatalog.ReadJson<ImportConfigurationPermission>(value);
					var roles = await _roles.GetRolesForDepartmentAsync(departmentId) ?? new List<PersonnelRole>();
					if (permission?.Roles?.Any(r => roles.All(x => x.PersonnelRoleId != r)) == true)
						throw new DepartmentImportException("IMP-CONFIG-ROLES-MISSING");
					break;
				}

				case ImportConfigurationValueKind.Logo:
				{
					var artifact = await _artifacts.GetByIdAsync(departmentId, value, cancellationToken);
					if (artifact == null || artifact.WorkspaceId != workspaceId || artifact.Kind != (int)ImportArtifactKind.Logo || artifact.PurgedOn.HasValue)
						throw new DepartmentImportException("IMP-CONFIG-LOGO-MISSING");
					break;
				}
			}
		}

		private async Task<bool> PermissionPreviewAvailableAsync(int departmentId, string userId, CancellationToken cancellationToken)
		{
			try
			{
				return await _assistAccess.CanAccessAsync(new AdminAssistActor(departmentId, userId), false, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex);
				return false;
			}
		}

		private async Task<ImportWorkspace> RequireEditableAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId,
			CancellationToken cancellationToken)
		{
			// Staff come through the staff service, which checks the role and the Prepare or Operate grant; administrators are checked here.
			if (actorKind == ImportActorKind.Administrator ? !await _access.CanManageAsync(departmentId, actor) : actorKind != ImportActorKind.ResgridStaff ||
				string.IsNullOrWhiteSpace(actor))
				throw new DepartmentImportException("import_not_allowed", 403);
			var workspace = await RequireWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			if (DepartmentImportStateMachine.IsTerminal((ImportWorkspaceStatus)workspace.Status))
				throw new DepartmentImportException("import_workspace_closed", 409);
			// An approved digest covers the accepted values: nothing changes under a pending or running commit or rollback.
			if (await HasOpenRunAsync(departmentId, workspaceId, cancellationToken))
				throw new DepartmentImportException("import_run_open", 409);
			return workspace;
		}

		private async Task<bool> HasOpenRunAsync(int departmentId, string workspaceId, CancellationToken cancellationToken) =>
			(await _runs.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
			.Any(r => (r.Kind == (int)ImportRunKind.Commit || r.Kind == (int)ImportRunKind.Rollback) && DepartmentImportStateMachine.OpenRunStatuses.Contains((ImportRunStatus)r.Status));

		private async Task<ImportWorkspace> RequireWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken) =>
			await _workspaces.GetByIdAsync(departmentId, workspaceId, cancellationToken) ?? throw new DepartmentImportException("import_workspace_not_found", 404);

		private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
	}
}
