using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Resources;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.AdminAssist;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	public sealed class AdminAssistPlanService(IAdminAssistAccessService access, IAdminAssistCatalog catalog,
		IAdminAssistService assist, IAdminAssistRepository revisions, IAdminAssistPlanStore store,
		IAdminAssistPlanProtection protection, IAdminAssistDiagnosticStore admission, IAdminAssistConversationStore conversations,
		IEnumerable<IOperationalImpactProvider> providers, IDispatchImpactService dispatch, IAuditService audit, IPdfProvider pdf, TimeProvider clock) : IAdminAssistPlans, IAdminAssistPlanQueries
	{
		private static readonly ResourceManager Labels = new(typeof(Resgrid.Localization.Areas.User.AdminAssist.AdminAssist));
		private DateTime Now => clock.GetUtcNow().UtcDateTime;
		private async Task RequireAsync(AdminAssistActor actor, CancellationToken ct)
		{
			if (!AdminAssistConfig.PlansEnabled || !await access.CanAccessAsync(actor, false, ct).WaitAsync(ct)) throw new UnauthorizedAccessException();
			await protection.RequireAsync(actor, ct);
		}
		private async Task<T> BoundedAsync<T>(AdminAssistActor actor, Func<CancellationToken, Task<T>> operation, CancellationToken ct)
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(60)); ct = deadline.Token;
			await RequireAsync(actor, ct);
			var lease = Guid.NewGuid().ToString("D");
			if (!await admission.AcquireDiagnosticLeaseAsync(actor, lease, Now, ct)) throw new AdminAssistConcurrencyException();
			try { return await operation(ct).WaitAsync(ct); }
			finally { using var release = new CancellationTokenSource(TimeSpan.FromSeconds(3)); try { await admission.ReleaseDiagnosticLeaseAsync(actor, lease, release.Token); } catch (Exception) { /* The lease expires; do not log protected goals. */ } }
		}
		public async Task<IReadOnlyList<PlanTemplate>> TemplatesAsync(AdminAssistActor actor, CancellationToken ct)
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(20)); ct = deadline.Token;
			await RequireAsync(actor, ct);
			var capabilities = await access.GetCapabilitiesAsync(actor, ct).WaitAsync(ct);
			var allowed = (capabilities ?? Array.Empty<CapabilityAccess>()).Where(c => c.State == EvidenceState.Known && c.CanConfigure).Select(c => c.CapabilityId).ToHashSet(StringComparer.Ordinal);
			var result = new List<PlanTemplate>();
			foreach (var template in ChangePlanPolicy.Templates(catalog))
			{
				var changes = template.ChangeSet.Changes.Where(c => catalog.Capabilities.Any(owner => allowed.Contains(owner.Id) && (owner.Id == c.CatalogId || owner.SettingIds.Contains(c.CatalogId)))).ToList();
				while (changes.RemoveAll(c => c.Prerequisites.Any(id => changes.All(other => other.Id != id))) > 0) { }
				if (changes.Count > 0) result.Add(template with { ChangeSet = template.ChangeSet with { Changes = changes } });
			}
			await RequireAsync(actor, ct); return result;
		}
		public Task<PlanView> DraftAsync(AdminAssistActor actor, PlanDraftRequest request, CancellationToken ct) => BoundedAsync(actor, async token =>
		{
			var content = NewContent(await NormalizeAsync(actor, request, token));
			await ValidateSourceAsync(actor, content.Draft.ChangeSet, token);
			return await BuildAsync(actor, null, content, token);
		}, ct);
		private async Task<PlanDraftRequest> NormalizeAsync(AdminAssistActor actor, PlanDraftRequest request, CancellationToken ct)
		{
			if (request?.TemplateId != null)
			{
				if (request.ChangeSet != null) throw new ArgumentException("Choose a template or a manual proposal.");
				var template = (await TemplatesAsync(actor, ct)).SingleOrDefault(t => t.Id == request.TemplateId) ?? throw new UnauthorizedAccessException();
				request = request with { TemplateId = null, ChangeSet = template.ChangeSet };
			}
			return ChangePlanPolicy.Normalize(request, catalog);
		}
		private static PlanContent NewContent(PlanDraftRequest draft) => new(draft, Array.Empty<string>(), new Dictionary<string, string>(), new Dictionary<string, string>(), Array.Empty<PlanAttestation>());
		private async Task ValidateSourceAsync(AdminAssistActor actor, ConfigurationChangeSet set, CancellationToken ct)
		{
			if (set.Source == "finding" && !catalog.Rules.Any(r => r.Id == set.SourceId)) throw new ArgumentException("Unknown finding.");
			if (set.Source == "archetype" && !catalog.Packs.Any(p => p.Id == set.SourceId)) throw new ArgumentException("Unknown archetype.");
			if (set.Source == "conversation" && (!Guid.TryParseExact(set.SourceId, "D", out _) || await conversations.GetRevisionAsync(actor, set.SourceId, ct) < 1)) throw new UnauthorizedAccessException();
		}
		public Task<PlanView> CreateAsync(AdminAssistActor actor, PlanCreateCommand command, CancellationToken ct) => BoundedAsync(actor, async token =>
		{
			if (command == null) throw new ArgumentException("Invalid plan.");
			var content = NewContent(await NormalizeAsync(actor, command.Draft, token));
			await ValidateSourceAsync(actor, content.Draft.ChangeSet, token);
			var preview = await BuildAsync(actor, null, content, token); Match(preview, command.PreviewDigest);
			content = content with { OperatingPacksAtCreation = preview.OperatingPacksAtCreation, InitialValues = preview.Steps.ToDictionary(s => s.Change.Id, s => s.CurrentValue) };
			var row = new AdminAssistPlanRow { Id = Guid.NewGuid().ToString("D"), DepartmentId = actor.DepartmentId, UserId = actor.UserId, Revision = 1, Status = "Proposed", CreatedOnUtc = Now, UpdatedOnUtc = Now };
			await SaveAsync(actor, row, content, 0, preview, "Create", token);
			return await BuildAsync(actor, row, content, token);
		}, ct);
		private async Task<AdminAssistPlanRow> RowAsync(AdminAssistActor actor, string id, long? revision, CancellationToken ct)
		{
			if (!Guid.TryParseExact(id, "D", out _) || revision < 1) throw new ArgumentException("Invalid plan reference.");
			var row = await store.ReadPlanAsync(actor, id, ct) ?? throw new UnauthorizedAccessException();
			if (row.Id != id || row.DepartmentId != actor.DepartmentId || row.Deleted || (row.UserId != actor.UserId && !row.Shared) ||
				row.ClosedOnUtc < Now.AddDays(-Math.Clamp(AdminAssistConfig.ClosedPlanRetentionDays, 1, 90))) throw new UnauthorizedAccessException();
			// Shared plans never survive their owner's removal or demotion as an authorization artifact.
			if (row.UserId != actor.UserId && !await access.CanAccessAsync(new(actor.DepartmentId, row.UserId, actor.Locale), false, ct)) throw new UnauthorizedAccessException();
			if (revision.HasValue && row.Revision != revision) throw new AdminAssistConcurrencyException();
			return row;
		}
		public Task<PlanView> ReadAsync(AdminAssistActor actor, PlanReference reference, CancellationToken ct) => BoundedAsync(actor, async token =>
		{
			if (reference == null) throw new ArgumentException("Invalid plan reference.");
			var row = await RowAsync(actor, reference.PlanId, reference.ExpectedRevision, token);
			var result = await BuildAsync(actor, row, await protection.ReadAsync(actor, row, token), token);
			await RowAsync(actor, row.Id, row.Revision, token); await AuditAsync(actor, row.Id, "Read", token); return result;
		}, ct);
		public Task<PlanVerification> VerifyStepAsync(AdminAssistActor actor, string planId, string stepId, CancellationToken ct) => BoundedAsync(actor, async token =>
		{
			if (!ChangePlanPolicy.IsId(stepId)) throw new ArgumentException("Invalid step.");
			var row = await RowAsync(actor, planId, null, token);
			var view = await BuildAsync(actor, row, await protection.ReadAsync(actor, row, token), token);
			await RowAsync(actor, row.Id, row.Revision, token); await AuditAsync(actor, row.Id, "VerifyEvidence", token);
			return view.Steps.SingleOrDefault(s => s.Change.Id == stepId)?.Verification ?? throw new ArgumentException("Unknown step.");
		}, ct);
		public Task<IReadOnlyList<PlanListItem>> ListAsync(AdminAssistActor actor, CancellationToken ct) => BoundedAsync<IReadOnlyList<PlanListItem>>(actor, async token =>
		{
			var result = new List<PlanListItem>();
			foreach (var item in await store.ListPlansAsync(actor, Now.AddDays(-Math.Clamp(AdminAssistConfig.ClosedPlanRetentionDays, 1, 90)), token))
			{
				try
				{
					var row = await RowAsync(actor, item.Id, item.Revision, token);
					var content = await protection.ReadAsync(actor, row, token);
					await DestinationsAsync(actor, content.Draft.ChangeSet, token);
					result.Add(new(row.Id, row.Revision, row.Shared, row.UserId == actor.UserId, row.Status, row.UpdatedOnUtc));
				}
				catch (UnauthorizedAccessException) { /* Even existence is hidden when a source or owner is no longer authorized. */ }
			}
			await RequireAsync(actor, token); return result;
		}, ct);
		private async Task<Dictionary<string, string>> DestinationsAsync(AdminAssistActor actor, ConfigurationChangeSet set, CancellationToken ct)
		{
			var result = new Dictionary<string, string>();
			foreach (var change in set.Changes)
			{
				var setting = catalog.Settings.SingleOrDefault(s => s.Id == change.CatalogId);
				var owners = setting == null ? catalog.Capabilities.Where(c => c.Id == change.CatalogId) : catalog.Capabilities.Where(c => c.SettingIds.Contains(setting.Id));
				foreach (var owner in owners)
				{
					var current = await access.GetCapabilityAsync(actor, owner.Id, ct).WaitAsync(ct);
					if (current?.State == EvidenceState.Known && current.CanConfigure) { result[change.Id] = setting?.Location.Url ?? owner.Location.Url; break; }
				}
				if (!result.ContainsKey(change.Id)) throw new UnauthorizedAccessException();
			}
			return result;
		}
		private async Task<PlanView> BuildAsync(AdminAssistActor actor, AdminAssistPlanRow row, PlanContent content, CancellationToken ct)
		{
			var draft = ChangePlanPolicy.Normalize(content.Draft, catalog);
			var paths = await DestinationsAsync(actor, draft.ChangeSet, ct);
			var overview = await assist.GetOverviewAsync(actor, false, ct).WaitAsync(ct);
			var snapshot = overview.Report.Snapshot; var now = Now;
			if (snapshot.DepartmentId != actor.DepartmentId || snapshot.ActorId != actor.UserId || overview.Workspace.DepartmentId != actor.DepartmentId || !snapshot.Consistent) throw new AdminAssistConcurrencyException();
			var evidenceDigest = EvidenceDigest(snapshot, now);
			var intermediate = snapshot; var steps = new List<PlanStepView>();
			foreach (var change in draft.ChangeSet.Changes)
			{
				var setting = catalog.Settings.SingleOrDefault(s => s.Id == change.CatalogId);
				string current, label, rationale, instructions, rollback; ConfigurationImpactReport impact;
				ConfigurationRuleDefinition[] rules;
				if (setting != null)
				{
					current = ChangePlanPolicy.Value(snapshot.Find(change.CatalogId.Substring(8)), now); label = setting.LabelKey; rationale = setting.HelpKey;
					instructions = setting.Impact.VerificationKey; rollback = setting.Impact.ReversibilityKey;
					var request = new ConfigurationImpactRequest(setting.Id, snapshot.Revision, change.Boolean, change.Number);
					impact = new ConfigurationImpactEvaluator(catalog).Evaluate(intermediate, request, now, TimeSpan.FromMinutes(1));
					foreach (var provider in providers.Where(p => p.Supports(setting.Id)))
					{
						var operational = await provider.EvaluateAsync(actor, intermediate, request, ct).WaitAsync(ct);
						impact = impact with { Metrics = impact.Metrics.Concat(operational.Metrics).ToArray(), LimitKeys = impact.LimitKeys.Concat(operational.LimitKeys).Distinct().ToArray(), EvaluatorVersion = impact.EvaluatorVersion + ";" + operational.Version };
					}
					rules = catalog.Rules.Where(r => r.AppliesWhen.Concat(r.FailsWhen).Any(c => c.EvidenceId == change.CatalogId.Substring(8))).ToArray();
				}
				else
				{
					var capability = catalog.Capabilities.Single(c => c.Id == change.CatalogId);
					var fact = capability.Setup == null ? null : snapshot.Find(capability.Setup.EvidenceId);
					current = fact?.IsFresh(now, TimeSpan.FromMinutes(1)) != true || !fact.Number.HasValue || fact.Number < 0 ? "Unknown" : fact.Number >= capability.Setup.Minimum ? "Configured" : "NotConfigured";
					label = capability.LabelKey; rationale = capability.PurposeKey; instructions = capability.Setup?.GuidanceKey ?? capability.AdoptionKey; rollback = "Plan.RollbackManual";
					impact = new(change.CatalogId, snapshot.Revision, now, "guided-task-v1", new("High", "Plan.AudienceUnknown", capability.ValueKey, "Plan.TimingUnknown", rollback, instructions), Array.Empty<ConfigurationImpactMetric>(), Array.Empty<ConfigurationImpactRule>(), new[] { "Plan.ManualImpact", "Impact.NoMutation", "Impact.Window" }, paths[change.Id]);
					rules = catalog.Rules.Where(r => (capability.Setup?.RuleIds ?? capability.RuleIds).Contains(r.Id)).ToArray();
				}
				var proposed = ChangePlanPolicy.Proposed(change);
				var results = rules.Select(r => new ConfigurationRule(r).Evaluate(snapshot, now, TimeSpan.FromMinutes(1)).Result).ToArray();
				var rule = results.Any(r => r == RuleResult.Fail) ? "Fail" : results.Any(r => r == RuleResult.Unknown) || results.Length == 0 ? "Unknown" : "Pass";
				if (change.CatalogId is "setting.DispatchShiftInsteadOfGroup" or "setting.AutoSetStatusForShiftDispatchPersonnel")
				{
					var scenario = draft.DispatchScenario;
					var crew = intermediate.Find("UnitDispatchAlsoDispatchToAssignedPersonnel"); var group = intermediate.Find("UnitDispatchAlsoDispatchToGroup");
					var shift = change.CatalogId == "setting.DispatchShiftInsteadOfGroup" ? change.Boolean : intermediate.Find("DispatchShiftInsteadOfGroup").Boolean;
					if (scenario == null || (scenario.SimulationTimeUtc - now).Duration() > TimeSpan.FromDays(7) || !crew.IsFresh(now, TimeSpan.FromMinutes(1)) || !group.IsFresh(now, TimeSpan.FromMinutes(1)) || !crew.Boolean.HasValue || !group.Boolean.HasValue || !shift.HasValue) rule = "Unknown";
					else
					{
						var routing = await dispatch.PreviewAsync(actor, new(snapshot.Revision, scenario.CallId, scenario.SimulationTimeUtc, shift.Value, crew.Boolean.Value, group.Boolean.Value), ct).WaitAsync(ct);
						var fallback = routing.Metrics.SingleOrDefault(m => m.LabelKey == "Impact.DispatchFallback");
						if (fallback?.State != EvidenceState.Known || !fallback.After.HasValue) rule = "Unknown";
						else if (fallback.After > 0) rule = "Fail";
						if (change.CatalogId == "setting.DispatchShiftInsteadOfGroup") impact = impact with { Metrics = impact.Metrics.Concat(routing.Metrics).ToArray(), LimitKeys = impact.LimitKeys.Concat(routing.LimitKeys).Distinct().ToArray(), EvaluatorVersion = impact.EvaluatorVersion + ";" + routing.EvaluatorVersion };
					}
					impact = impact with { LimitKeys = impact.LimitKeys.Append("Plan.DispatchScenarioHelp").ToArray() };
				}
				if (setting == null && results.Any(r => r == RuleResult.NotApplicable)) rule = "Unknown";
				var saved = current == "Unknown" ? "Unknown" : current == proposed ? "Confirmed" : "NotMatched";
				var inputs = rules.SelectMany(r => r.AppliesWhen.Concat(r.FailsWhen)).Select(c => c.EvidenceId)
					.Append(setting != null ? setting.Id.Substring(8) : catalog.Capabilities.Single(c => c.Id == change.CatalogId).Setup?.EvidenceId)
					.Where(id => id != null).Distinct().OrderBy(id => id, StringComparer.Ordinal).Select(id => snapshot.Find(id))
					.Select(f => new { f.Id, f.State, f.Version, f.Boolean, f.Number, f.Code, Fresh = f.IsFresh(now, TimeSpan.FromMinutes(1)) });
				var digest = ChangePlanPolicy.Digest(new { catalog.Version, inputs, change, current, rule, impact.Metrics, impact.RuleChanges, Scope = overview.Workspace.ScopeRevision });
				var attestation = content.Attestations.SingleOrDefault(a => a.StepId == change.Id && a.EvidenceDigest == digest);
				// Human confirmations supplement, never replace, the live value and deterministic rule checks.
				if (attestation != null && !await access.CanAccessAsync(new(actor.DepartmentId, attestation.ActorId, actor.Locale), false, ct)) attestation = null;
				var verification = new PlanVerification(saved, rule, attestation?.PropagationChecked == true ? "HumanConfirmed" : "Unknown", attestation?.BehaviorTested == true ? "HumanConfirmed" : "Unknown", digest, now, saved == "Confirmed" && attestation == null ? now.AddMinutes(5) : null);
				content.States.TryGetValue(change.Id, out var stored); content.InitialValues.TryGetValue(change.Id, out var initial);
				var state = ChangePlanPolicy.State(stored, current, proposed, initial ?? current, verification, change.Prerequisites.All(id => steps.Any(s => s.Change.Id == id && s.State == "Done")));
				steps.Add(new(change, label, state, current, proposed, rationale, instructions, paths[change.Id], rollback, impact, verification));
				if (state is not ("Skipped" or "Superseded")) intermediate = ChangePlanPolicy.Overlay(intermediate, change, now);
			}
			var finalMetrics = new List<ConfigurationImpactMetric>();
			var activeSettings = steps.Where(s => s.State is not ("Skipped" or "Superseded")).Select(s => s.Change.CatalogId).ToArray();
			foreach (var provider in providers.OfType<IComposedOperationalImpactProvider>().Where(p => p.AppliesTo(activeSettings)))
			{
				var projection = await provider.EvaluateComposedAsync(actor, snapshot, intermediate, activeSettings, ct).WaitAsync(ct);
				finalMetrics.AddRange(projection.Metrics);
			}
			var final = new PlanImpact(ChangePlanPolicy.HighestRisk(steps.Where(s => s.State is not ("Skipped" or "Superseded")).Select(s => s.Impact.Profile.Risk)), null,
				ChangePlanPolicy.RuleDelta(catalog, snapshot, intermediate, now), new[] { "Plan.UniqueUnknown", "Plan.ManualImpact", "Plan.TimingUnknown", "Impact.Window", "Plan.Intermediate", "Plan.VerificationBoundary" }, "Plan.CommunicationDraft")
			{ FinalMetrics = finalMetrics };
			var fingerprint = ChangePlanPolicy.Digest(new
			{
				draft,
				catalog.Version,
				snapshot.Revision,
				overview.Workspace.ScopeRevision,
				evidenceDigest,
				Steps = steps.Select(s => new { s.Change, s.CurrentValue, s.ProposedValue, s.Destination, s.Impact.Metrics, s.Impact.RuleChanges, s.Impact.EvaluatorVersion, s.Impact.Profile }),
				final
			});
			if (snapshot.Revision != (await revisions.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture) ||
				overview.Workspace.ScopeRevision != (await revisions.GetWorkspaceAsync(actor.DepartmentId, actor.UserId, catalog.Version, ct)).ScopeRevision) throw new AdminAssistConcurrencyException();
			var after = await assist.GetOverviewAsync(actor, false, ct).WaitAsync(ct);
			if (after.Report.Snapshot.DepartmentId != actor.DepartmentId || after.Report.Snapshot.ActorId != actor.UserId || !after.Report.Snapshot.Consistent || after.Report.Snapshot.Revision != snapshot.Revision || after.Workspace.ScopeRevision != overview.Workspace.ScopeRevision || EvidenceDigest(after.Report.Snapshot, Now) != evidenceDigest) throw new AdminAssistConcurrencyException();
			var afterPaths = await DestinationsAsync(actor, draft.ChangeSet, ct); if (paths.Any(p => afterPaths[p.Key] != p.Value)) throw new AdminAssistConcurrencyException();
			await RequireAsync(actor, ct);
			var packs = row == null ? (snapshot.Find("operatingPackIds").Code ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Where(p => catalog.Packs.Any(c => c.Id == p)).ToArray() : content.OperatingPacksAtCreation;
			var status = row?.Status is "Closed" or "Superseded" ? row.Status : steps.All(s => s.State is "Done" or "Skipped" or "Superseded") ? "Complete" : steps.Any(s => s.State == "Drifted") ? "Drifted" : steps.Any(s => s.State == "InProgress" || s.State == "Done") ? "InProgress" : "Proposed";
			return new(row?.Id, row?.Revision ?? 0, row == null || row.UserId == actor.UserId, row?.Shared ?? false, status, row?.CreatedOnUtc ?? now, row?.UpdatedOnUtc ?? now, draft.Goal, draft.ChangeSet, packs, steps, final, catalog.Version, snapshot.Revision, overview.Workspace.ScopeRevision, now, fingerprint, content.Review);
		}
		private static string EvidenceDigest(ConfigurationSnapshot snapshot, DateTime now) => ChangePlanPolicy.Digest(snapshot.Evidence.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { p.Key, p.Value.State, p.Value.Source, p.Value.Version, p.Value.Boolean, p.Value.Number, p.Value.Code, Fresh = p.Value.IsFresh(now, TimeSpan.FromMinutes(1)) }));
		private static void Match(PlanView preview, string digest) { if (digest?.Length != 64 || preview.PreviewDigest != digest) throw new AdminAssistConcurrencyException(); }
		public Task<PlanView> CommandAsync(AdminAssistActor actor, PlanCommand command, CancellationToken ct) => BoundedAsync(actor, async token =>
		{
			if (command == null) throw new ArgumentException("Invalid command.");
			var row = await RowAsync(actor, command.PlanId, command.ExpectedRevision, token);
			if (row.UserId != actor.UserId) throw new UnauthorizedAccessException();
			var content = await protection.ReadAsync(actor, row, token);
			var preview = await BuildAsync(actor, row, content, token); Match(preview, command.PreviewDigest);
			if (row.ClosedOnUtc.HasValue && command.Operation != "delete") throw new ArgumentException("This plan is closed; draft a replacement.");
			var states = content.States.ToDictionary(p => p.Key, p => p.Value);
			var step = command.StepId == null ? null : preview.Steps.SingleOrDefault(s => s.Change.Id == command.StepId) ?? throw new ArgumentException("Unknown step.");
			if (command.Operation is "start" or "skip" or "supersede-step" or "verify" or "attest")
			{
				if (step == null) throw new ArgumentException("Select a step.");
				if (command.Operation is "start" or "attest")
				{
					if (step.Change.Prerequisites.Any(id => !preview.Steps.Any(s => s.Change.Id == id && s.State == "Done"))) throw new ArgumentException("Verify prerequisites first.");
					if (preview.Impact.Risk is "High" or "Critical" && (content.Review == null || content.Review.PreviewDigest != preview.PreviewDigest)) throw new ArgumentException("Review the current impact and maintenance window first.");
				}
				switch (command.Operation)
				{
					case "start": states[step.Change.Id] = "InProgress"; break;
					case "skip": states[step.Change.Id] = "Skipped"; break;
					case "supersede-step": states[step.Change.Id] = "Superseded"; break;
					case "verify": states[step.Change.Id] = step.State; break;
					case "attest":
						if (!command.PropagationChecked || !command.BehaviorTested || step.Verification.Saved != "Confirmed" || step.Verification.Rule != "Pass") throw new ArgumentException("Live configuration and rules must pass before confirming observed propagation and behavior.");
						content = content with { Attestations = content.Attestations.Where(a => a.StepId != step.Change.Id).Append(new PlanAttestation(step.Change.Id, actor.UserId, Now, step.Verification.EvidenceDigest, true, true)).ToArray() };
						states[step.Change.Id] = "Done"; break;
				}
				content = content with { States = states };
			}
			else switch (command.Operation)
				{
					case "share": if (!command.Shared.HasValue) throw new ArgumentException("Select a sharing preference."); row.Shared = command.Shared.Value; break;
					case "review":
						if (command.WindowUtc?.Kind != DateTimeKind.Utc || command.WindowUtc < Now.AddHours(-24) || command.WindowUtc > Now.AddDays(90) || string.IsNullOrWhiteSpace(command.Fallback) || command.Fallback.Length > 1000) throw new ArgumentException("Choose a UTC maintenance window and fallback procedure.");
						content = content with { Review = new(actor.UserId, Now, preview.PreviewDigest, command.WindowUtc.Value, command.Fallback.Trim()) }; break;
					case "close": row.Status = "Closed"; row.ClosedOnUtc = Now; break;
					case "supersede": row.Status = "Superseded"; row.ClosedOnUtc = Now; break;
					case "delete": row.Deleted = true; row.Shared = false; row.ClosedOnUtc ??= Now; break;
					default: throw new ArgumentException("Unknown plan command.");
				}
			var updated = await BuildAsync(actor, row, content, token);
			row.Status = updated.Status; row.Revision++; row.UpdatedOnUtc = Now;
			await SaveAsync(actor, row, content, command.ExpectedRevision, updated, command.Operation, token);
			return updated with { Revision = row.Revision, UpdatedOnUtc = row.UpdatedOnUtc };
		}, ct);
		private async Task SaveAsync(AdminAssistActor actor, AdminAssistPlanRow row, PlanContent content, long expected, PlanView preview, string action, CancellationToken ct)
		{
			await protection.ProtectAsync(actor, row, content, ct);
			await DestinationsAsync(actor, content.Draft.ChangeSet, ct); await RequireAsync(actor, ct);
			await AuditAsync(actor, row.Id, action, ct);
			await store.SavePlanAsync(actor, row, expected, preview.SnapshotRevision, preview.ScopeRevision, ct);
		}
		public Task<PlanPdf> ExportAsync(AdminAssistActor actor, PlanCommand command, CancellationToken ct) => BoundedAsync<PlanPdf>(actor, async token =>
		{
			if (command == null || command.Operation != "export") throw new ArgumentException("Invalid export.");
			var row = await RowAsync(actor, command.PlanId, command.ExpectedRevision, token);
			var content = await protection.ReadAsync(actor, row, token);
			var preview = await BuildAsync(actor, row, content, token); Match(preview, command.PreviewDigest);
			// Only freshly authorized, escaped text enters the existing PDF provider. No remote assets or supplied HTML.
			var bytes = pdf.ConvertHtmlToPdf(RenderPdf(preview, actor.Locale), "Letter");
			if (bytes == null || bytes.Length == 0 || bytes.Length > 10 * 1024 * 1024) throw new InvalidOperationException("PDF unavailable.");
			var refreshed = await BuildAsync(actor, row, content, token); Match(refreshed, preview.PreviewDigest);
			await RowAsync(actor, row.Id, row.Revision, token); await RequireAsync(actor, token); await AuditAsync(actor, row.Id, "Export", token);
			return new("resgrid-change-plan.pdf", "application/pdf", Convert.ToBase64String(bytes));
		}, ct);
		public static string RenderPdf(PlanView plan, string locale)
		{
			var culture = CultureInfo.GetCultureInfo(locale ?? "en");
			string E(string value) => WebUtility.HtmlEncode(value ?? "");
			string T(string key) => E(Labels.GetString(key, culture) ?? key);
			var html = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><style>body{font-family:sans-serif;font-size:11pt}section{page-break-inside:avoid}p{white-space:pre-wrap}td,th{padding:6px;border:1px solid #bbb}table{border-collapse:collapse;width:100%}</style></head><body>");
			html.Append("<h1>").Append(T("Plan.Title")).Append("</h1><p>").Append(E(plan.Goal)).Append("</p><p>").Append(T("Plan.ExportBoundary")).Append("</p><p>").Append(E(plan.AsOfUtc.ToString("O"))).Append(" · ").Append(E(plan.CatalogVersion)).Append(" · ").Append(E(plan.SnapshotRevision)).Append("</p><p>").Append(T("Plan.Risk")).Append(": ").Append(E(plan.Impact.Risk)).Append("</p>");
			foreach (var metric in plan.Impact.FinalMetrics) html.Append("<p>").Append(T(metric.LabelKey)).Append(": ").Append(E(metric.Before?.ToString(culture) ?? "Unknown")).Append(" → ").Append(E(metric.After?.ToString(culture) ?? "Unknown")).Append("</p>");
			foreach (var rule in plan.Impact.FinalRuleChanges) html.Append("<p>").Append(T(rule.TitleKey)).Append(": ").Append(T("Ui." + rule.Before)).Append(" → ").Append(T("Ui." + rule.After)).Append("</p>");
			if (plan.Review != null) html.Append("<h2>").Append(T("Plan.Review")).Append("</h2><p>").Append(T(plan.Review.PreviewDigest == plan.PreviewDigest ? "Plan.ReviewCurrent" : "Plan.ReviewExpired")).Append(" · ").Append(E(plan.Review.WindowUtc.ToString("O"))).Append("</p><p>").Append(E(plan.Review.Fallback)).Append("</p>");
			foreach (var key in plan.Impact.LimitKeys) html.Append("<p>").Append(T(key)).Append("</p>");
			foreach (var step in plan.Steps)
			{
				html.Append("<section><h2>").Append(T(step.LabelKey)).Append("</h2><p>").Append(E(step.CurrentValue)).Append(" → ").Append(E(step.ProposedValue)).Append(" · ").Append(T("Plan.State." + step.State)).Append("</p><p>").Append(T(step.RationaleKey)).Append("</p><p>").Append(T(step.InstructionsKey)).Append("</p><p>").Append(T(step.Impact.Profile.OperationKey)).Append("</p><p>").Append(T(step.Impact.Profile.TimingKey)).Append("</p><p>").Append(T(step.RollbackKey)).Append("</p><p>").Append(E(step.Destination)).Append("</p>");
				html.Append("<p>").Append(T("Plan.Prerequisites")).Append(": ").Append(E(string.Join(", ", step.Change.Prerequisites))).Append("</p>");
				foreach (var key in step.Impact.LimitKeys) html.Append("<p>").Append(T(key)).Append("</p>");
				foreach (var rule in step.Impact.RuleChanges) html.Append("<p>").Append(T(rule.TitleKey)).Append(": ").Append(T("Ui." + rule.Before)).Append(" → ").Append(T("Ui." + rule.After)).Append("</p>");
				foreach (var metric in step.Impact.Metrics) html.Append("<p>").Append(T(metric.LabelKey)).Append(": ").Append(E(metric.Before?.ToString(culture) ?? "Unknown")).Append(" → ").Append(E(metric.After?.ToString(culture) ?? "Unknown")).Append("</p>");
				html.Append("<p>").Append(T("Plan.Saved")).Append(": ").Append(T("Plan." + step.Verification.Saved)).Append(" · ").Append(T("Plan.Rules")).Append(": ").Append(T("Plan." + step.Verification.Rule)).Append(" · ").Append(T("Plan.Propagated")).Append(": ").Append(T("Plan." + step.Verification.Propagated)).Append(" · ").Append(T("Plan.BehaviorTested")).Append(": ").Append(T("Plan." + step.Verification.BehaviorTested)).Append("</p></section>");
			}
			html.Append("<h2>").Append(T("Plan.Communication")).Append("</h2><p>").Append(T(plan.Impact.CommunicationDraftKey)).Append("</p>");
			return html.Append("</body></html>").ToString();
		}
		private async Task AuditAsync(AdminAssistActor actor, string id, string action, CancellationToken ct) => await audit.SaveAuditLogAsync(new AuditLog
		{
			DepartmentId = actor.DepartmentId,
			ObjectDepartmentId = actor.DepartmentId,
			UserId = actor.UserId,
			ObjectId = id,
			LogType = (int)AuditLogTypes.AdminAssistPlanAccess,
			LoggedOn = Now,
			Successful = true,
			Message = action,
			Data = JsonSerializer.Serialize(new { planId = id, action, stage = "AccessAuthorized", version = ChangePlanPolicy.Version })
		}, ct);
	}
}
