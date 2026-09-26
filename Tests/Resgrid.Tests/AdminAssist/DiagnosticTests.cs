using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class DiagnosticPolicyTests
	{
		private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
		[TestCase("paging"), TestCase("map"), TestCase("access"), TestCase("imports"), TestCase("statuses"), TestCase("coverage"), TestCase("equipment"), TestCase("integration")]
		public void Each_flow_has_a_valid_bounded_scope(string flow) => Assert.DoesNotThrow(() => DiagnosticPolicy.Validate(Request(flow), Now));
		private static DiagnosticRequest Request(string flow) => new(flow, Now.AddHours(-1), Now,
			CallId: flow == "paging" ? 1 : null, MemberId: flow is "paging" or "map" or "access" or "statuses" ? "member" : null,
			UnitId: flow == "equipment" ? 2 : null, RoleId: flow == "coverage" ? 3 : null,
			Permission: flow == "access" ? "CreateCall" : null, CapabilityId: flow is "access" or "integration" ? "calls" : null);
		[Test]
		public void Invalid_windows_targets_and_actions_are_rejected()
		{
			var r = Request("paging");
			foreach (var invalid in new[] { r with { MemberId = null }, r with { CallId = 0 }, r with { FromUtc = Now.AddDays(-8) }, r with { UntilUtc = Now.AddHours(1) },
				r with { FromUtc = DateTime.SpecifyKind(r.FromUtc, DateTimeKind.Unspecified) }, r with { Permission = "DeleteDepartment" }, r with { Flow = "execute_sql" }, r with { MemberId = "bad\nvalue" } })
				Assert.Throws<ArgumentException>(() => DiagnosticPolicy.Validate(invalid, Now));
		}
		private static DispatchTraceObservation Observation(string attempt, int sequence, DispatchTraceStage stage, string member = null) =>
			new(Guid.NewGuid().ToString("D"), 7, 12, 1, attempt, Now.AddSeconds(sequence), stage, DispatchTraceChannel.Routing,
				DispatchTraceReason.None, member, null, null, Now, sequence, 0, DispatchRecipientResolver.Version);
		[Test]
		public void Duplicate_and_out_of_order_observations_do_not_change_sequence_coverage()
		{
			var id = Guid.NewGuid().ToString("D");
			var start = Observation(id, 1, DispatchTraceStage.BroadcastStarted); var selected = Observation(id, 2, DispatchTraceStage.Selected, "target"); var end = Observation(id, 3, DispatchTraceStage.BroadcastCompleted);
			var result = DiagnosticPolicy.Trace(new[] { end, selected, start, selected }, "target", false).Single();
			Assert.That(result.Complete, Is.True); Assert.That(result.Observed, Is.EqualTo(3)); Assert.That(result.Events, Has.Count.EqualTo(1));
		}
		[TestCase(true, 0, 0), TestCase(false, 1, 0), TestCase(false, 0, 1)]
		public void Missing_truncated_and_dropped_events_never_establish_complete_history(bool truncated, int gap, int drop)
		{
			var id = Guid.NewGuid().ToString("D");
			var rows = new[] { Observation(id, 1, DispatchTraceStage.BroadcastStarted), Observation(id, 2 + gap, DispatchTraceStage.BroadcastCompleted) with { PriorDropped = drop } };
			Assert.That(DiagnosticPolicy.Trace(rows, "target", truncated).Single().Complete, Is.False);
		}
		[Test]
		public void Unknown_resolver_versions_are_neither_exported_as_free_text_nor_used_to_prove_exclusion()
		{
			var id = Guid.NewGuid().ToString("D");
			var rows = new[] { Observation(id, 1, DispatchTraceStage.BroadcastStarted), Observation(id, 2, DispatchTraceStage.BroadcastCompleted) }.Select(o => o with { ResolverVersion = "untrusted-private-text" });
			var trace = DiagnosticPolicy.Trace(rows, "target", false).Single();
			Assert.That(trace.Complete, Is.False); Assert.That(trace.ResolverVersion, Is.EqualTo("unknown"));
		}
		[Test]
		public void Trace_excludes_other_members_provider_identifiers_and_route_sources()
		{
			var id = Guid.NewGuid().ToString("D");
			var rows = new[] { Observation(id, 1, DispatchTraceStage.Selected, "target") with { SourceId = "private-group", ProviderMessageId = "private-provider-id" }, Observation(id, 2, DispatchTraceStage.Selected, "other-person") };
			var text = JsonSerializer.Serialize(DiagnosticPolicy.Trace(rows, "target", false));
			Assert.That(text, Does.Not.Contain("private").And.Not.Contain("target").And.Not.Contain("other-person"));
			Assert.That(text, Does.Not.Contain("Delivered").And.Not.Contain("Acknowledged"));
		}
		[Test]
		public void Support_preview_omits_restricted_values_and_navigation_and_fingerprints_changed_facts()
		{
			var visible = new DiagnosticCheck("metric", "PossibleCause", "Current", "Diagnostic.Check.metric", "Source", "1", Now, 3, "/User/Profile/View");
			var hidden = visible with { Id = "hidden-id", Basis = "Restricted", Value = 900 };
			var report = new DiagnosticReport("run", 1, "imports", Now, Now, "catalog", "1", "PossibleCause", new[] { visible, hidden }, Array.Empty<DiagnosticChange>(), Array.Empty<DiagnosticTraceAttempt>(), false, false);
			var original = DiagnosticPolicy.Bundle(report);
			Assert.That(JsonSerializer.Serialize(original), Does.Not.Contain("hidden-id").And.Not.Contain("/User/Profile/View"));
			Assert.That(DiagnosticPolicy.Bundle(report with { CheckedOnUtc = Now.AddSeconds(2), Checks = new[] { visible with { AsOfUtc = Now.AddSeconds(2) }, hidden } }).PreviewDigest, Is.EqualTo(original.PreviewDigest));
			Assert.That(DiagnosticPolicy.Bundle(report with { Checks = new[] { visible with { Value = 4 } } }).PreviewDigest, Is.Not.EqualTo(original.PreviewDigest));
		}
		[TestCase("ConfirmedCause", "PossibleCause", "ConfirmedCause")]
		[TestCase("NoIssueFound", "InsufficientEvidence", "InsufficientEvidence")]
		[TestCase("PossibleCause", "InsufficientEvidence", "PossibleCause")]
		public void Summary_preserves_cause_and_uncertainty_categories(string first, string second, string expected) =>
			Assert.That(DiagnosticPolicy.Outcome(new[] { new DiagnosticCheck("a", first, "Current", "a", "s", "1", Now), new DiagnosticCheck("b", second, "Current", "b", "s", "1", Now) }), Is.EqualTo(expected));
	}

	[TestFixture, NonParallelizable]
	public class DiagnosticServiceTests
	{
		private bool _enabled;
		[SetUp] public void SetUp() { _enabled = AdminAssistConfig.TroubleshootingEnabled; AdminAssistConfig.TroubleshootingEnabled = true; }
		[TearDown] public void TearDown() => AdminAssistConfig.TroubleshootingEnabled = _enabled;
		private sealed class Fixture
		{
			public readonly AdminAssistActor Actor = new(7, "admin");
			public readonly Mock<IAdminAssistDiagnosticStore> Store = new();
			public readonly Mock<IAdminAssistDiagnosticSource> Source = new();
			public readonly Mock<IAdminAssistDiagnosticProtection> Protection = new();
			public readonly Mock<IAdminAssistAccessService> Access = new();
			public readonly Mock<IAdminAssistRepository> Revisions = new();
			public readonly Mock<IAuditService> Audit = new();
			public readonly AdminAssistDiagnosticService Service;
			public AdminAssistDiagnosticRun Row;
			public readonly DiagnosticRequest Request = new("imports", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
			public Fixture()
			{
				Access.Setup(s => s.CanAccessAsync(Actor, false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Store.Setup(s => s.AcquireDiagnosticLeaseAsync(Actor, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Store.Setup(s => s.ReadDiagnosticChangesAsync(7, It.IsAny<DateTime>(), It.IsAny<DateTime>(), 100, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DiagnosticChange>());
				Source.Setup(s => s.ReadAsync(Actor, It.IsAny<DiagnosticRequest>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DiagnosticSourceResult(new[] { new DiagnosticCheck("imports", "InsufficientEvidence", "Current", "test", "test", "1", DateTime.UtcNow) }, Array.Empty<DiagnosticTraceAttempt>()));
				Protection.Setup(s => s.ProtectAsync(Actor, It.IsAny<AdminAssistDiagnosticRun>(), It.IsAny<DiagnosticRequest>(), It.IsAny<CancellationToken>())).Callback<AdminAssistActor, AdminAssistDiagnosticRun, DiagnosticRequest, CancellationToken>((_, row, _, _) => row.Content = "enc2:test").Returns(Task.CompletedTask);
				Protection.Setup(s => s.ReadAsync(Actor, It.IsAny<AdminAssistDiagnosticRun>(), It.IsAny<CancellationToken>())).ReturnsAsync(Request);
				Store.Setup(s => s.SaveDiagnosticAsync(Actor, It.IsAny<AdminAssistDiagnosticRun>(), It.IsAny<CancellationToken>())).Callback<AdminAssistActor, AdminAssistDiagnosticRun, CancellationToken>((_, row, _) => Row = row).Returns(Task.CompletedTask);
				Store.Setup(s => s.ReadDiagnosticAsync(Actor, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Row);
				Service = new(Access.Object, Store.Object, Source.Object, Revisions.Object, new ConfigurationCatalog(), Protection.Object, Audit.Object, TimeProvider.System);
			}
		}
		[Test]
		public void Default_off_host_gate_prevents_all_evidence_storage_and_protection_calls()
		{
			AdminAssistConfig.TroubleshootingEnabled = false; var f = new Fixture();
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.RunAsync(f.Actor, f.Request, CancellationToken.None));
			f.Store.VerifyNoOtherCalls(); f.Source.VerifyNoOtherCalls(); f.Protection.VerifyNoOtherCalls();
		}
		[Test]
		public async Task Run_encrypts_request_and_audits_without_storing_source_results()
		{
			var f = new Fixture(); var result = await f.Service.RunAsync(f.Actor, f.Request, CancellationToken.None);
			Assert.That(f.Row.Content, Is.EqualTo("enc2:test")); Assert.That(result.RunId, Is.EqualTo(f.Row.Id));
			f.Audit.Verify(a => a.SaveAuditLogAsync(It.Is<AuditLog>(a => a.Message == "Run" && a.UserId == "admin" && !a.Data.Contains("imports")), It.IsAny<CancellationToken>()), Times.Once);
			f.Store.Verify(s => s.ReleaseDiagnosticLeaseAsync(f.Actor, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}
		[Test]
		public async Task Owner_or_department_switch_cannot_replay_a_retained_run()
		{
			var f = new Fixture(); var run = await f.Service.RunAsync(f.Actor, f.Request, CancellationToken.None);
			f.Row.UserId = "other";
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.ReadAsync(f.Actor, new(run.RunId), CancellationToken.None));
			f.Protection.Verify(p => p.ReadAsync(f.Actor, It.IsAny<AdminAssistDiagnosticRun>(), It.IsAny<CancellationToken>()), Times.Never);
		}
		[Test]
		public async Task A_retained_run_stays_readable_after_its_window_ages_past_ninety_days()
		{
			var f = new Fixture(); var run = await f.Service.RunAsync(f.Actor, f.Request, CancellationToken.None);
			// Created two days ago with the oldest window allowed then: valid at creation, 91 days old today, still retained.
			f.Row.CreatedOnUtc = DateTime.UtcNow.AddDays(-2);
			var from = f.Row.CreatedOnUtc.AddDays(-89);
			f.Protection.Setup(s => s.ReadAsync(f.Actor, It.IsAny<AdminAssistDiagnosticRun>(), It.IsAny<CancellationToken>())).ReturnsAsync(f.Request with { FromUtc = from, UntilUtc = from.AddHours(1) });
			var report = await f.Service.ReadAsync(f.Actor, new(run.RunId), CancellationToken.None);
			Assert.That(report.RunId, Is.EqualTo(run.RunId));
		}
		[Test]
		public async Task Export_requires_a_preview_of_current_evidence_and_audits_the_download()
		{
			var f = new Fixture(); var run = await f.Service.RunAsync(f.Actor, f.Request, CancellationToken.None);
			var preview = await f.Service.PreviewSupportAsync(f.Actor, new(run.RunId), CancellationToken.None);
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(() => f.Service.ExportSupportAsync(f.Actor, new(run.RunId, 1, new string('a', 64)), CancellationToken.None));
			var exported = await f.Service.ExportSupportAsync(f.Actor, new(run.RunId, 1, preview.PreviewDigest), CancellationToken.None);
			Assert.That(exported.PreviewDigest, Is.EqualTo(preview.PreviewDigest));
			f.Audit.Verify(a => a.SaveAuditLogAsync(It.Is<AuditLog>(a => a.Message == "SupportExport"), It.IsAny<CancellationToken>()), Times.Once);
		}
		[Test]
		public async Task Permission_revoked_after_preview_blocks_export()
		{
			var f = new Fixture(); var run = await f.Service.RunAsync(f.Actor, f.Request, CancellationToken.None);
			var preview = await f.Service.PreviewSupportAsync(f.Actor, new(run.RunId), CancellationToken.None);
			f.Access.Setup(a => a.CanAccessAsync(f.Actor, false, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.ExportSupportAsync(f.Actor, new(run.RunId, 1, preview.PreviewDigest), CancellationToken.None));
		}
		[Test]
		public async Task Configuration_timeline_requires_owning_access_and_projects_only_reviewed_scalars()
		{
			var f = new Fixture(); var now = DateTime.UtcNow;
			var changes = new[] { new DiagnosticChange(Guid.NewGuid().ToString("D"), now, "setting.EnableTextToCall", 1, "{\"Setting\":false,\"private\":\"patient-content\"}", "{\"Setting\":true}", "private-free-text") };
			f.Store.Setup(s => s.ReadDiagnosticChangesAsync(7, It.IsAny<DateTime>(), It.IsAny<DateTime>(), 100, It.IsAny<CancellationToken>())).ReturnsAsync(changes);
			Assert.That((await f.Service.RunAsync(f.Actor, f.Request, CancellationToken.None)).Changes, Is.Empty);
			f.Access.Setup(a => a.GetCapabilityAsync(f.Actor, "text-intake", It.IsAny<CancellationToken>())).ReturnsAsync(new CapabilityAccess("text-intake", EvidenceState.Known, Array.Empty<string>(), true, null, now));
			var report = await f.Service.RunAsync(f.Actor, f.Request, CancellationToken.None);
			Assert.That(report.Changes.Single().BeforeCode, Is.EqualTo("false"));
			Assert.That(report.Changes.Single().AfterCode, Is.EqualTo("true"));
			Assert.That(JsonSerializer.Serialize(DiagnosticPolicy.Bundle(report)), Does.Not.Contain("patient-content").And.Not.Contain("private-free-text"));
		}
		[Test]
		public void Cancellation_releases_admission_even_when_a_source_does_not_observe_the_token()
		{
			var f = new Fixture(); using var cancelled = new CancellationTokenSource();
			var pending = new TaskCompletionSource<DiagnosticSourceResult>();
			f.Source.Setup(s => s.ReadAsync(f.Actor, It.IsAny<DiagnosticRequest>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.Callback(() => cancelled.Cancel()).Returns(pending.Task);
			Assert.CatchAsync<OperationCanceledException>(() => f.Service.RunAsync(f.Actor, f.Request, cancelled.Token));
			Assert.That(f.Row, Is.Null);
			f.Store.Verify(s => s.ReleaseDiagnosticLeaseAsync(f.Actor, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			pending.SetCanceled();
		}
		[Test]
		public void Host_revocation_during_source_read_prevents_storage()
		{
			var f = new Fixture();
			f.Source.Setup(s => s.ReadAsync(f.Actor, It.IsAny<DiagnosticRequest>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.Callback(() => AdminAssistConfig.TroubleshootingEnabled = false)
				.ReturnsAsync(new DiagnosticSourceResult(Array.Empty<DiagnosticCheck>(), Array.Empty<DiagnosticTraceAttempt>()));
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.RunAsync(f.Actor, f.Request, CancellationToken.None));
			Assert.That(f.Row, Is.Null);
		}
		[Test]
		public void Changed_configuration_and_failed_audit_do_not_persist_a_successful_run()
		{
			var f = new Fixture(); f.Revisions.SetupSequence(r => r.GetConfigurationRevisionAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(1).ReturnsAsync(2);
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(() => f.Service.RunAsync(f.Actor, f.Request, CancellationToken.None)); Assert.That(f.Row, Is.Null);
			f = new Fixture(); f.Audit.Setup(a => a.SaveAuditLogAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
			Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RunAsync(f.Actor, f.Request, CancellationToken.None)); Assert.That(f.Row, Is.Null);
		}
	}
}
