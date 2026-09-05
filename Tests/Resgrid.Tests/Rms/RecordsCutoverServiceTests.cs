using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Feature-flag / cutover / legacy write-denial / permission-migration / rollback tests
	/// (RMS plan sections 4.1 and 7, registry section 4.6).
	/// </summary>
	[TestFixture]
	public class RecordsCutoverServiceTests
	{
		private const int Dept = 42;
		private FakeRmsStore _store;
		private Mock<IFeatureToggleService> _flags;
		private Mock<IPermissionsService> _permissions;
		private Mock<IDepartmentDataProtectionService> _adp;
		private Mock<IRmsLegacyStatsRepository> _legacy;
		private List<Permission> _rows;
		private RecordsCutoverService _service;

		[SetUp]
		public void SetUp()
		{
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = false;
			_store = new FakeRmsStore();
			_flags = new Mock<IFeatureToggleService>();
			_permissions = new Mock<IPermissionsService>();
			_adp = new Mock<IDepartmentDataProtectionService>();
			_legacy = new Mock<IRmsLegacyStatsRepository>();
			_rows = new List<Permission>();

			_legacy.Setup(l => l.GetLegacyStatsAsync(Dept)).ReturnsAsync(new RmsLegacyStats { LogCount = 120, EventTypeLogCount = 2, UnitLogCount = 15, MaxLogId = 900, MaxUnitLogId = 77 });
			_permissions.Setup(p => p.GetAllPermissionsForDepartmentAsync(Dept)).ReturnsAsync(() => _rows.ToList());
			_permissions.Setup(p => p.SetPermissionForDepartmentAsync(Dept, It.IsAny<string>(), It.IsAny<PermissionTypes>(), It.IsAny<PermissionActions>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string u, PermissionTypes t, PermissionActions a, string data, bool l, CancellationToken c) =>
				{
					var row = new Permission { DepartmentId = d, PermissionType = (int)t, Action = (int)a, Data = data, LockToGroup = l };
					_rows.Add(row);
					return row;
				});

			_service = new RecordsCutoverService(_store.CutoversRepo.Object, _store.CutoverEventsRepo.Object, _store.RecordsRepo.Object, _store.AuditsRepo.Object,
				_legacy.Object, _flags.Object, _permissions.Object, _adp.Object, _store.UnitOfWork.Object, new Mock<ICacheProvider>().Object);
		}

		private void FlagOn(bool on = true) => _flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.RecordsSystem, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(on);

		[Test]
		public async Task Flag_off_and_no_cutover_leaves_logs_writable_and_records_unusable()
		{
			FlagOn(false);
			var state = await _service.GetModuleStateAsync(Dept);

			state.FlagEnabled.Should().BeFalse();
			state.Activated.Should().BeFalse();
			state.LegacyWritesBlocked.Should().BeFalse();
			state.RecordsUsable.Should().BeFalse();
			await _service.Invoking(s => s.EnsureLegacyWriteAllowedAsync(Dept, "test")).Should().NotThrowAsync();
		}

		[Test]
		public async Task Flag_on_without_activation_does_not_block_legacy_writes()
		{
			FlagOn();
			var state = await _service.GetModuleStateAsync(Dept);

			state.FlagEnabled.Should().BeTrue();
			state.LegacyWritesBlocked.Should().BeFalse("only the cutover row engages the guard, never the flag alone");
			state.RecordsUsable.Should().BeFalse();
		}

		[Test]
		public async Task Active_cutover_blocks_legacy_writes_with_the_stable_message_and_audits_the_attempt()
		{
			FlagOn();
			_store.SeedActiveCutover(Dept);

			var state = await _service.GetModuleStateAsync(Dept);
			state.RecordsUsable.Should().BeTrue();
			state.LegacyWritesBlocked.Should().BeTrue();

			var act = () => _service.EnsureLegacyWriteAllowedAsync(Dept, "WorkLogsService.SaveLogAsync", "u1");
			(await act.Should().ThrowAsync<RecordsLegacyWriteBlockedException>()).Which.Message.Should().Be(RecordsLegacyWriteBlockedException.StableMessage);

			_store.Audits.Should().ContainSingle(a => a.Action == (int)RmsAccessAuditAction.LegacyWriteDenied && a.Purpose == "WorkLogsService.SaveLogAsync" && a.ActorUserId == "u1" && !a.Successful);
		}

		[Test]
		public async Task Reverted_cutover_reopens_legacy_writes()
		{
			FlagOn();
			_store.SeedActiveCutover(Dept);
			_store.Cutovers[0].State = (int)RmsDepartmentCutoverState.Reverted;

			var state = await _service.GetModuleStateAsync(Dept);
			state.Activated.Should().BeTrue("the activation fact is retained in history");
			state.LegacyWritesBlocked.Should().BeFalse();
			state.RecordsUsable.Should().BeFalse();
		}

		[Test]
		public async Task Preview_reports_legacy_counts_permission_table_and_blocks_when_flag_is_off()
		{
			FlagOn(false);
			_rows.Add(new Permission { PermissionType = (int)PermissionTypes.CreateLog, Action = (int)PermissionActions.DepartmentAdminsAndSelectRoles, Data = "3,4", LockToGroup = true });
			_rows.Add(new Permission { PermissionType = (int)PermissionTypes.ViewGroupUsers, Action = (int)PermissionActions.Everyone, LockToGroup = true });

			var preview = await _service.GetActivationPreviewAsync(Dept);

			preview.LegacyLogCount.Should().Be(120);
			preview.LegacyUnitLogCount.Should().Be(15);
			preview.LegacyEventTypeLogCount.Should().Be(2);
			preview.SourceChecksum.Should().HaveLength(64);
			preview.ProtectedDataPreflight.Should().Be("NotApplicable", "no policy row means the subsystem is absent");
			preview.SuggestedViewGroupRecordsLockToGroup.Should().BeTrue("read from ViewGroupUsers as a suggestion");
			preview.CanActivate.Should().BeFalse();
			preview.Blockers.Should().ContainSingle(b => b.Contains("feature flag"));

			var create = preview.PermissionMapping.Single(r => r.Target == PermissionTypes.CreateRecord);
			create.SourceRowExists.Should().BeTrue();
			create.EffectiveAction.Should().Be(PermissionActions.DepartmentAdminsAndSelectRoles);
			create.SourceData.Should().Be("3,4");
			create.SourceLockToGroup.Should().BeTrue();
			preview.PermissionMapping.Single(r => r.Target == PermissionTypes.FinalizeRecords).Source.Should().Be(PermissionTypes.CreateLog);
			preview.PermissionMapping.Single(r => r.Target == PermissionTypes.DeleteRecord).EffectiveAction.Should().Be(PermissionActions.Everyone, "no DeleteLog row: everyone, matching AddDeleteLogClaims");
			preview.PermissionMapping.Single(r => r.Target == PermissionTypes.ReviewRecords).EffectiveAction.Should().Be(PermissionActions.DepartmentAndGroupAdmins);
			preview.PermissionMapping.Select(r => r.Target).Distinct().Should().HaveCount(18);
		}

		[Test]
		public async Task Preview_blocks_protected_data_until_RMS_protection_is_supported()
		{
			FlagOn();
			_adp.Setup(a => a.GetPolicyByDepartmentIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = Dept, State = (int)DepartmentDataProtectionState.Encrypting });
			(await _service.GetActivationPreviewAsync(Dept)).Blockers.Should().ContainSingle(b => b.Contains("Encrypting"));

			_adp.Setup(a => a.GetPolicyByDepartmentIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = Dept, State = (int)DepartmentDataProtectionState.Enabled });
			var preview = await _service.GetActivationPreviewAsync(Dept);
			preview.ProtectedDataPreflight.Should().Be("Enabled");
			preview.CanActivate.Should().BeFalse();
			_adp.Verify(a => a.GetPolicyByDepartmentIdAsync(Dept, true), Times.Exactly(2));
		}

		[Test]
		public async Task Unavailable_protection_policy_blocks_activation_without_writing_cutover_or_permissions()
		{
			FlagOn();
			_adp.Setup(a => a.GetPolicyByDepartmentIdAsync(Dept, true)).ThrowsAsync(new InvalidOperationException("policy unavailable"));
			var preview = await _service.GetActivationPreviewAsync(Dept);
			preview.ProtectedDataPreflight.Should().Be("Unknown");
			preview.CanActivate.Should().BeFalse();
			var result = await _service.ActivateAsync(Dept, "admin", "go live", true);
			result.Success.Should().BeFalse();
			_store.Cutovers.Should().BeEmpty();
			_store.CutoverEvents.Should().BeEmpty();
			_rows.Should().BeEmpty();
			_store.Commits.Should().Be(0);
		}

		[Test]
		public async Task A_disabled_protection_policy_allows_activation()
		{
			FlagOn();
			_adp.Setup(a => a.GetPolicyByDepartmentIdAsync(Dept, true)).ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = Dept, State = (int)DepartmentDataProtectionState.Disabled });
			var preview = await _service.GetActivationPreviewAsync(Dept);
			preview.ProtectedDataPreflight.Should().Be("NotApplicable");
			preview.CanActivate.Should().BeTrue();
		}

		[Test]
		public async Task Activation_writes_the_cutover_row_migrates_permission_rows_and_audits_in_one_transaction()
		{
			FlagOn();
			_rows.Add(new Permission { PermissionType = (int)PermissionTypes.CreateLog, Action = (int)PermissionActions.DepartmentAndGroupAdmins, Data = null, LockToGroup = true });
			_rows.Add(new Permission { PermissionType = (int)PermissionTypes.DeleteLog, Action = (int)PermissionActions.DepartmentAdminsOnly });
			// An administrator already configured DeleteRecord; activation must not overwrite it.
			_rows.Add(new Permission { PermissionType = (int)PermissionTypes.DeleteRecord, Action = (int)PermissionActions.Everyone });

			var result = await _service.ActivateAsync(Dept, "admin", "go live", viewGroupRecordsLockToGroup: true);

			result.Success.Should().BeTrue(result.Error);
			var cutover = _store.Cutovers.Single();
			cutover.State.Should().Be((int)RmsDepartmentCutoverState.Active);
			cutover.ActivatedByUserId.Should().Be("admin");
			cutover.SourceLegacyLogCount.Should().Be(120);
			cutover.SourceLegacyUnitLogCount.Should().Be(15);
			cutover.PermissionMappingJson.Should().Contain("CreateRecord");
			Guid.TryParse(cutover.ProtectionId, out _).Should().BeTrue();

			_rows.Where(r => r.PermissionType == (int)PermissionTypes.CreateRecord).Should().ContainSingle(r => r.Action == (int)PermissionActions.DepartmentAndGroupAdmins && r.LockToGroup);
			_rows.Where(r => r.PermissionType == (int)PermissionTypes.FinalizeRecords).Should().ContainSingle(r => r.Action == (int)PermissionActions.DepartmentAndGroupAdmins && r.LockToGroup);
			_rows.Where(r => r.PermissionType == (int)PermissionTypes.DeleteRecord).Should().ContainSingle(r => r.Action == (int)PermissionActions.Everyone, "the pre-configured row is left alone");
			_rows.Where(r => r.PermissionType == (int)PermissionTypes.ViewGroupRecords).Should().ContainSingle(r => r.LockToGroup && r.Action == (int)PermissionActions.Everyone);
			_rows.Should().NotContain(r => r.PermissionType == (int)PermissionTypes.ReviewRecords, "no-row defaults are not materialized");

			_store.CutoverEvents.Select(e => e.EventType).Should().Equal(RmsDepartmentCutoverEventTypes.Activated, RmsDepartmentCutoverEventTypes.PermissionRowsMigrated);
			_store.Audits.Should().ContainSingle(a => a.Action == (int)RmsAccessAuditAction.Activation);
			_store.Commits.Should().Be(1);
			_store.Discards.Should().Be(0);

			(await _service.GetModuleStateAsync(Dept)).LegacyWritesBlocked.Should().BeTrue();
		}

		[Test]
		public async Task Activation_with_no_permission_rows_writes_none_so_the_fall_through_keeps_behaving()
		{
			FlagOn();
			var result = await _service.ActivateAsync(Dept, "admin", null, viewGroupRecordsLockToGroup: false);

			result.Success.Should().BeTrue(result.Error);
			_rows.Should().BeEmpty();
		}

		[Test]
		public async Task Activation_is_refused_when_flag_off_or_already_active()
		{
			FlagOn(false);
			(await _service.ActivateAsync(Dept, "admin", null, false)).Success.Should().BeFalse();

			FlagOn();
			_store.SeedActiveCutover(Dept);
			var again = await _service.ActivateAsync(Dept, "admin", null, false);
			again.Success.Should().BeFalse();
			again.Error.Should().Contain("already active");
			_store.Cutovers.Should().HaveCount(1);
		}

		[Test]
		public async Task Rollback_decision_frame_follows_what_records_data_exists()
		{
			FlagOn();
			_store.SeedActiveCutover(Dept, DateTime.UtcNow.AddHours(-2));
			(await _service.GetRollbackOutcomeAsync(Dept)).Should().Be(RecordsRollbackOutcome.CleanRevert);

			_store.Records.Add(new RmsOperationalRecord { RmsOperationalRecordId = "d1", DepartmentId = Dept, CreatedOn = DateTime.UtcNow, RevisionCount = 0 });
			(await _service.GetRollbackOutcomeAsync(Dept)).Should().Be(RecordsRollbackOutcome.DrainAndRevert);
			(await _service.RevertAsync(Dept, "admin", "oops")).Success.Should().BeFalse("drafts must be drained through the runbook first");

			_store.Records.Add(new RmsOperationalRecord { RmsOperationalRecordId = "f1", DepartmentId = Dept, CreatedOn = DateTime.UtcNow, RevisionCount = 1 });
			(await _service.GetRollbackOutcomeAsync(Dept)).Should().Be(RecordsRollbackOutcome.NoRollback);
			var refused = await _service.RevertAsync(Dept, "admin", "oops");
			refused.Success.Should().BeFalse();
			refused.Error.Should().Contain("finalized");
			_store.Cutovers.Single().State.Should().Be((int)RmsDepartmentCutoverState.Active);
		}

		[Test]
		public async Task Clean_revert_marks_the_row_reverted_keeps_activation_history_and_reopens_logs()
		{
			FlagOn();
			_store.SeedActiveCutover(Dept, DateTime.UtcNow.AddHours(-2));
			var activatedOn = _store.Cutovers[0].ActivatedOn;

			var result = await _service.RevertAsync(Dept, "admin", "pilot ended");

			result.Success.Should().BeTrue(result.Error);
			var row = _store.Cutovers.Single();
			row.State.Should().Be((int)RmsDepartmentCutoverState.Reverted);
			row.ActivatedOn.Should().Be(activatedOn, "ActivatedOn is retained in history");
			row.RevertedByUserId.Should().Be("admin");
			_store.CutoverEvents.Should().ContainSingle(e => e.EventType == RmsDepartmentCutoverEventTypes.Reverted);
			(await _service.AreLegacyWritesBlockedAsync(Dept)).Should().BeFalse();

			// Re-activation after a clean revert reuses the row and re-engages the guard.
			(await _service.ActivateAsync(Dept, "admin", "again", false)).Success.Should().BeTrue();
			_store.Cutovers.Should().HaveCount(1);
			(await _service.AreLegacyWritesBlockedAsync(Dept)).Should().BeTrue();
		}
	}
}
