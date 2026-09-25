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
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class ChangePlanPolicyTests
	{
		private readonly ConfigurationCatalog _catalog = new();
		private static ConfigurationChange Change(string id, string setting, params string[] dependencies) => new(id, "setting." + setting, true, null, dependencies);
		private static PlanDraftRequest Draft(params ConfigurationChange[] changes) => new("Goal", ChangeSet: new(ChangePlanPolicy.Version, "manual", null, changes));
		[Test]
		public void Prerequisites_are_sorted_and_cycles_duplicate_settings_and_unknown_references_are_rejected()
		{
			var a = Change("a", "DispatchShiftInsteadOfGroup"); var b = Change("b", "AutoSetStatusForShiftDispatchPersonnel", "a");
			Assert.That(ChangePlanPolicy.Normalize(Draft(b, a), _catalog).ChangeSet.Changes.Select(c => c.Id), Is.EqualTo(new[] { "a", "b" }));
			Assert.Throws<ArgumentException>(() => ChangePlanPolicy.Normalize(Draft(a with { Prerequisites = new[] { "b" } }, b), _catalog));
			Assert.Throws<ArgumentException>(() => ChangePlanPolicy.Normalize(Draft(a, a with { Id = "b" }), _catalog));
			Assert.Throws<ArgumentException>(() => ChangePlanPolicy.Normalize(Draft(b), _catalog));
		}
		[Test]
		public void Secret_values_ranges_code_and_unbounded_proposals_are_rejected()
		{
			var a = Change("a", "EnableTextCommand");
			foreach (var invalid in new[] { a with { CatalogId = "setting.MappingMapboxAccessToken" }, a with { Id = "<script>" }, a with { Number = 1 }, a with { Boolean = null }, a with { CatalogId = "setting.Require2FAForAdmins", Boolean = null, Number = 3 } })
				Assert.Throws<ArgumentException>(() => ChangePlanPolicy.Normalize(Draft(invalid), _catalog));
			Assert.Throws<ArgumentException>(() => ChangePlanPolicy.Normalize(Draft(Enumerable.Repeat(a, 9).ToArray()), _catalog));
			Assert.Throws<ArgumentException>(() => ChangePlanPolicy.Normalize(Draft(a) with { Goal = new string('x', 1001) }, _catalog));
		}
		[Test]
		public void Every_reviewed_template_uses_the_shared_validated_contract()
		{
			var templates = ChangePlanPolicy.Templates(_catalog);
			Assert.That(templates.Count, Is.GreaterThan(3));
			foreach (var template in templates) Assert.DoesNotThrow(() => ChangePlanPolicy.Normalize(new("Goal", template.Id), _catalog), template.Id);
		}
		[Test]
		public void Intermediate_and_final_state_are_composed_in_memory_including_irreversible_side_effects()
		{
			var now = DateTime.UtcNow;
			var snapshot = new ConfigurationSnapshot(7, "admin", "1", now, true, new Dictionary<string, ConfigurationEvidence>
			{
				["DispatchShiftInsteadOfGroup"] = new("DispatchShiftInsteadOfGroup", EvidenceState.Known, "source", "1", now, Boolean: false),
				["AutoSetStatusForShiftDispatchPersonnel"] = new("AutoSetStatusForShiftDispatchPersonnel", EvidenceState.Known, "source", "1", now, Boolean: false),
				["mapTokenPresent"] = new("mapTokenPresent", EvidenceState.Known, "source", "1", now, Boolean: true)
			});
			var first = ChangePlanPolicy.Overlay(snapshot, Change("status", "AutoSetStatusForShiftDispatchPersonnel"), now);
			Assert.That(ChangePlanPolicy.RuleDelta(_catalog, snapshot, first, now).Any(r => r.RuleId == "shift-auto-without-dispatch" && r.After == RuleResult.Fail), Is.True);
			var composed = ChangePlanPolicy.Overlay(first, Change("routing", "DispatchShiftInsteadOfGroup"), now);
			Assert.That(ChangePlanPolicy.RuleDelta(_catalog, first, composed, now).Any(r => r.RuleId == "shift-auto-without-dispatch" && r.Before == RuleResult.Fail && r.After == RuleResult.Pass), Is.True);
			var map = ChangePlanPolicy.Overlay(snapshot, Change("map", "MappingUseMapboxOverride") with { Boolean = false }, now);
			Assert.That(map.Find("mapTokenPresent").Boolean, Is.False); Assert.That(snapshot.Find("mapTokenPresent").Boolean, Is.True);
		}
		[Test]
		public void Model_plan_tools_accept_only_their_narrow_schemas()
		{
			var draft = Resgrid.Ai.AdminAssistPrompt.ValidateCall(new Resgrid.Llm.LlmToolCall("1", "draft_plan", "{\"goal\":\"admin-security\"}"));
			Assert.That(draft.Id, Is.EqualTo("admin-security"));
			var verify = Resgrid.Ai.AdminAssistPrompt.ValidateCall(new Resgrid.Llm.LlmToolCall("2", "verify_step", "{\"planId\":\"opaque-id\",\"stepId\":\"step-1\"}"));
			Assert.That(verify.Value, Is.EqualTo("step-1"));
			Assert.Throws<ArgumentException>(() => Resgrid.Ai.AdminAssistPrompt.ValidateCall(new Resgrid.Llm.LlmToolCall("3", "draft_plan", "{\"goal\":\"admin-security\",\"apply\":true}")));
		}
		[TestCase("Proposed", "1", "1", "0", "Unknown", "Unknown", true, "InProgress")]
		[TestCase("Proposed", "1", "1", "0", "HumanConfirmed", "HumanConfirmed", true, "Done")]
		[TestCase("Proposed", "1", "1", "0", "HumanConfirmed", "HumanConfirmed", false, "InProgress")]
		[TestCase("Done", "2", "1", "0", "Unknown", "Unknown", true, "Drifted")]
		[TestCase("Done", "Unknown", "1", "0", "Unknown", "Unknown", true, "Drifted")]
		[TestCase("Skipped", "2", "1", "0", "Unknown", "Unknown", true, "Skipped")]
		public void Verification_does_not_confuse_saved_with_propagated_or_tested(string stored, string current, string proposed, string initial, string propagated, string behavior, bool prerequisites, string expected) =>
			Assert.That(ChangePlanPolicy.State(stored, current, proposed, initial, new(current == proposed ? "Confirmed" : "Unknown", "Pass", propagated, behavior, "digest", DateTime.UtcNow, null), prerequisites), Is.EqualTo(expected));
	}

	[TestFixture, NonParallelizable]
	public class ChangePlanServiceTests
	{
		private bool _enabled;
		[SetUp] public void Setup() { _enabled = AdminAssistConfig.PlansEnabled; AdminAssistConfig.PlansEnabled = true; }
		[TearDown] public void Cleanup() => AdminAssistConfig.PlansEnabled = _enabled;
		private sealed class Fixture
		{
			public readonly AdminAssistActor Actor = new(7, "admin");
			public readonly Mock<IAdminAssistAccessService> Access = new(); public readonly Mock<IAdminAssistPlanStore> Store = new();
			public readonly Mock<IAdminAssistPlanProtection> Protection = new(); public readonly Mock<IAdminAssistDiagnosticStore> Admission = new();
			public readonly Mock<IAdminAssistService> Assist = new(); public readonly Mock<IAdminAssistRepository> Revisions = new();
			public readonly Mock<IAuditService> Audit = new(); public readonly Mock<IPdfProvider> Pdf = new(); public readonly Mock<IDispatchImpactService> Dispatch = new();
			public readonly PlanDraftRequest Draft = new("Protect admin access", "admin-security");
			public ConfigurationSnapshot Snapshot; public SetupWorkspace Workspace; public AdminAssistPlanRow Row; public PlanContent Content;
			public readonly AdminAssistPlanService Service;
			public Fixture()
			{
				Snapshot = new(7, "admin", "1", DateTime.UtcNow, true, new Dictionary<string, ConfigurationEvidence> { ["Require2FAForAdmins"] = new("Require2FAForAdmins", EvidenceState.Known, "settings", "1", DateTime.UtcNow, Number: 0) });
				Workspace = new(7, 1, SetupMode.Fresh, new Dictionary<string, SetupAreaChoice>(), Array.Empty<string>(), Array.Empty<string>(), "catalog", null);
				Access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Access.Setup(a => a.GetCapabilitiesAsync(It.IsAny<AdminAssistActor>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ConfigurationCatalog().Capabilities.Select(c => new CapabilityAccess(c.Id, EvidenceState.Known, Array.Empty<string>(), true, c.Location.Url, DateTime.UtcNow)).ToArray());
				Access.Setup(a => a.GetCapabilityAsync(It.IsAny<AdminAssistActor>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((AdminAssistActor a, string id, CancellationToken _) => new(id, EvidenceState.Known, Array.Empty<string>(), true, "/User/Department/Security", DateTime.UtcNow));
				Assist.Setup(a => a.GetOverviewAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync((AdminAssistActor a, bool _, CancellationToken _) => new("catalog", Workspace, new(Snapshot with { ActorId = a.UserId }, Array.Empty<ConfigurationFinding>(), Array.Empty<string>()), Array.Empty<CapabilityAccess>()));
				Revisions.Setup(r => r.GetConfigurationRevisionAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(() => long.Parse(Snapshot.Revision));
				Revisions.Setup(r => r.GetWorkspaceAsync(7, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Workspace);
				Admission.Setup(a => a.AcquireDiagnosticLeaseAsync(It.IsAny<AdminAssistActor>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Protection.Setup(p => p.ProtectAsync(It.IsAny<AdminAssistActor>(), It.IsAny<AdminAssistPlanRow>(), It.IsAny<PlanContent>(), It.IsAny<CancellationToken>())).Callback<AdminAssistActor, AdminAssistPlanRow, PlanContent, CancellationToken>((_, row, content, _) => { row.Content = "enc2:test"; Content = content; }).Returns(Task.CompletedTask);
				Protection.Setup(p => p.ReadAsync(It.IsAny<AdminAssistActor>(), It.IsAny<AdminAssistPlanRow>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Content);
				Store.Setup(s => s.SavePlanAsync(It.IsAny<AdminAssistActor>(), It.IsAny<AdminAssistPlanRow>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>())).Callback<AdminAssistActor, AdminAssistPlanRow, long, string, long, CancellationToken>((_, row, _, _, _, _) => Row = Clone(row)).Returns(Task.CompletedTask);
				Store.Setup(s => s.ReadPlanAsync(It.IsAny<AdminAssistActor>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Clone(Row));
				Pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>(), "Letter")).Returns(new byte[] { 37, 80, 68, 70 });
				Service = new(Access.Object, new ConfigurationCatalog(), Assist.Object, Revisions.Object, Store.Object, Protection.Object, Admission.Object, Mock.Of<IAdminAssistConversationStore>(), Array.Empty<IOperationalImpactProvider>(), Dispatch.Object, Audit.Object, Pdf.Object, TimeProvider.System);
			}
			private static AdminAssistPlanRow Clone(AdminAssistPlanRow row) => row == null ? null : JsonSerializer.Deserialize<AdminAssistPlanRow>(JsonSerializer.Serialize(row));
			public async Task<PlanView> Create()
			{
				var draft = await Service.DraftAsync(Actor, Draft, CancellationToken.None);
				return await Service.CreateAsync(Actor, new(Draft, draft.PreviewDigest), CancellationToken.None);
			}
			public Task<PlanView> Read(AdminAssistActor actor = null) => Service.ReadAsync(actor ?? Actor, new(Row.Id, Row.Revision), CancellationToken.None);
			public void SetMfa(int value) => Snapshot = Snapshot with { Evidence = new Dictionary<string, ConfigurationEvidence> { ["Require2FAForAdmins"] = Snapshot.Find("Require2FAForAdmins") with { Number = value } } };
			public Task<PlanView> Command(PlanView view, string operation, string step = null) => Service.CommandAsync(Actor, new(view.Id, view.Revision, operation, view.PreviewDigest, step), CancellationToken.None);
		}
		[Test]
		public void Disabled_flag_denies_all_queries_and_commands_before_evidence_or_storage()
		{
			var f = new Fixture(); AdminAssistConfig.PlansEnabled = false;
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.DraftAsync(f.Actor, f.Draft, CancellationToken.None));
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.TemplatesAsync(f.Actor, CancellationToken.None));
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.ListAsync(f.Actor, CancellationToken.None));
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.CommandAsync(f.Actor, null, CancellationToken.None));
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.ExportAsync(f.Actor, null, CancellationToken.None));
			f.Assist.VerifyNoOtherCalls(); f.Store.VerifyNoOtherCalls(); f.Protection.VerifyNoOtherCalls();
		}
		[Test]
		public async Task Draft_and_model_verification_never_persist_and_create_requires_reviewed_current_digest()
		{
			var f = new Fixture(); var draft = await f.Service.DraftAsync(f.Actor, f.Draft, CancellationToken.None);
			f.Store.VerifyNoOtherCalls(); f.Protection.Verify(p => p.ProtectAsync(It.IsAny<AdminAssistActor>(), It.IsAny<AdminAssistPlanRow>(), It.IsAny<PlanContent>(), It.IsAny<CancellationToken>()), Times.Never);
			f.SetMfa(2);
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(() => f.Service.CreateAsync(f.Actor, new(f.Draft, draft.PreviewDigest), CancellationToken.None));
			var saved = await f.Create(); var revision = f.Row.Revision;
			await f.Service.VerifyStepAsync(f.Actor, saved.Id, saved.Steps[0].Change.Id, CancellationToken.None);
			Assert.That(f.Row.Revision, Is.EqualTo(revision)); Assert.That(f.Row.Shared, Is.False); Assert.That(f.Row.Content, Is.EqualTo("enc2:test"));
		}
		[Test]
		public async Task Human_confirmation_requires_live_match_rules_and_current_review_then_drift_invalidates_it()
		{
			var f = new Fixture(); var saved = await f.Create(); var step = saved.Steps[0].Change.Id;
			Assert.ThrowsAsync<ArgumentException>(() => f.Command(saved, "start", step));
			f.SetMfa(1); var current = await f.Read(); Assert.That(current.Steps[0].State, Is.EqualTo("InProgress"));
			current = await f.Service.CommandAsync(f.Actor, new(current.Id, current.Revision, "review", current.PreviewDigest, WindowUtc: DateTime.UtcNow.AddHours(1), Fallback: "Have another administrator verify recovery before the change."), CancellationToken.None);
			current = await f.Service.CommandAsync(f.Actor, new(current.Id, current.Revision, "attest", current.PreviewDigest, step, PropagationChecked: true, BehaviorTested: true), CancellationToken.None);
			Assert.That(current.Steps[0].State, Is.EqualTo("Done")); Assert.That(current.Steps[0].Verification.BehaviorTested, Is.EqualTo("HumanConfirmed"));
			f.SetMfa(2); Assert.That((await f.Read()).Steps[0].State, Is.EqualTo("Drifted"));
		}
		[Test]
		public async Task Tenant_owner_revision_and_source_gates_are_rechecked_on_read_and_share()
		{
			var f = new Fixture(); var saved = await f.Create(); var other = f.Actor with { UserId = "other" };
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Read(other));
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Read(f.Actor with { DepartmentId = 8 }));
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(() => f.Service.ReadAsync(f.Actor, new(saved.Id, 999), CancellationToken.None));
			await f.Service.CommandAsync(f.Actor, new(saved.Id, saved.Revision, "share", saved.PreviewDigest, Shared: true), CancellationToken.None);
			Assert.That((await f.Read(other)).Owned, Is.False);
			f.Access.Setup(a => a.GetCapabilityAsync(other, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new CapabilityAccess("source", EvidenceState.Redacted, Array.Empty<string>(), false, null, DateTime.UtcNow));
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Read(other));
			f.Access.Setup(a => a.CanAccessAsync(f.Actor, false, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Read(other));
		}
		[Test]
		public async Task Preview_detects_nonjournaled_evidence_and_scope_changes()
		{
			var f = new Fixture(); var saved = await f.Create(); f.Workspace = f.Workspace with { ScopeRevision = 3 };
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(() => f.Command(saved, "close"));
			var count = 0;
			f.Assist.Setup(a => a.GetOverviewAsync(f.Actor, false, It.IsAny<CancellationToken>())).ReturnsAsync(() =>
			{
				if (++count > 1) f.SetMfa(1);
				return new AdminAssistOverview("catalog", f.Workspace, new(f.Snapshot, Array.Empty<ConfigurationFinding>(), Array.Empty<string>()), Array.Empty<CapabilityAccess>());
			});
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(() => f.Read());
		}
		[Test]
		public async Task Pdf_is_fresh_escaped_reauthorized_and_deleted_or_expired_content_cannot_be_replayed()
		{
			var f = new Fixture(); var saved = await f.Create();
			var malicious = saved with { Goal = "<img src='https://external.invalid'>", Review = new("actor", DateTime.UtcNow, saved.PreviewDigest, DateTime.UtcNow, "private") };
			Assert.That(AdminAssistPlanService.RenderPdf(malicious, "en"), Does.Contain("&lt;img").And.Not.Contain("<img"));
			var pdf = await f.Service.ExportAsync(f.Actor, new(saved.Id, saved.Revision, "export", saved.PreviewDigest), CancellationToken.None); Assert.That(pdf.ContentType, Is.EqualTo("application/pdf"));
			f.SetMfa(1); Assert.ThrowsAsync<AdminAssistConcurrencyException>(() => f.Service.ExportAsync(f.Actor, new(saved.Id, saved.Revision, "export", saved.PreviewDigest), CancellationToken.None));
			f.Row.ClosedOnUtc = DateTime.UtcNow.AddDays(-91); Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Read());
			f.Row.ClosedOnUtc = null; f.Row.Deleted = true; Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Read());
		}
		[Test]
		public void Admission_failure_or_cancellation_does_not_access_sources()
		{
			var f = new Fixture(); f.Admission.Setup(a => a.AcquireDiagnosticLeaseAsync(It.IsAny<AdminAssistActor>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(() => f.Service.DraftAsync(f.Actor, f.Draft, CancellationToken.None)); f.Assist.VerifyNoOtherCalls();
		}
		[Test]
		public async Task Routing_verification_requires_the_shared_resolver_scenario_and_rejects_empty_shift_fallback()
		{
			var f = new Fixture(); var now = DateTime.UtcNow;
			var facts = new Dictionary<string, ConfigurationEvidence>
			{
				["DispatchShiftInsteadOfGroup"] = new("DispatchShiftInsteadOfGroup", EvidenceState.Known, "settings", "1", now, Boolean: true),
				["AutoSetStatusForShiftDispatchPersonnel"] = new("AutoSetStatusForShiftDispatchPersonnel", EvidenceState.Known, "settings", "1", now, Boolean: false),
				["groupsWithoutShiftCoverage"] = new("groupsWithoutShiftCoverage", EvidenceState.Known, "coverage", "1", now, Number: 0),
				["UnitDispatchAlsoDispatchToAssignedPersonnel"] = new("UnitDispatchAlsoDispatchToAssignedPersonnel", EvidenceState.Known, "settings", "1", now, Boolean: false),
				["UnitDispatchAlsoDispatchToGroup"] = new("UnitDispatchAlsoDispatchToGroup", EvidenceState.Known, "settings", "1", now, Boolean: false)
			};
			f.Snapshot = f.Snapshot with { Evidence = facts };
			var request = new PlanDraftRequest("Review shift routing", "shift-routing");
			Assert.That((await f.Service.DraftAsync(f.Actor, request, CancellationToken.None)).Steps[0].Verification.Rule, Is.EqualTo("Unknown"));
			f.Dispatch.Setup(d => d.PreviewAsync(f.Actor, It.IsAny<DispatchImpactRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ConfigurationImpactReport("routing", "1", now, "dispatch-impact-v1", new("Critical", "a", "b", "c", "d", "e"), new[] { new ConfigurationImpactMetric("Impact.DispatchFallback", EvidenceState.Known, 0, 2) }, Array.Empty<ConfigurationImpactRule>(), Array.Empty<string>(), "/User/Department/DispatchSettings"));
			var result = await f.Service.DraftAsync(f.Actor, request with { DispatchScenario = new(123, now) }, CancellationToken.None);
			Assert.That(result.Steps[0].Verification.Rule, Is.EqualTo("Fail"));
			f.Dispatch.Verify(d => d.PreviewAsync(f.Actor, It.Is<DispatchImpactRequest>(r => r.CallId == 123 && r.ShiftInsteadOfGroup && !r.UnitCrew && !r.UnitGroup), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
		}
		[Test]
		public async Task Skipped_steps_are_explicit_and_source_mutation_commands_are_rejected()
		{
			var f = new Fixture(); var saved = await f.Create(); var skipped = await f.Command(saved, "skip", saved.Steps[0].Change.Id);
			Assert.That(skipped.Steps[0].State, Is.EqualTo("Skipped"));
			Assert.ThrowsAsync<ArgumentException>(() => f.Service.CommandAsync(f.Actor, new(skipped.Id, skipped.Revision, "apply", skipped.PreviewDigest), CancellationToken.None));
		}
	}
}
