using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Repositories.DataRepository
{
	public sealed partial class AdminAssistRepository : IAdminAssistPlanStore
	{
		public async Task<AdminAssistPlanRow> ReadPlanAsync(AdminAssistActor actor, string id, CancellationToken ct) =>
			UtcPlan((await QueryAsync<AdminAssistPlanRow>($"SELECT * FROM {Tbl("AdminAssistPlans")} WHERE {Col("Id")}={P}Id AND {Col("DepartmentId")}={P}DepartmentId AND ({Col("UserId")}={P}UserId OR {Col("Shared")}={P}True) AND {Col("Deleted")}={P}False", new { Id = id, actor.DepartmentId, actor.UserId, True = true, False = false }, ct)).SingleOrDefault());
		public async Task<IReadOnlyList<AdminAssistPlanRow>> ListPlansAsync(AdminAssistActor actor, DateTime closedAfter, CancellationToken ct) =>
			(await QueryAsync<AdminAssistPlanRow>($"SELECT * FROM {Tbl("AdminAssistPlans")} WHERE {Col("DepartmentId")}={P}DepartmentId AND ({Col("UserId")}={P}UserId OR {Col("Shared")}={P}True) AND {Col("Deleted")}={P}False AND ({Col("ClosedOnUtc")} IS NULL OR {Col("ClosedOnUtc")}>={P}ClosedAfter) ORDER BY {Col("UpdatedOnUtc")} DESC,{Col("Id")} {Paging()}", new { actor.DepartmentId, actor.UserId, True = true, False = false, ClosedAfter = DatabaseTimestamp(closedAfter), Skip = 0, Take = 20 }, ct)).Select(UtcPlan).ToArray();
		private static AdminAssistPlanRow UtcPlan(AdminAssistPlanRow row)
		{
			if (row == null) return null;
			row.CreatedOnUtc = DateTime.SpecifyKind(row.CreatedOnUtc, DateTimeKind.Utc); row.UpdatedOnUtc = DateTime.SpecifyKind(row.UpdatedOnUtc, DateTimeKind.Utc);
			if (row.ClosedOnUtc.HasValue) row.ClosedOnUtc = DateTime.SpecifyKind(row.ClosedOnUtc.Value, DateTimeKind.Utc); return row;
		}
		public Task SavePlanAsync(AdminAssistActor actor, AdminAssistPlanRow row, long expectedRevision, string expectedConfigurationRevision, long expectedScopeRevision, CancellationToken ct) => MaintenanceTransactionAsync(actor.DepartmentId, async () =>
		{
			if (row.DepartmentId != actor.DepartmentId || row.UserId != actor.UserId || row.Revision != expectedRevision + 1 || expectedRevision < 0 ||
				!Guid.TryParseExact(row.Id, "D", out _) || row.Content == null || row.Content.Length > 131072 ||
				(row.IsProtected ? !ProtectedDataEnvelope.HasEnvelopePrefix(row.Content) : !row.Content.StartsWith("enc2:", StringComparison.Ordinal))) throw new ArgumentException("Invalid protected plan write.");
			if ((await GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture) != expectedConfigurationRevision) throw new AdminAssistConcurrencyException();
			var workspace = await GetWorkspaceAsync(actor.DepartmentId, actor.UserId, "", ct);
			if (workspace.ScopeRevision != expectedScopeRevision) throw new AdminAssistConcurrencyException();
			var args = new
			{
				row.Id,
				row.DepartmentId,
				row.UserId,
				row.Revision,
				row.Status,
				row.Shared,
				row.Deleted,
				row.Content,
				row.IsProtected,
				row.ProtectedCatalogVersion,
				CreatedOnUtc = DatabaseTimestamp(row.CreatedOnUtc),
				UpdatedOnUtc = DatabaseTimestamp(row.UpdatedOnUtc),
				ClosedOnUtc = row.ClosedOnUtc.HasValue ? DatabaseTimestamp(row.ClosedOnUtc.Value) : (DateTime?)null,
				Expected = expectedRevision,
				False = false
			};
			if (expectedRevision == 0)
			{
				if (row.Deleted || row.Shared || row.ClosedOnUtc.HasValue || row.Status != "Proposed") throw new ArgumentException("Invalid new plan.");
				if (await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("AdminAssistPlans")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId AND {Col("ClosedOnUtc")} IS NULL AND {Col("Deleted")}={P}False", args, ct) >= 100) throw new ArgumentException("Close an existing plan first.");
				await ExecuteAsync($"INSERT INTO {Tbl("AdminAssistPlans")} ({Cols("Id", "DepartmentId", "UserId", "Revision", "Status", "Shared", "Deleted", "Content", "IsProtected", "ProtectedCatalogVersion", "CreatedOnUtc", "UpdatedOnUtc", "ClosedOnUtc")}) VALUES ({P}Id,{P}DepartmentId,{P}UserId,{P}Revision,{P}Status,{P}Shared,{P}Deleted,{P}Content,{P}IsProtected,{P}ProtectedCatalogVersion,{P}CreatedOnUtc,{P}UpdatedOnUtc,{P}ClosedOnUtc)", args, ct);
			}
			else if (await ExecuteAsync($"UPDATE {Tbl("AdminAssistPlans")} SET {Col("Revision")}={P}Revision,{Col("Status")}={P}Status,{Col("Shared")}={P}Shared,{Col("Deleted")}={P}Deleted,{Col("Content")}={P}Content,{Col("IsProtected")}={P}IsProtected,{Col("ProtectedCatalogVersion")}={P}ProtectedCatalogVersion,{Col("UpdatedOnUtc")}={P}UpdatedOnUtc,{Col("ClosedOnUtc")}={P}ClosedOnUtc WHERE {Col("Id")}={P}Id AND {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId AND {Col("Revision")}={P}Expected AND {Col("Deleted")}={P}False", args, ct) != 1) throw new AdminAssistConcurrencyException();
			return 0;
		}, ct);
	}
}
