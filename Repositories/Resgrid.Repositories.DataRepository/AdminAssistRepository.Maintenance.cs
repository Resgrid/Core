using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Repositories.DataRepository
{
	public sealed partial class AdminAssistRepository
	{
		public async Task<IReadOnlyList<int>> GetDueDepartmentsAsync(DateTime nowUtc, int take, CancellationToken ct) =>
			(await QueryAsync<int>($"SELECT w.{Col("DepartmentId")} FROM (SELECT {Col("DepartmentId")},MIN({Col("ModifiedOn")}) AS {Col("ModifiedOn")} FROM (SELECT {Col("DepartmentId")},{Col("ModifiedOn")} FROM {Tbl("AdminAssistWorkspaces")} UNION ALL SELECT {Col("DepartmentId")},{Col("ModifiedOnUtc")} AS {Col("ModifiedOn")} FROM {Tbl("AdminAssistConversations")} UNION ALL SELECT {Col("DepartmentId")},{Col("ExpiresOnUtc")} AS {Col("ModifiedOn")} FROM {Tbl("AiUsageLedger")} UNION ALL SELECT {Col("DepartmentId")},{Col("UpdatedOnUtc")} AS {Col("ModifiedOn")} FROM {Tbl("AdminAssistPlans")} UNION ALL SELECT {Col("DepartmentId")},{Col("CreatedOnUtc")} AS {Col("ModifiedOn")} FROM {Tbl("AdminAssistDiagnosticRuns")} UNION ALL SELECT {Col("DepartmentId")},{Col("ExpiresOnUtc")} AS {Col("ModifiedOn")} FROM {Tbl("AdminAssistDiagnosticLeases")}) scopes GROUP BY {Col("DepartmentId")}) w INNER JOIN {Tbl("Departments")} d ON d.{Col("DepartmentId")}=w.{Col("DepartmentId")} " +
				$"LEFT JOIN {Tbl("AdminAssistWorkerStates")} s ON s.{Col("DepartmentId")}=w.{Col("DepartmentId")} " +
				$"WHERE (s.{Col("LeaseExpiresOn")} IS NULL OR s.{Col("LeaseExpiresOn")}<={P}Now) AND (s.{Col("LastAttemptOn")} IS NULL OR s.{Col("LastAttemptOn")}<{P}Due) " +
				$"ORDER BY COALESCE(s.{Col("LastAttemptOn")},w.{Col("ModifiedOn")}),w.{Col("DepartmentId")} {Paging()}",
				new { Now = DatabaseTimestamp(nowUtc), Due = DatabaseTimestamp(nowUtc.AddHours(-1)), Skip = 0, Take = Math.Clamp(take, 1, 50) }, ct)).ToArray();

		private async Task<T> MaintenanceTransactionAsync<T>(int departmentId, Func<Task<T>> action, CancellationToken ct)
		{
			if (UnitOfWork.Transaction != null) throw new InvalidOperationException("Maintenance commands own their transaction.");
			await UnitOfWork.CreateOrGetConnectionAsync(ct);
			try { await LockConfigurationAsync(departmentId, ct); var result = await action(); UnitOfWork.CommitChanges(); return result; }
			catch { UnitOfWork.DiscardChanges(); throw; }
		}
		public Task<bool> TryLeaseAsync(int departmentId, string lease, DateTime nowUtc, CancellationToken ct) => MaintenanceTransactionAsync(departmentId, async () =>
		{
			var args = new { DepartmentId = departmentId, Lease = lease, Now = DatabaseTimestamp(nowUtc), Until = DatabaseTimestamp(nowUtc.AddMinutes(20)) };
			var exists = await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("AdminAssistWorkerStates")} WHERE {Col("DepartmentId")}={P}DepartmentId", args, ct);
			if (exists == 0) await ExecuteAsync($"INSERT INTO {Tbl("AdminAssistWorkerStates")} ({Col("DepartmentId")}) VALUES ({P}DepartmentId)", args, ct);
			return await ExecuteAsync($"UPDATE {Tbl("AdminAssistWorkerStates")} SET {Col("LeaseOwner")}={P}Lease,{Col("LeaseExpiresOn")}={P}Until " +
				$"WHERE {Col("DepartmentId")}={P}DepartmentId AND ({Col("LeaseExpiresOn")} IS NULL OR {Col("LeaseExpiresOn")}<={P}Now)", args, ct) == 1;
		}, ct);
		public async Task CompleteLeaseAsync(int departmentId, string lease, DateTime? evaluatedOn, CancellationToken ct) =>
			await MaintenanceTransactionAsync(departmentId, () => ExecuteAsync($"UPDATE {Tbl("AdminAssistWorkerStates")} SET {Col("LeaseOwner")}=NULL,{Col("LeaseExpiresOn")}=NULL," +
				$"{Col("LastAttemptOn")}={P}AttemptOn,{Col("LastEvaluatedOn")}=COALESCE({P}EvaluatedOn,{Col("LastEvaluatedOn")}) WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("LeaseOwner")}={P}Lease",
				new { DepartmentId = departmentId, Lease = lease, AttemptOn = DatabaseTimestamp(DateTime.UtcNow), EvaluatedOn = evaluatedOn.HasValue ? DatabaseTimestamp(evaluatedOn.Value) : (DateTime?)null }, ct), ct);
		public async Task<AdminAssistWorkerStatus> GetWorkerStatusAsync(int departmentId, CancellationToken ct)
		{
			var row = await QueryFirstOrDefaultAsync<WorkerDates>($"SELECT {Cols("LastEvaluatedOn", "LastDigestOn")} FROM {Tbl("AdminAssistWorkerStates")} WHERE {Col("DepartmentId")}={P}DepartmentId", new { DepartmentId = departmentId }, ct);
			DateTime? Utc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
			return new AdminAssistWorkerStatus(Utc(row?.LastEvaluatedOn), Utc(row?.LastDigestOn));
		}
		public async Task<AdminAssistPreferences> GetPreferencesAsync(int departmentId, string userId, CancellationToken ct) =>
			await QueryFirstOrDefaultAsync<AdminAssistPreferences>($"SELECT * FROM {Tbl("AdminAssistPreferences")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId", new { DepartmentId = departmentId, UserId = userId }, ct)
			?? new AdminAssistPreferences { DepartmentId = departmentId, UserId = userId };
		public async Task<IReadOnlyList<AdminAssistPreferences>> GetDigestPreferencesAsync(int departmentId, CancellationToken ct)
		{
			var cursor = await ScalarAsync<string>($"SELECT {Col("DigestCursor")} FROM {Tbl("AdminAssistWorkerStates")} WHERE {Col("DepartmentId")}={P}DepartmentId", new { DepartmentId = departmentId }, ct);
			async Task<AdminAssistPreferences[]> Page(string after) => (await QueryAsync<AdminAssistPreferences>(
				$"SELECT * FROM {Tbl("AdminAssistPreferences")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("DigestEnabled")}={P}Enabled " +
				$"AND ({P}After IS NULL OR {Col("UserId")}>{P}After) ORDER BY {Col("UserId")} {Paging()}", new { DepartmentId = departmentId, Enabled = true, After = after, Skip = 0, Take = 50 }, ct)).ToArray();
			var rows = await Page(cursor);
			if (rows.Length == 0 && cursor != null)
			{
				await AdvanceDigestCursorAsync(departmentId, null, ct);
				rows = await Page(null);
			}
			return rows;
		}
		public async Task AdvanceDigestCursorAsync(int departmentId, string userId, CancellationToken ct)
		{
			if (userId?.Length > 128) throw new ArgumentException("Invalid digest cursor.");
			await MaintenanceTransactionAsync(departmentId, () => ExecuteAsync($"UPDATE {Tbl("AdminAssistWorkerStates")} SET {Col("DigestCursor")}={P}UserId WHERE {Col("DepartmentId")}={P}DepartmentId",
				new { DepartmentId = departmentId, UserId = userId }, ct), ct);
		}
		public async Task SavePreferencesAsync(AdminAssistActor actor, AdminAssistPreferencesCommand command, CancellationToken ct)
		{
			if (command == null || command.ExpectedRevision < 0 || command.QuietStartHour is < 0 or > 23 || command.QuietEndHour is < 0 or > 23) throw new ArgumentException("Invalid preferences.");
			await MaintenanceTransactionAsync(actor.DepartmentId, async () =>
			{
				var current = await GetPreferencesAsync(actor.DepartmentId, actor.UserId, ct);
				if (current.Revision != command.ExpectedRevision) throw new AdminAssistConcurrencyException();
				var args = new { actor.DepartmentId, actor.UserId, command.DigestEnabled, command.QuietStartHour, command.QuietEndHour,
					Locale = (actor.Locale ?? "en").Split('-')[0], Revision = command.ExpectedRevision + 1 };
				if (current.Revision == 0)
					await ExecuteAsync($"INSERT INTO {Tbl("AdminAssistPreferences")} ({Cols("DepartmentId", "UserId", "DigestEnabled", "QuietStartHour", "QuietEndHour", "Locale", "Revision")}) VALUES ({P}DepartmentId,{P}UserId,{P}DigestEnabled,{P}QuietStartHour,{P}QuietEndHour,{P}Locale,{P}Revision)", args, ct);
				else await ExecuteAsync($"UPDATE {Tbl("AdminAssistPreferences")} SET {Col("DigestEnabled")}={P}DigestEnabled,{Col("QuietStartHour")}={P}QuietStartHour,{Col("QuietEndHour")}={P}QuietEndHour,{Col("Locale")}={P}Locale,{Col("Revision")}={P}Revision WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId", args, ct);
				return true;
			}, ct);
		}
		public Task<bool> ClaimDigestAsync(AdminAssistPreferences preference, string week, DateTime nowUtc, CancellationToken ct) => MaintenanceTransactionAsync(preference.DepartmentId, async () =>
		{
			// Reserve before provider handoff. Ambiguous provider failures are never automatically resent.
			var args = new { preference.DepartmentId, preference.UserId, preference.Revision, Week = week, Now = DatabaseTimestamp(nowUtc), Enabled = true };
			return await ExecuteAsync($"UPDATE {Tbl("AdminAssistPreferences")} SET {Col("LastAttemptWeek")}={P}Week,{Col("LastAttemptOn")}={P}Now,{Col("LastAttemptOutcome")}='HandoffUnconfirmed' " +
				$"WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId AND {Col("Revision")}={P}Revision AND {Col("DigestEnabled")}={P}Enabled AND ({Col("LastAttemptWeek")} IS NULL OR {Col("LastAttemptWeek")}<>{P}Week)", args, ct) == 1;
		}, ct);
		public async Task CompleteDigestAsync(int departmentId, string userId, string week, string outcome, DateTime nowUtc, CancellationToken ct)
		{
			if (outcome != "HandedOff" && outcome != "HandoffUnconfirmed" && outcome != "Suppressed") throw new ArgumentException("Invalid digest outcome.");
			await MaintenanceTransactionAsync(departmentId, async () =>
			{
				var args = new { DepartmentId = departmentId, UserId = userId, Week = week, Outcome = outcome, Now = DatabaseTimestamp(nowUtc) };
				await ExecuteAsync($"UPDATE {Tbl("AdminAssistPreferences")} SET {Col("LastAttemptOutcome")}={P}Outcome WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId AND {Col("LastAttemptWeek")}={P}Week", args, ct);
				return await ExecuteAsync($"UPDATE {Tbl("AdminAssistWorkerStates")} SET {Col("LastDigestOn")}={P}Now WHERE {Col("DepartmentId")}={P}DepartmentId", args, ct);
			}, ct);
		}
		public Task<int> PurgeExpiredMetadataAsync(int departmentId, DateTime nowUtc, CancellationToken ct) => MaintenanceTransactionAsync(departmentId, async () =>
		{
			// The owning RMS hold fence takes the same department lock. Conservatively retain all derived
			// evidence while any department hold is active, including encrypted hold membership.
			if (await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("RmsRecordLegalHolds")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("ReleasedOn")} IS NULL", new { DepartmentId = departmentId }, ct) > 0) return 0;
			var deleted = 0;
			Task<int> DeleteWhere(string table, string where, object args)
			{
				var sql = IsPostgres ? $"DELETE FROM {Tbl(table)} WHERE ctid IN (SELECT ctid FROM {Tbl(table)} WHERE {where} LIMIT 500)" : $"DELETE TOP (500) FROM {Tbl(table)} WHERE {where}";
				return ExecuteAsync(sql, args, ct);
			}
			foreach (var (table, column, days) in new[] { ("AdminAssistDailySummaries", "DayUtc", AdminAssistConfig.AggregateRetentionDays), ("AdminAssistLearning", "ModifiedOn", AdminAssistConfig.PersonalLearningRetentionDays), ("AdminAssistDispatchTraces", "OccurredOn", AdminAssistConfig.TraceRetentionDays), ("AiGenerations", "CreatedOnUtc", Resgrid.Config.AiConfig.ConversationRetentionDays), ("AdminAssistDiagnosticRuns", "CreatedOnUtc", Math.Clamp(AdminAssistConfig.DiagnosticRetentionDays, 1, 30)) })
			{
				var where = $"{Col("DepartmentId")}={P}DepartmentId AND {Col(column)}<{P}Before";
				deleted += await DeleteWhere(table, where, new { DepartmentId = departmentId, Before = DatabaseTimestamp(nowUtc.AddDays(-Math.Clamp(days, 1, 3650))) });
			}
			deleted += await DeleteWhere("AdminAssistPlans", $"{Col("DepartmentId")}={P}DepartmentId AND ({Col("Deleted")}={P}True OR {Col("ClosedOnUtc")}<{P}Before)", new { DepartmentId = departmentId, True = true, Before = DatabaseTimestamp(nowUtc.AddDays(-Math.Clamp(AdminAssistConfig.ClosedPlanRetentionDays, 1, 90))) });
			deleted += await DeleteWhere("AdminAssistDiagnosticRuns", $"{Col("DepartmentId")}={P}DepartmentId AND {Col("Deleted")}={P}True", new { DepartmentId = departmentId, True = true });
			deleted += await DeleteWhere("AdminAssistDiagnosticLeases", $"{Col("DepartmentId")}={P}DepartmentId AND {Col("ExpiresOnUtc")}<={P}Now", new { DepartmentId = departmentId, Now = DatabaseTimestamp(nowUtc) });
			var personalActions = $"{Col("Action")} IN ('learn','interest','dismiss')";
			deleted += await DeleteWhere("AdminAssistHistory", $"{Col("DepartmentId")}={P}DepartmentId AND {personalActions} AND {Col("OccurredOnUtc")}<{P}Before",
				new { DepartmentId = departmentId, Before = DatabaseTimestamp(nowUtc.AddDays(-Math.Clamp(AdminAssistConfig.PersonalLearningRetentionDays, 1, 3650))) });
			deleted += await DeleteWhere("AiGenerations", $"{Col("DepartmentId")}={P}DepartmentId AND EXISTS (SELECT 1 FROM {Tbl("AdminAssistConversations")} c WHERE c.{Col("Id")}={Tbl("AiGenerations")}.{Col("ConversationId")} AND c.{Col("DepartmentId")}={P}DepartmentId AND c.{Col("Deleted")}={P}True)", new { DepartmentId = departmentId, True = true });
			foreach (var (table, userColumn) in new[] { ("AdminAssistLearning", "UserId"), ("AdminAssistPreferences", "UserId"), ("AdminAssistHistory", "ActorId"), ("AdminAssistPlans", "UserId"), ("AdminAssistDiagnosticRuns", "UserId"), ("AiGenerations", "UserId"), ("AdminAssistConversations", "UserId") })
			{
				var where = $"{Col("DepartmentId")}={P}DepartmentId AND " + (table == "AdminAssistHistory" ? personalActions + " AND " : "") +
					$"NOT EXISTS (SELECT 1 FROM {Tbl("DepartmentMembers")} m WHERE m.{Col("DepartmentId")}={P}DepartmentId AND m.{Col("UserId")}={Tbl(table)}.{Col(userColumn)} AND m.{Col("IsDeleted")}={P}NotDeleted)";
				deleted += await DeleteWhere(table, where, new { DepartmentId = departmentId, NotDeleted = false });
			}
			deleted += await DeleteWhere("AdminAssistConversations", $"{Col("DepartmentId")}={P}DepartmentId AND {Col("ModifiedOnUtc")}<{P}Before AND NOT EXISTS (SELECT 1 FROM {Tbl("AiGenerations")} g WHERE g.{Col("ConversationId")}={Tbl("AdminAssistConversations")}.{Col("Id")})",
				new { DepartmentId = departmentId, Before = DatabaseTimestamp(nowUtc.AddDays(-Math.Clamp(Resgrid.Config.AiConfig.ConversationRetentionDays, 1, 365))) });
			// Preserve a single actor-free first-answer marker so retention cannot restart the free starter allowance.
			var ledgerArgs = new { DepartmentId = departmentId, Month = nowUtc.AddMonths(-12).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), Skip = 0, Take = 1 };
			var firstAnswer = $"SELECT {Col("Id")} FROM {Tbl("AiUsageLedger")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Outcome")}='Answered' AND ({Col("Feature")}='AdminAssist' OR {Col("Feature")} IS NULL) AND {Col("CreatedOnUtc")} IS NOT NULL ORDER BY {Col("CreatedOnUtc")},{Col("Id")} {Paging()}";
			deleted += await DeleteWhere("AiUsageLedger", $"{Col("DepartmentId")}={P}DepartmentId AND {Col("Month")}<{P}Month AND {Col("Id")} NOT IN ({firstAnswer})", ledgerArgs);
			await ExecuteAsync($"UPDATE {Tbl("AiUsageLedger")} SET {Col("UserId")}='' WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Month")}<{P}Month AND {Col("Id")} IN ({firstAnswer})", ledgerArgs, ct);
			return deleted;
		}, ct);
		private sealed class WorkerDates { public DateTime? LastEvaluatedOn { get; set; } public DateTime? LastDigestOn { get; set; } }
	}
}
