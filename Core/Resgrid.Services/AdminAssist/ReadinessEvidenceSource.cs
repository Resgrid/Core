using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Read-only summaries through the source's permissions and protection gates. No automatic sends or retries.</summary>
	public sealed class ReadinessEvidenceSource(IChecklistsService checklists, IWorkOrdersService orders,
		IWorkOrderReportingService orderReports, IInventoryOperationsService inventory, IWorkflowService workflows,
		IRmsOperationalRecordsRepository records, IRecordsAuthorizationService authorization, IFeatureToggleService flags,
		IAuthorizationService sourceAuthorization) : IAdminAssistEvidenceSource
	{
		public string SourceId => "Readiness";
		public IReadOnlyList<string> EvidenceIds { get; } = new[] { "overdueChecklistCount", "activeSafetyHoldCount", "stockExpiring30Days", "stockShortageCount", "failedWorkflowCount", "overdueRecordReviewCount" };
		public async Task<IReadOnlyList<ConfigurationEvidence>> ReadAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var result = new List<ConfigurationEvidence>();
			var limit = Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000);
			var checklistActor = new ChecklistActor { DepartmentId = actor.DepartmentId, UserId = actor.UserId };
			var inventoryActor = new InventoryActor { DepartmentId = actor.DepartmentId, UserId = actor.UserId };
			async Task Read(string id, Func<Task<int>> read)
			{
				ct.ThrowIfCancellationRequested();
				try { result.Add(new ConfigurationEvidence(id, EvidenceState.Known, SourceId, "1", now, Number: await read().WaitAsync(ct))); }
				catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
				catch (UnauthorizedAccessException) { result.Add(new ConfigurationEvidence(id, EvidenceState.Redacted, SourceId, "1", now, ReasonCode: "SourceAccessUnavailable")); }
				catch (Exception) { result.Add(new ConfigurationEvidence(id, EvidenceState.Unknown, SourceId, "1", now, ReasonCode: "SourceUnavailable")); }
			}
			await Read("overdueChecklistCount", async () =>
			{
				var summary = await checklists.GetComplianceSummaryAsync(checklistActor, new ChecklistReportQuery { FromUtc = now.AddDays(-30), UntilUtc = now });
				if (summary == null || summary.UnavailableSources.Count > 0 || summary.Entries.Count > limit) throw new InvalidOperationException("Incomplete checklist evidence.");
				if (summary.IsRedacted) throw new UnauthorizedAccessException();
				return summary.Entries.Count(e => e.Expected && !e.Completed && !e.Skipped && e.DueUtc < now);
			});
			await Read("activeSafetyHoldCount", async () =>
			{
				var holds = new HashSet<string>(StringComparer.Ordinal);
				int rows = 0;
				for (int page = 0; ; page++)
				{
					var list = await orders.ListAsync(checklistActor, new WorkOrderFilter { Page = page }) ?? throw new InvalidOperationException("Orders unavailable.");
					rows += list.Items.Count;
					if (rows > limit) throw new InvalidOperationException("Row bound exceeded.");
					foreach (var order in list.Items)
					{
						ct.ThrowIfCancellationRequested();
						var evidence = await orderReports.GetWorkOrderHoldsAsync(checklistActor, order.Id);
						if (evidence == null || evidence.NextAfterId != null) throw new InvalidOperationException("Incomplete hold evidence.");
						foreach (var hold in evidence.Items.Where(h => !h.Hold.ReleasedOn.HasValue)) holds.Add(hold.Hold.Id);
					}
					if (!list.HasMore) break;
					if (list.Items.Count == 0) throw new InvalidOperationException("Invalid page.");
				}
				return holds.Count;
			});
			await Read("stockExpiring30Days", async () =>
			{
				var report = await inventory.BuildReportAsync(inventoryActor, new InventoryReportInput { Kind = InventoryReportKind.Expiration, FromUtc = now, UntilUtc = now.AddDays(30) });
				if (report?.Rows == null || report.Rows.Count > limit) throw new InvalidOperationException("Incomplete stock evidence.");
				return report.Rows.Count;
			});
			await Read("stockShortageCount", async () =>
			{
				var report = await inventory.BuildReportAsync(inventoryActor, new InventoryReportInput { Kind = InventoryReportKind.LowStock });
				if (report?.Rows == null || report.Rows.Count > limit) throw new InvalidOperationException("Incomplete stock evidence.");
				return report.Rows.Count;
			});
			await Read("failedWorkflowCount", async () =>
			{
				if (!await sourceAuthorization.CanUserViewWorkflowRunsAsync(actor.UserId, actor.DepartmentId)) throw new UnauthorizedAccessException();
				int count = 0, rows = 0;
				for (int page = 1; ; page++)
				{
					var runs = await workflows.GetRunsByDepartmentIdAsync(actor.DepartmentId, page, 100, ct) ?? throw new InvalidOperationException("Runs unavailable.");
					rows += runs.Count;
					if (rows > limit) throw new InvalidOperationException("Row bound exceeded.");
					count += runs.Count(r => r.DepartmentId == actor.DepartmentId && r.StartedOn >= now.AddDays(-7) && r.StartedOn <= now && r.Status == (int)WorkflowRunStatus.Failed);
					if (runs.Count < 100 || runs.All(r => r.StartedOn < now.AddDays(-7))) break;
				}
				return count;
			});
			await Read("overdueRecordReviewCount", async () =>
			{
				if ((await flags.EvaluateFreshAsync(FeatureFlagKeys.RecordsSystem, actor.DepartmentId))?.IsEnabled != true) throw new InvalidOperationException("Records unavailable.");
				if (!await authorization.HasPermissionAsync(actor.UserId, actor.DepartmentId, PermissionTypes.ReviewRecords)) throw new UnauthorizedAccessException();
				var rows = (await records.GetOpenAsync(actor.DepartmentId))?.ToList() ?? throw new InvalidOperationException("Records unavailable.");
				if (rows.Count > limit) throw new InvalidOperationException("Row bound exceeded.");
				int count = 0;
				foreach (var row in rows.Where(r => r.DepartmentId == actor.DepartmentId && r.State == (int)RmsRecordState.ReadyForReview && r.ReviewDueOn < now))
				{
					if (!await authorization.CanUserViewRecordAsync(actor.UserId, row.RmsOperationalRecordId, actor.DepartmentId)) throw new UnauthorizedAccessException();
					count++;
				}
				return count;
			});
			return result;
		}
	}
}
