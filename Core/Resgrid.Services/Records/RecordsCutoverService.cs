using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Records module state and department cutover (RMS plan section 4.1). The flag selects Records vs
	/// Logs in the UI; only the append-only RmsDepartmentCutover row engages the legacy write guard.
	/// Activation migrates Permission rows (registry section 4.6) and is refused unless the flag is on,
	/// the Protected Data preflight passes (NotApplicable when the subsystem is absent), and the
	/// department is not already active. Rollback follows the three-outcome decision frame.
	/// </summary>
	public class RecordsCutoverService : IRecordsCutoverService
	{
		private const string ModuleStateCacheKey = "RmsModuleState_{0}";
		private static readonly TimeSpan ModuleStateCacheLength = TimeSpan.FromMinutes(5);

		private readonly IRmsDepartmentCutoversRepository _cutoversRepository;
		private readonly IRmsDepartmentCutoverEventsRepository _cutoverEventsRepository;
		private readonly IRmsOperationalRecordsRepository _recordsRepository;
		private readonly IRmsAccessAuditsRepository _accessAuditsRepository;
		private readonly IRmsLegacyStatsRepository _legacyStatsRepository;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IPermissionsService _permissionsService;
		private readonly IDepartmentDataProtectionService _dataProtectionService;
		private readonly IUnitOfWork _unitOfWork;
		private readonly ICacheProvider _cacheProvider;

		public RecordsCutoverService(IRmsDepartmentCutoversRepository cutoversRepository, IRmsDepartmentCutoverEventsRepository cutoverEventsRepository,
			IRmsOperationalRecordsRepository recordsRepository, IRmsAccessAuditsRepository accessAuditsRepository, IRmsLegacyStatsRepository legacyStatsRepository,
			IFeatureToggleService featureToggleService, IPermissionsService permissionsService, IDepartmentDataProtectionService dataProtectionService,
			IUnitOfWork unitOfWork, ICacheProvider cacheProvider)
		{
			_cutoversRepository = cutoversRepository;
			_cutoverEventsRepository = cutoverEventsRepository;
			_recordsRepository = recordsRepository;
			_accessAuditsRepository = accessAuditsRepository;
			_legacyStatsRepository = legacyStatsRepository;
			_featureToggleService = featureToggleService;
			_permissionsService = permissionsService;
			_dataProtectionService = dataProtectionService;
			_unitOfWork = unitOfWork;
			_cacheProvider = cacheProvider;
		}

		public async Task<RecordsModuleState> GetModuleStateAsync(int departmentId, bool bypassCache = false)
		{
			async Task<RecordsModuleState> load()
			{
				var state = new RecordsModuleState { DepartmentId = departmentId };
				state.FlagEnabled = await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.RecordsSystem, departmentId);

				var cutover = await _cutoversRepository.GetByDepartmentIdAsync(departmentId);
				if (cutover != null)
				{
					state.Activated = true;
					state.ActivatedOn = cutover.ActivatedOn;
					state.CutoverId = cutover.RmsDepartmentCutoverId;
					state.CutoverState = (RmsDepartmentCutoverState)cutover.State;
					state.LegacyWritesBlocked = cutover.IsActive;
				}

				return state;
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				var cached = await _cacheProvider.RetrieveAsync<RecordsModuleState>(string.Format(ModuleStateCacheKey, departmentId), load, ModuleStateCacheLength);

				// A blank cache payload deserializes to a non-null entity with DepartmentId 0; never trust it.
				if (cached != null && cached.DepartmentId == departmentId)
					return cached;
			}

			return await load();
		}

		public async Task<bool> IsRecordsEnabledAsync(int departmentId)
		{
			return (await GetModuleStateAsync(departmentId)).FlagEnabled;
		}

		public async Task<bool> AreLegacyWritesBlockedAsync(int departmentId)
		{
			return (await GetModuleStateAsync(departmentId)).LegacyWritesBlocked;
		}

		public async Task EnsureLegacyWriteAllowedAsync(int departmentId, string context, string userId = null)
		{
			if (!await AreLegacyWritesBlockedAsync(departmentId))
				return;

			try
			{
				await _accessAuditsRepository.InsertAsync(new RmsAccessAudit
				{
					DepartmentId = departmentId,
					Action = (int)RmsAccessAuditAction.LegacyWriteDenied,
					ActorUserId = userId,
					Purpose = context,
					Successful = false,
					OccurredOn = DateTime.UtcNow,
					OriginClient = (int)RmsOriginClient.System
				}, CancellationToken.None, true);
			}
			catch (Exception ex)
			{
				// The denial itself must never depend on the audit write succeeding.
				Logging.LogException(ex, $"Failed to audit a denied legacy write for department {departmentId} ({context}).");
			}

			throw new RecordsLegacyWriteBlockedException(departmentId, context);
		}

		public async Task<RecordsActivationPreview> GetActivationPreviewAsync(int departmentId)
		{
			var state = await GetModuleStateAsync(departmentId, true);
			var stats = await _legacyStatsRepository.GetLegacyStatsAsync(departmentId);
			var permissions = await _permissionsService.GetAllPermissionsForDepartmentAsync(departmentId) ?? new List<Permission>();

			var preview = new RecordsActivationPreview
			{
				DepartmentId = departmentId,
				FlagEnabled = state.FlagEnabled,
				AlreadyActivated = state.Activated && state.CutoverState == RmsDepartmentCutoverState.Active,
				LegacyLogCount = stats.LogCount,
				LegacyUnitLogCount = stats.UnitLogCount,
				LegacyEventTypeLogCount = stats.EventTypeLogCount,
				SourceChecksum = ComputeSourceChecksum(stats),
				PermissionMapping = BuildPermissionMapping(permissions),
				SuggestedViewGroupRecordsLockToGroup = permissions.FirstOrDefault(p => p.PermissionType == (int)PermissionTypes.ViewGroupUsers)?.LockToGroup ?? false,
				ProtectedDataPreflight = await ResolveProtectedDataPreflightAsync(departmentId)
			};

			if (!preview.FlagEnabled)
				preview.Blockers.Add("The Records.System feature flag is not enabled for this department.");

			if (preview.ProtectedDataPreflight != "NotApplicable" && preview.ProtectedDataPreflight != "Enabled")
				preview.Blockers.Add($"Protected Data is in state {preview.ProtectedDataPreflight}; activation is blocked until it settles.");

			return preview;
		}

		public async Task<RecordsActivationResult> ActivateAsync(int departmentId, string userId, string reason, bool viewGroupRecordsLockToGroup, string ipAddress = null, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return RecordsActivationResult.Failed("An acting user is required.");

			var preview = await GetActivationPreviewAsync(departmentId);
			if (preview.AlreadyActivated)
				return RecordsActivationResult.Failed("Records is already active for this department.");
			if (!preview.CanActivate)
				return RecordsActivationResult.Failed(string.Join(" ", preview.Blockers));

			var now = DateTime.UtcNow;
			var existing = await _cutoversRepository.GetByDepartmentIdAsync(departmentId);

			_unitOfWork.CreateOrGetConnection();
			try
			{
				RmsDepartmentCutover cutover;
				if (existing == null)
				{
					cutover = new RmsDepartmentCutover
					{
						DepartmentId = departmentId,
						ProtectionId = Guid.NewGuid().ToString(),
						ActivatedOn = now,
						ActivatedByUserId = userId,
						Reason = reason,
						SourceLegacyLogCount = preview.LegacyLogCount,
						SourceLegacyUnitLogCount = preview.LegacyUnitLogCount,
						SourceChecksum = preview.SourceChecksum,
						State = (int)RmsDepartmentCutoverState.Active,
						PermissionMappingJson = JsonConvert.SerializeObject(preview.PermissionMapping, new Newtonsoft.Json.Converters.StringEnumConverter()),
						CreatedOn = now,
						ModifiedOn = now,
						RowVersion = 1
					};
					cutover = await _cutoversRepository.InsertAsync(cutover, cancellationToken, true);
				}
				else
				{
					// Re-activation after a clean revert: the history event records the prior activation.
					existing.ActivatedOn = now;
					existing.ActivatedByUserId = userId;
					existing.Reason = reason;
					existing.SourceLegacyLogCount = preview.LegacyLogCount;
					existing.SourceLegacyUnitLogCount = preview.LegacyUnitLogCount;
					existing.SourceChecksum = preview.SourceChecksum;
					existing.State = (int)RmsDepartmentCutoverState.Active;
					existing.RevertedOn = null;
					existing.RevertedByUserId = null;
					existing.PermissionMappingJson = JsonConvert.SerializeObject(preview.PermissionMapping, new Newtonsoft.Json.Converters.StringEnumConverter());
					existing.ModifiedOn = now;
					existing.RowVersion += 1;
					cutover = await _cutoversRepository.UpdateAsync(existing, cancellationToken, true);
				}

				var migrated = await MigratePermissionRowsAsync(departmentId, userId, preview.PermissionMapping, viewGroupRecordsLockToGroup, cancellationToken);

				await _cutoverEventsRepository.InsertAsync(new RmsDepartmentCutoverEvent
				{
					DepartmentId = departmentId,
					RmsDepartmentCutoverId = cutover.RmsDepartmentCutoverId,
					EventType = RmsDepartmentCutoverEventTypes.Activated,
					ActorUserId = userId,
					OccurredOn = now,
					DetailJson = JsonConvert.SerializeObject(new { reason, preview.LegacyLogCount, preview.LegacyUnitLogCount, preview.LegacyEventTypeLogCount, preview.SourceChecksum, preview.ProtectedDataPreflight, viewGroupRecordsLockToGroup }),
					CreatedOn = now
				}, cancellationToken, true);

				await _cutoverEventsRepository.InsertAsync(new RmsDepartmentCutoverEvent
				{
					DepartmentId = departmentId,
					RmsDepartmentCutoverId = cutover.RmsDepartmentCutoverId,
					EventType = RmsDepartmentCutoverEventTypes.PermissionRowsMigrated,
					ActorUserId = userId,
					OccurredOn = now,
					DetailJson = JsonConvert.SerializeObject(migrated),
					CreatedOn = now
				}, cancellationToken, true);

				await _accessAuditsRepository.InsertAsync(new RmsAccessAudit
				{
					DepartmentId = departmentId,
					Action = (int)RmsAccessAuditAction.Activation,
					ActorUserId = userId,
					Purpose = "Records activation",
					IpAddress = ipAddress,
					Successful = true,
					OccurredOn = now,
					OriginClient = (int)RmsOriginClient.Web,
					DetailJson = JsonConvert.SerializeObject(new { cutover.RmsDepartmentCutoverId, reason })
				}, cancellationToken, true);

				_unitOfWork.CommitChanges();
				await InvalidateCacheAsync(departmentId);

				return new RecordsActivationResult { Success = true, Cutover = cutover };
			}
			catch (Exception ex)
			{
				_unitOfWork.DiscardChanges();
				Logging.LogException(ex, $"Records activation failed for department {departmentId}.");
				return RecordsActivationResult.Failed("Activation failed; no change was made.");
			}
		}

		public async Task<RecordsRollbackOutcome> GetRollbackOutcomeAsync(int departmentId)
		{
			var cutover = await _cutoversRepository.GetByDepartmentIdAsync(departmentId);
			if (cutover == null || !cutover.IsActive)
				return RecordsRollbackOutcome.CleanRevert;

			if (await _recordsRepository.CountFinalizedSinceAsync(departmentId, cutover.ActivatedOn) > 0)
				return RecordsRollbackOutcome.NoRollback;

			return await _recordsRepository.CountCreatedSinceAsync(departmentId, cutover.ActivatedOn) > 0
				? RecordsRollbackOutcome.DrainAndRevert
				: RecordsRollbackOutcome.CleanRevert;
		}

		public async Task<RecordsActivationResult> RevertAsync(int departmentId, string userId, string reason, string ipAddress = null, CancellationToken cancellationToken = default)
		{
			var cutover = await _cutoversRepository.GetByDepartmentIdAsync(departmentId);
			if (cutover == null || !cutover.IsActive)
				return RecordsActivationResult.Failed("Records is not active for this department.");

			var outcome = await GetRollbackOutcomeAsync(departmentId);
			if (outcome == RecordsRollbackOutcome.NoRollback)
				return RecordsActivationResult.Failed("A finalized Record exists; Records cannot be reverted. Use forward fixes (disable definitions, amend data) or restore from backup.");
			if (outcome == RecordsRollbackOutcome.DrainAndRevert)
				return RecordsActivationResult.Failed("Draft Records exist since activation; export and void them through the operator runbook before reverting.");

			var now = DateTime.UtcNow;
			_unitOfWork.CreateOrGetConnection();
			try
			{
				cutover.State = (int)RmsDepartmentCutoverState.Reverted;
				cutover.RevertedOn = now;
				cutover.RevertedByUserId = userId;
				cutover.ModifiedOn = now;
				cutover.RowVersion += 1;
				await _cutoversRepository.UpdateAsync(cutover, cancellationToken, true);

				await _cutoverEventsRepository.InsertAsync(new RmsDepartmentCutoverEvent
				{
					DepartmentId = departmentId,
					RmsDepartmentCutoverId = cutover.RmsDepartmentCutoverId,
					EventType = RmsDepartmentCutoverEventTypes.Reverted,
					ActorUserId = userId,
					OccurredOn = now,
					DetailJson = JsonConvert.SerializeObject(new { reason, outcome = outcome.ToString() }),
					CreatedOn = now
				}, cancellationToken, true);

				await _accessAuditsRepository.InsertAsync(new RmsAccessAudit
				{
					DepartmentId = departmentId,
					Action = (int)RmsAccessAuditAction.Activation,
					ActorUserId = userId,
					Purpose = "Records clean revert",
					IpAddress = ipAddress,
					Successful = true,
					OccurredOn = now,
					OriginClient = (int)RmsOriginClient.Web,
					DetailJson = JsonConvert.SerializeObject(new { cutover.RmsDepartmentCutoverId, reason })
				}, cancellationToken, true);

				_unitOfWork.CommitChanges();
				await InvalidateCacheAsync(departmentId);

				return new RecordsActivationResult { Success = true, Cutover = cutover };
			}
			catch (Exception ex)
			{
				_unitOfWork.DiscardChanges();
				Logging.LogException(ex, $"Records revert failed for department {departmentId}.");
				return RecordsActivationResult.Failed("Revert failed; no change was made.");
			}
		}

		public Task InvalidateCacheAsync(int departmentId)
		{
			_cacheProvider.Remove(string.Format(ModuleStateCacheKey, departmentId));
			return Task.CompletedTask;
		}

		/// <summary>Registry section 4.6: copy CreateLog to CreateRecord + FinalizeRecords and DeleteLog to DeleteRecord verbatim; everything else takes its no-row default.</summary>
		public static List<RecordsPermissionMappingRow> BuildPermissionMapping(IEnumerable<Permission> existing)
		{
			var rows = new List<RecordsPermissionMappingRow>();
			var byType = (existing ?? Enumerable.Empty<Permission>()).GroupBy(p => p.PermissionType).ToDictionary(g => g.Key, g => g.First());

			foreach (var mapping in RecordPermissionCatalog.ActivationRowMapping)
			{
				byType.TryGetValue((int)mapping.Key, out var source);
				foreach (var target in mapping.Value)
				{
					var descriptor = RecordPermissionCatalog.Get(target);
					rows.Add(new RecordsPermissionMappingRow
					{
						Source = mapping.Key,
						Target = target,
						SourceRowExists = source != null,
						SourceAction = source?.Action,
						SourceData = source?.Data,
						SourceLockToGroup = source?.LockToGroup ?? false,
						EffectiveAction = source != null ? (PermissionActions)source.Action : descriptor.NoRowDefault,
						Note = source != null ? "Copied verbatim from the existing row." : $"No row exists; the no-row default ({descriptor.NoRowDefault}) applies, matching today's Logs behavior."
					});
				}
			}

			foreach (var descriptor in RecordPermissionCatalog.All)
			{
				if (rows.Any(r => r.Target == descriptor.Type))
					continue;

				rows.Add(new RecordsPermissionMappingRow
				{
					Source = descriptor.Type,
					Target = descriptor.Type,
					SourceRowExists = false,
					EffectiveAction = descriptor.NoRowDefault,
					Note = descriptor.Type == PermissionTypes.ViewGroupRecords
						? "Department-wide unless the administrator locks it to group at activation."
						: $"No row at activation; no-row default ({descriptor.NoRowDefault})."
				});
			}

			return rows;
		}

		private async Task<List<object>> MigratePermissionRowsAsync(int departmentId, string userId, IEnumerable<RecordsPermissionMappingRow> mapping, bool viewGroupRecordsLockToGroup, CancellationToken cancellationToken)
		{
			var written = new List<object>();
			var current = (await _permissionsService.GetAllPermissionsForDepartmentAsync(departmentId) ?? new List<Permission>()).ToDictionary(p => p.PermissionType, p => p);

			foreach (var row in mapping.Where(r => r.SourceRowExists && r.Source != r.Target))
			{
				// Never overwrite a Records row an administrator already configured.
				if (current.ContainsKey((int)row.Target))
					continue;

				await _permissionsService.SetPermissionForDepartmentAsync(departmentId, userId, row.Target, (PermissionActions)row.SourceAction.GetValueOrDefault((int)PermissionActions.Everyone), row.SourceData, row.SourceLockToGroup, cancellationToken);
				written.Add(new { target = row.Target.ToString(), action = row.SourceAction, row.SourceData, lockToGroup = row.SourceLockToGroup });
			}

			if (viewGroupRecordsLockToGroup && !current.ContainsKey((int)PermissionTypes.ViewGroupRecords))
			{
				await _permissionsService.SetPermissionForDepartmentAsync(departmentId, userId, PermissionTypes.ViewGroupRecords, PermissionActions.Everyone, null, true, cancellationToken);
				written.Add(new { target = PermissionTypes.ViewGroupRecords.ToString(), action = (int)PermissionActions.Everyone, lockToGroup = true });
			}

			return written;
		}

		private async Task<string> ResolveProtectedDataPreflightAsync(int departmentId)
		{
			try
			{
				var policy = await _dataProtectionService.GetPolicyByDepartmentIdAsync(departmentId);
				if (policy == null)
					return "NotApplicable";

				var state = (DepartmentDataProtectionState)policy.State;
				return state == DepartmentDataProtectionState.Disabled ? "NotApplicable" : state.ToString();
			}
			catch (Exception ex)
			{
				// The subsystem being unreachable is treated as absent, per decision 18; the state is logged.
				Logging.LogException(ex, $"Protected Data preflight could not be resolved for department {departmentId}; treating as NotApplicable.");
				return "NotApplicable";
			}
		}

		private static string ComputeSourceChecksum(RmsLegacyStats stats)
		{
			return RecordSnapshotSerializer.Checksum($"{stats.LogCount}:{stats.EventTypeLogCount}:{stats.MaxLogId}:{stats.UnitLogCount}:{stats.MaxUnitLogId}");
		}
	}
}
