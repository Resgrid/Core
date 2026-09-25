using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Repositories.DataRepository
{
	public sealed partial class AdminAssistRepository : IAdminAssistDiagnosticStore
	{
		public Task<bool> AcquireDiagnosticLeaseAsync(AdminAssistActor actor, string id, DateTime now, CancellationToken ct) => MaintenanceTransactionAsync(actor.DepartmentId, async () =>
		{
			var args = new { actor.DepartmentId, actor.UserId, Id = id, Now = DatabaseTimestamp(now), Expires = DatabaseTimestamp(now.AddSeconds(120)) };
			await ExecuteAsync($"DELETE FROM {Tbl("AdminAssistDiagnosticLeases")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("ExpiresOnUtc")}<={P}Now", args, ct);
			if (await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("AdminAssistDiagnosticLeases")} WHERE {Col("DepartmentId")}={P}DepartmentId", args, ct) >= 2 ||
				await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("AdminAssistDiagnosticLeases")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId", args, ct) > 0) return false;
			await ExecuteAsync($"INSERT INTO {Tbl("AdminAssistDiagnosticLeases")} ({Cols("Id", "DepartmentId", "UserId", "ExpiresOnUtc")}) VALUES ({P}Id,{P}DepartmentId,{P}UserId,{P}Expires)", args, ct);
			return true;
		}, ct);
		public async Task ReleaseDiagnosticLeaseAsync(AdminAssistActor actor, string id, CancellationToken ct) =>
			await ExecuteAsync($"DELETE FROM {Tbl("AdminAssistDiagnosticLeases")} WHERE {Col("Id")}={P}Id AND {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId", new { Id = id, actor.DepartmentId, actor.UserId }, ct);
		public Task SaveDiagnosticAsync(AdminAssistActor actor, AdminAssistDiagnosticRun row, CancellationToken ct) => MaintenanceTransactionAsync(actor.DepartmentId, async () =>
		{
			if (row.DepartmentId != actor.DepartmentId || row.UserId != actor.UserId || row.Revision != 1 || row.Deleted ||
				!Guid.TryParseExact(row.Id, "D", out _) || row.Content == null || row.Content.Length > 16384 ||
				(row.IsProtected ? !ProtectedDataEnvelope.HasEnvelopePrefix(row.Content) : !row.Content.StartsWith("enc2:", StringComparison.Ordinal))) throw new ArgumentException("Invalid diagnostic write.");
			await ExecuteAsync($"INSERT INTO {Tbl("AdminAssistDiagnosticRuns")} ({Cols("Id", "DepartmentId", "UserId", "Flow", "CreatedOnUtc", "Revision", "Deleted", "Content", "IsProtected", "ProtectedCatalogVersion")}) VALUES ({P}Id,{P}DepartmentId,{P}UserId,{P}Flow,{P}CreatedOnUtc,{P}Revision,{P}Deleted,{P}Content,{P}IsProtected,{P}ProtectedCatalogVersion)",
				new { row.Id, row.DepartmentId, row.UserId, row.Flow, CreatedOnUtc = DatabaseTimestamp(row.CreatedOnUtc), row.Revision, row.Deleted, row.Content, row.IsProtected, row.ProtectedCatalogVersion }, ct);
			return 0;
		}, ct);
		public async Task<AdminAssistDiagnosticRun> ReadDiagnosticAsync(AdminAssistActor actor, string id, CancellationToken ct)
		{
			var row = (await QueryAsync<AdminAssistDiagnosticRun>($"SELECT * FROM {Tbl("AdminAssistDiagnosticRuns")} WHERE {Col("Id")}={P}Id AND {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId AND {Col("Deleted")}={P}False", new { Id = id, actor.DepartmentId, actor.UserId, False = false }, ct)).SingleOrDefault();
			if (row != null) row.CreatedOnUtc = DateTime.SpecifyKind(row.CreatedOnUtc, DateTimeKind.Utc);
			return row;
		}
		public async Task<IReadOnlyList<DiagnosticRunSummary>> ListDiagnosticsAsync(AdminAssistActor actor, CancellationToken ct) =>
			(await QueryAsync<DiagnosticRunSummary>($"SELECT {Cols("Id", "Flow", "CreatedOnUtc", "Revision")} FROM {Tbl("AdminAssistDiagnosticRuns")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId AND {Col("Deleted")}={P}False ORDER BY {Col("CreatedOnUtc")} DESC,{Col("Id")} {Paging()}", new { actor.DepartmentId, actor.UserId, False = false, Skip = 0, Take = 20 }, ct)).Select(r => r with { CreatedOnUtc = DateTime.SpecifyKind(r.CreatedOnUtc, DateTimeKind.Utc) }).ToArray();
		public Task DeleteDiagnosticAsync(AdminAssistActor actor, DiagnosticRunCommand command, CancellationToken ct) => MaintenanceTransactionAsync(actor.DepartmentId, async () =>
		{
			if (await ExecuteAsync($"UPDATE {Tbl("AdminAssistDiagnosticRuns")} SET {Col("Deleted")}={P}True,{Col("Revision")}={Col("Revision")}+1 WHERE {Col("Id")}={P}Id AND {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId AND {Col("Revision")}={P}Revision AND {Col("Deleted")}={P}False", new { Id = command.RunId, actor.DepartmentId, actor.UserId, Revision = command.ExpectedRevision, True = true, False = false }, ct) != 1) throw new AdminAssistConcurrencyException();
			return 0;
		}, ct);
		public async Task<IReadOnlyList<AdminAssistDispatchTraceRow>> ReadDiagnosticTracesAsync(int departmentId, int callId, DateTime from, DateTime until, int bound, CancellationToken ct) =>
			(await QueryAsync<AdminAssistDispatchTraceRow>($"SELECT * FROM {Tbl("AdminAssistDispatchTraces")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("CallId")}={P}CallId AND {Col("OccurredOn")}>={P}From AND {Col("OccurredOn")}<{P}Until ORDER BY {Col("OccurredOn")},{Col("AdminAssistDispatchTraceId")} {Paging()}", new { DepartmentId = departmentId, CallId = callId, From = DatabaseTimestamp(from), Until = DatabaseTimestamp(until), Skip = 0, Take = Math.Clamp(bound, 1, 2000) + 1 }, ct)).ToArray();
		public async Task<IReadOnlyList<DiagnosticChange>> ReadDiagnosticChangesAsync(int departmentId, DateTime from, DateTime until, int bound, CancellationToken ct) =>
			(await QueryAsync<DiagnosticChange>($"SELECT {Col("AdminAssistHistoryId")} AS {Col("Id")},{Col("OccurredOnUtc")},{Col("SubjectId")} AS {Col("SettingId")},{Cols("Revision", "BeforeCode", "AfterCode", "CorrelationId")} FROM {Tbl("AdminAssistHistory")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Source")}='Configuration' AND {Col("OccurredOnUtc")}>={P}From AND {Col("OccurredOnUtc")}<{P}Until ORDER BY {Col("OccurredOnUtc")} DESC,{Col("AdminAssistHistoryId")} {Paging()}", new { DepartmentId = departmentId, From = DatabaseTimestamp(from), Until = DatabaseTimestamp(until), Skip = 0, Take = Math.Clamp(bound, 1, 100) + 1 }, ct)).Select(r => r with { OccurredOnUtc = DateTime.SpecifyKind(r.OccurredOnUtc, DateTimeKind.Utc) }).ToArray();
		public async Task<IReadOnlyList<DiagnosticStatusHeader>> ReadDiagnosticStatusesAsync(int departmentId, string memberId, int? unitId, DateTime from, DateTime until, CancellationToken ct)
		{
			var table = unitId.HasValue ? "UnitStates" : "ActionLogs";
			var scope = unitId.HasValue ? $"s.{Col("UnitId")}={P}UnitId AND EXISTS (SELECT 1 FROM {Tbl("Units")} u WHERE u.{Col("UnitId")}=s.{Col("UnitId")} AND u.{Col("DepartmentId")}={P}DepartmentId)" : $"s.{Col("DepartmentId")}={P}DepartmentId AND s.{Col("UserId")}={P}MemberId";
			// DestinationSource is not the writer of a status transition. Do not mislabel it as automation provenance.
			return (await QueryAsync<DiagnosticStatusHeader>($"SELECT s.{Col("Timestamp")},s.{Col(unitId.HasValue ? "State" : "ActionTypeId")} AS {Col("Status")},'Unrecorded' AS {Col("Source")} FROM {Tbl(table)} s WHERE {scope} AND s.{Col("Timestamp")}>={P}From AND s.{Col("Timestamp")}<{P}Until ORDER BY s.{Col("Timestamp")} DESC,s.{Col(unitId.HasValue ? "UnitStateId" : "ActionLogId")} DESC {Paging()}", new { DepartmentId = departmentId, MemberId = memberId, UnitId = unitId, From = DatabaseTimestamp(from), Until = DatabaseTimestamp(until), Skip = 0, Take = 101 }, ct)).Select(r => r with { Timestamp = DateTime.SpecifyKind(r.Timestamp, DateTimeKind.Utc) }).ToArray();
		}
	}
}
