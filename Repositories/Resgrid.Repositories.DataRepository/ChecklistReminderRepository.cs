using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	public sealed class ChecklistReminderRepository : RmsRepositoryBase<ChecklistDefinition>, IChecklistReminderRepository
	{
		public ChecklistReminderRepository(IConnectionProvider connection, SqlConfiguration config, IUnitOfWork uow, IQueryFactory queries) : base(connection, config, uow, queries) { }
		private static readonly string[] Columns = typeof(ChecklistReminder).GetProperties().Select(p => p.Name).ToArray();
		public async Task<List<ChecklistReminder>> ForRecipientAsync(int departmentId, string userId, int skip, CancellationToken ct = default)
		{
			if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
			return (await QueryAsync<ChecklistReminder>($"SELECT {Cols(Columns.Where(c => c != "ClaimToken").ToArray())} FROM {Tbl("ChecklistReminders")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("RecipientUserId")}={P}UserId ORDER BY {Col("CreatedOnUtc")},{Col("Id")} {Paging()}", new { DepartmentId = departmentId, UserId = userId, Skip = skip, Take = 100 }, ct)).ToList();
		}
		public async Task<List<int>> DepartmentsAsync(int afterDepartmentId, CancellationToken ct = default) => (await QueryAsync<int>(
			$"SELECT {Col("DepartmentId")} FROM (SELECT {Col("DepartmentId")} FROM {Tbl("DepartmentChecklistSettings")} WHERE {Col("RemindersEnabled")}={P}Enabled UNION SELECT {Col("DepartmentId")} FROM {Tbl("ChecklistReminders")} WHERE {Col("Status")}=0) d WHERE {Col("DepartmentId")}>{P}After ORDER BY {Col("DepartmentId")} {Paging()}", new { Enabled = true, After = afterDepartmentId, Skip = 0, Take = 100 }, ct)).ToList();
		public async Task EnqueueAsync(ChecklistReminder reminder, CancellationToken ct = default)
		{
			RequireTransaction();
			if (await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("ChecklistReminders")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("OccurrenceId")}={P}OccurrenceId AND {Col("RecipientUserId")}={P}RecipientUserId AND {Col("Kind")}={P}Kind AND {Col("PeriodKey")}={P}PeriodKey", reminder, ct) > 0) return;
			await ExecuteAsync($"INSERT INTO {Tbl("ChecklistReminders")} ({Cols(Columns)}) VALUES ({string.Join(",", Columns.Select(c => P + c))})", reminder, ct);
		}
		public async Task<List<ChecklistReminder>> ClaimAsync(int departmentId, DateTime now, bool digest, CancellationToken ct = default)
		{
			RequireTransaction();
			var predicate = $"{Col("DepartmentId")}={P}DepartmentId AND {Col("Status")}=0 AND {Col("NextAttemptUtc")}<={P}Now AND ({Col("ClaimUntilUtc")} IS NULL OR {Col("ClaimUntilUtc")}<={P}Now)";
			var first = await QueryFirstOrDefaultAsync<ChecklistReminder>($"SELECT {Cols(Columns)} FROM {Tbl("ChecklistReminders")} WHERE {predicate} ORDER BY {Col("NextAttemptUtc")},{Col("Id")} {Paging()}", new { DepartmentId = departmentId, Now = now, Skip = 0, Take = 1 }, ct);
			if (first == null) return new List<ChecklistReminder>();
			var rows = digest ? (await QueryAsync<ChecklistReminder>($"SELECT {Cols(Columns)} FROM {Tbl("ChecklistReminders")} WHERE {predicate} AND {Col("RecipientUserId")}={P}Recipient ORDER BY {Col("NextAttemptUtc")},{Col("Id")} {Paging()}", new { DepartmentId = departmentId, Now = now, Recipient = first.RecipientUserId, Skip = 0, Take = 500 }, ct)).ToList() : new List<ChecklistReminder> { first };
			var token = Guid.NewGuid().ToString("D");
			foreach (var row in rows)
			{
				row.ClaimToken = token; row.ClaimUntilUtc = now.AddMinutes(30); row.Attempts++;
				await ExecuteAsync($"UPDATE {Tbl("ChecklistReminders")} SET {Col("ClaimToken")}={P}ClaimToken,{Col("ClaimUntilUtc")}={P}ClaimUntilUtc,{Col("Attempts")}={P}Attempts WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id", row, ct);
			}
			return rows;
		}
		public async Task FinishAsync(ChecklistReminder reminder, ChecklistReminderStatus status, DateTime now, CancellationToken ct = default)
		{
			RequireTransaction();
			var changed = await ExecuteAsync($"UPDATE {Tbl("ChecklistReminders")} SET {Col("Status")}={P}Status,{Col("NextAttemptUtc")}={P}Next,{Col("CompletedOnUtc")}={P}Completed,{Col("ClaimToken")}=NULL,{Col("ClaimUntilUtc")}=NULL WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id AND {Col("ClaimToken")}={P}ClaimToken AND {Col("Status")}=0",
				new { reminder.DepartmentId, reminder.Id, reminder.ClaimToken, Status = (int)status, Next = now.AddMinutes(5), Completed = status == ChecklistReminderStatus.Pending ? (DateTime?)null : now }, ct);
			if (changed != 1) throw new InvalidOperationException("Checklist reminder lease was lost.");
		}
		private void RequireTransaction() { if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Checklist reminder writes require a department transaction."); }
	}
}
