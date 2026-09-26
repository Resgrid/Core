using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.AdminAssist;
using Resgrid.Config;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Services.AdminAssist
{
	public sealed class AdminAssistService(IAdminAssistAccessService access, IAdminAssistCatalog catalog,
		IConfigurationSnapshotProvider snapshots, IAdminAssistRepository repository, TimeProvider clock) : IAdminAssistService
	{
		public async Task<AdminAssistOverview> GetOverviewAsync(AdminAssistActor actor, bool setup, CancellationToken ct = default)
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(AdminAssistConfig.SnapshotTimeoutSeconds, 1, 60)));
			ct = timeout.Token;
			await RequireAccessAsync(actor, setup, ct);
			var workspace = CurrentCatalogOnly(await repository.GetWorkspaceAsync(actor.DepartmentId, actor.UserId, catalog.Version, ct).WaitAsync(ct));
			var snapshot = await snapshots.ReadAsync(actor, ct).WaitAsync(ct);
			var now = clock.GetUtcNow().UtcDateTime;
			var findings = catalog.Rules.Select(r => new ConfigurationRule(r).Evaluate(snapshot, now,
				TimeSpan.FromSeconds(Math.Clamp(AdminAssistConfig.EvidenceFreshnessSeconds, 1, 300)))).ToArray();
			var availability = await access.GetCapabilitiesAsync(actor, ct).WaitAsync(ct);
			await RequireAccessAsync(actor, setup, ct);
			var report = new ConfigurationReport(snapshot, findings, workspace.Areas.Where(a => a.Value == SetupAreaChoice.UseNow).Select(a => a.Key).ToArray());
			var assessments = catalog.Capabilities.Select(capability => CapabilitySetupEvaluator.Evaluate(capability, availability.SingleOrDefault(a => a.CapabilityId == capability.Id), report,
				clock.GetUtcNow().UtcDateTime, TimeSpan.FromSeconds(Math.Clamp(AdminAssistConfig.EvidenceFreshnessSeconds, 1, 300)))).ToArray();
			return new AdminAssistOverview(catalog.Version, workspace, report, availability, assessments,
				SetupPlanBuilder.Build(catalog, workspace, availability, assessments, snapshot.Revision, clock.GetUtcNow().UtcDateTime));
		}

		public async Task<SetupWorkspace> UpdateSetupAsync(AdminAssistActor actor, SetupProgressCommand command, CancellationToken ct = default)
		{
			await RequireAccessAsync(actor, true, ct);
			if (command == null || command.ExpectedRevision < 0 || command.ExpectedRevision == long.MaxValue || command.CatalogVersion != catalog.Version)
				throw new ArgumentException("A current catalog and workspace revision are required.");
			command = command with { ReviewEvidence = null }; // Never trust review counts supplied by a caller.
			bool valid = command.Operation switch
			{
				"area" => catalog.Areas.Any(a => a.Id == command.TargetId) && Enum.GetNames<SetupAreaChoice>().Contains(command.Choice) &&
					(command.Choice != nameof(SetupAreaChoice.NotApplicable) ? command.ReasonCode == null : Enum.GetNames<SetupAreaReason>().Contains(command.ReasonCode)),
				"mode" => command.TargetId == null && Enum.GetNames<SetupMode>().Contains(command.Choice),
				"learn" or "interest" => catalog.Capabilities.Any(c => c.Id == command.TargetId) && (command.Choice == "true" || command.Choice == "false"),
				"review" => command.TargetId == null && command.Choice == null && !string.IsNullOrWhiteSpace(command.EvidenceRevision),
				"revisit" => command.TargetId == null && command.Choice == null && (!command.RevisitOnUtc.HasValue ||
					command.RevisitOnUtc.Value.Kind == DateTimeKind.Utc && command.RevisitOnUtc > clock.GetUtcNow().UtcDateTime && command.RevisitOnUtc <= clock.GetUtcNow().UtcDateTime.AddDays(365)),
				"dismiss" => command.TargetId == null && (command.Choice == "true" || command.Choice == "false"),
				_ => false
			};
			if (!valid) throw new ArgumentException("Invalid setup choice.");
			// Baseline security applies regardless of scope or personal orientation progress.
			if (command.Operation == "area" && command.TargetId == "security" && command.Choice != nameof(SetupAreaChoice.UseNow))
				throw new ArgumentException("Baseline security cannot be removed from setup scope.");
			if (command.Operation == "review")
			{
				var overview = await GetOverviewAsync(actor, true, ct);
				if (!overview.Report.Snapshot.Consistent || overview.Report.Snapshot.Revision != command.EvidenceRevision) throw new AdminAssistConcurrencyException();
				command = command with { ReviewEvidence = new SetupReviewEvidence(catalog.Version, overview.Report.Snapshot.Revision,
					overview.Report.Snapshot.AsOfUtc, overview.Report.Required, overview.Report.Verified, overview.Report.Failed, overview.Report.Unknown, overview.Workspace.ScopeRevision) };
			}
			await RequireAccessAsync(actor, true, ct);
			return CurrentCatalogOnly(await repository.UpdateWorkspaceAsync(actor, command, ct));
		}

		/// <summary>Learning carries across catalog releases; choices for capabilities no longer in the catalog are not shown or counted.</summary>
		private SetupWorkspace CurrentCatalogOnly(SetupWorkspace workspace)
		{
			var current = catalog.Capabilities.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
			return workspace with
			{
				LearnedCapabilityIds = workspace.LearnedCapabilityIds.Where(current.Contains).ToArray(),
				InterestedCapabilityIds = workspace.InterestedCapabilityIds.Where(current.Contains).ToArray()
			};
		}

		public async Task<IReadOnlyList<AdminAssistHistoryItem>> GetHistoryAsync(AdminAssistActor actor, int skip, int take, CancellationToken ct = default)
		{
			await RequireAccessAsync(actor, false, ct);
			var history = await repository.GetHistoryAsync(actor.DepartmentId, actor.UserId, Math.Max(0, skip),
				Math.Clamp(take, 1, Math.Clamp(AdminAssistConfig.MaxHistoryPageSize, 1, 100)), ct);
			await RequireAccessAsync(actor, false, ct);
			return history;
		}

		private async Task RequireAccessAsync(AdminAssistActor actor, bool setup, CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			if (!await access.CanAccessAsync(actor, setup, ct).WaitAsync(ct)) throw new UnauthorizedAccessException();
		}
	}
}
