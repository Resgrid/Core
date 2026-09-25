using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class RetentionImpactTests
	{
		private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero); }
		private sealed class Fixture
		{
			public readonly Mock<IAdminAssistAccessService> Access = new();
			public readonly Mock<IAdminAssistRepository> Repository = new();
			public readonly Mock<IDepartmentSettingsRepository> Settings = new();
			public readonly Mock<IRetentionImpactStore> Store = new();
			public readonly Mock<IRecordsAuthorizationService> Authorization = new();
			public readonly Mock<IRecordsCutoverService> Cutover = new();
			public RetentionImpactService Service => new(Access.Object, Repository.Object, new ConfigurationCatalog(), Settings.Object, Store.Object, new Clock(), Authorization.Object, Cutover.Object);
			public Fixture()
			{
				Access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Authorization.Setup(a => a.HasPermissionAsync("admin", 7, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(true);
				Authorization.Setup(a => a.CanUserViewRecordAsync("admin", It.IsAny<string>(), 7)).ReturnsAsync(true);
				Cutover.Setup(c => c.GetModuleStateAsync(7, true)).ReturnsAsync(new RecordsModuleState { FlagEnabled = true });
				Settings.Setup(s => s.GetAllByDepartmentIdAsync(7)).ReturnsAsync(Array.Empty<DepartmentSetting>());
				Rows(Row());
			}
			public void Rows(params RetentionImpactHeader[] rows) => Store.Setup(s => s.ReadRetentionHeadersAsync(7, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(rows);
			public static RetentionImpactHeader Row(string id = "record") => new() { RecordId = id, Kind = (int)RmsRecordKind.Operational,
				DefinitionKey = RmsDefinitionKeys.Training, State = (int)RmsRecordState.Finalized, FinalizedOn = new DateTime(2010, 1, 1), ModifiedOn = new DateTime(2010, 1, 1), RowVersion = 1 };
		}
		[Test]
		public async Task Prospective_policy_keeps_historical_retention_and_never_claims_actual_purge_eligibility()
		{
			var f = new Fixture();
			var report = await f.Service.PreviewAsync(new(7, "admin"), new("0", 0));
			var expired = report.Metrics.Single(m => m.LabelKey == "Impact.RetentionExpired");
			Assert.That(expired.Before, Is.EqualTo(1)); Assert.That(expired.After, Is.EqualTo(1));
			Assert.That(report.Metrics.Single(m => m.LabelKey == "Impact.RetentionDefault").After, Is.Zero);
			Assert.That(report.Metrics.Single(m => m.LabelKey == "Impact.RetentionActualEligibility").State, Is.EqualTo(EvidenceState.Unknown));
			Assert.That(report.LimitKeys, Does.Contain("Impact.RetentionProtection"));
			f.Settings.Verify(s => s.GetAllByDepartmentIdAsync(7), Times.Exactly(2)); f.Settings.VerifyNoOtherCalls();
		}
		[Test]
		public async Task Known_holds_and_uncertain_historical_holds_are_excluded_from_remaining_candidates()
		{
			var f = new Fixture(); f.Rows(Fixture.Row("held") with { HoldOrPermanentContent = 1 }, Fixture.Row("history") with { HistoricalHoldUncertainty = 1 }, Fixture.Row("clear"));
			var report = await f.Service.PreviewAsync(new(7, "admin"), new("0", 1));
			foreach (var metric in new[] { "RetentionHeld", "RetentionHoldUnknown", "RetentionCandidates" }) Assert.That(report.Metrics.Single(m => m.LabelKey == "Impact." + metric).After, Is.EqualTo(1));
		}
		[Test]
		public async Task Open_amending_recent_restricted_and_unexpired_records_do_not_become_candidates()
		{
			var f = new Fixture(); f.Rows(Fixture.Row("draft") with { State = (int)RmsRecordState.Draft }, Fixture.Row("amending") with { AmendsRevisionId = "revision" },
				Fixture.Row("recent") with { ModifiedOn = new Clock().GetUtcNow().UtcDateTime }, Fixture.Row("unexpired") with { FinalizedOn = new DateTime(2025, 1, 1) },
				Fixture.Row("restricted") with { DefinitionKey = RmsDefinitionKeys.RestrictedClass.First() });
			var report = await f.Service.PreviewAsync(new(7, "admin"), new("0", 1));
			Assert.That(report.Metrics.Single(m => m.LabelKey == "Impact.RetentionCandidates").After, Is.Zero);
		}
		[Test]
		public async Task Unavailable_metadata_returns_unknown_and_never_reads_record_content()
		{
			var f = new Fixture(); f.Store.Setup(s => s.ReadRetentionHeadersAsync(7, It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
			var report = await f.Service.PreviewAsync(new(7, "admin"), new("0", null));
			Assert.That(report.Metrics.Single().State, Is.EqualTo(EvidenceState.Unknown));
		}
		[Test]
		public void Source_drift_restricted_access_or_disabled_module_cannot_return_counts()
		{
			var f = new Fixture(); f.Store.SetupSequence(s => s.ReadRetentionHeadersAsync(7, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Fixture.Row() }).ReturnsAsync(new[] { Fixture.Row() with { RowVersion = 2 } });
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await f.Service.PreviewAsync(new(7, "admin"), new("0", 1)));
			f = new Fixture(); f.Authorization.Setup(a => a.CanUserViewRecordAsync("admin", "record", 7)).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Service.PreviewAsync(new(7, "admin"), new("0", 1)));
			f = new Fixture(); f.Cutover.Setup(c => c.GetModuleStateAsync(7, true)).ReturnsAsync(new RecordsModuleState { FlagEnabled = false });
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Service.PreviewAsync(new(7, "admin"), new("0", 1))); f.Store.VerifyNoOtherCalls();
		}
		[Test]
		public void Retention_boundaries_handle_permanent_overflow_missing_and_leap_day_dates()
		{
			var now = new DateTime(2025, 2, 28);
			Assert.That(RecordsRetentionWindow.HasExpired(new DateTime(2024, 2, 29), 1, now), Is.True);
			Assert.That(RecordsRetentionWindow.HasExpired(new DateTime(2024, 2, 29), 1, now.AddTicks(-1)), Is.False);
			Assert.That(RecordsRetentionWindow.HasExpired(now, 0, now), Is.False);
			Assert.That(RecordsRetentionWindow.HasExpired(null, 1, now), Is.False);
			Assert.That(RecordsRetentionWindow.HasExpired(new DateTime(9999, 1, 1), int.MaxValue, now), Is.False);
		}
	}
}
