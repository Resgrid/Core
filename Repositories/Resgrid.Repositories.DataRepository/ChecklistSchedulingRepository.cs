using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;

namespace Resgrid.Repositories.DataRepository
{
	public partial class ChecklistRepository
	{
		public async Task LockAccessFenceAsync(CancellationToken ct = default)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Checklist access changes require a transaction.");
			var sql = Config.DataConfig.DatabaseType == Config.DatabaseTypes.Postgres
				? $"SELECT id FROM {Tbl("ChecklistAccessFence")} WHERE id=1 FOR UPDATE" : $"SELECT [Id] FROM {Tbl("ChecklistAccessFence")} WITH (UPDLOCK,HOLDLOCK) WHERE [Id]=1";
			if (await ScalarAsync<int>(sql, null, ct) != 1) throw new InvalidOperationException("Checklist access fence is unavailable.");
		}
		public async Task ApplyAccessStateAsync(int departmentId, bool enabled, DateTime nowUtc, bool inventoryEnabled = true, CancellationToken ct = default)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Checklist access changes require a transaction.");
			foreach (var schedule in await QueryAsync<ChecklistSchedule>($"SELECT {Col("Id")},{Col("IsSuspended")},{Col("TargetType")} FROM {Tbl("ChecklistSchedules")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("IsActive")}={P}Active", new { DepartmentId = departmentId, Active = true }, ct))
			{
				var available = enabled && (schedule.TargetType != 5 || inventoryEnabled);
				if (schedule.IsSuspended == !available) continue;
				await CancelUnstartedOccurrencesAsync(departmentId, schedule.Id, null, nowUtc, ct);
				var resume = available ? $",{Col("Revision")}={Col("Revision")}+1,{Col("ActiveFromUtc")}={P}Now,{Col("GeneratedThroughUtc")}={P}Now,{Col("LastSweepUtc")}={P}Now" : "";
				// Operational metadata only. Existing protected Content is never read or rewritten.
				await ExecuteAsync($"UPDATE {Tbl("ChecklistSchedules")} SET {Col("IsSuspended")}={P}Suspended,{Col("UpdatedOn")}={P}Now{resume} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Id")}={P}Id", new { DepartmentId = departmentId, schedule.Id, Suspended = !available, Now = nowUtc }, ct);
			}
		}
		public async Task<List<ChecklistOccurrence>> CalendarOccurrencesAsync(int departmentId, DateTime fromUtc, DateTime untilUtc, int skip, CancellationToken ct = default) => (await QueryAsync<ChecklistOccurrence>(
			$"SELECT {Cols(Columns<ChecklistOccurrence>())} FROM {Tbl("ChecklistOccurrences")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("ScheduleId")} IS NOT NULL AND {Col("PeriodStartUtc")}<{P}Until AND {Col("WindowEndUtc")}>{P}From AND {Col("State")}<>6 ORDER BY {Col("PeriodStartUtc")},{Col("Id")} {Paging()}", new { DepartmentId = departmentId, From = fromUtc, Until = untilUtc, Skip = skip, Take = 500 }, ct)).ToList();
		public async Task<List<ChecklistShiftStart>> ShiftStartsAsync(int departmentId, DateTime fromUtc, DateTime untilUtc, CancellationToken ct = default) => (await QueryAsync<ChecklistShiftStart>(
			$"SELECT d.{Col("WorkshiftDayId")},d.{Col("Day")} AS {Col("StartUtc")},u.{Col("UnitId")} FROM {Tbl("WorkshiftDays")} d INNER JOIN {Tbl("Workshifts")} s ON s.{Col("WorkshiftId")}=d.{Col("WorkshiftId")} INNER JOIN {Tbl("WorkshiftEntities")} e ON e.{Col("WorkshiftId")}=s.{Col("WorkshiftId")} INNER JOIN {Tbl("Units")} u ON e.{Col("BackingId")}=CAST(u.{Col("UnitId")} AS VARCHAR(20)) AND u.{Col("DepartmentId")}=s.{Col("DepartmentId")} WHERE s.{Col("DepartmentId")}={P}DepartmentId AND s.{Col("DeletedOn")} IS NULL AND s.{Col("Type")}=1 AND d.{Col("Day")}>{P}From AND d.{Col("Day")}<={P}Until ORDER BY d.{Col("Day")}", new { DepartmentId = departmentId, From = fromUtc, Until = untilUtc }, ct)).ToList();
		public Task AdvanceDigestSweepAsync(int departmentId, DateTime nowUtc, CancellationToken ct = default)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Checklist digest checkpoints require a transaction.");
			return ExecuteAsync($"UPDATE {Tbl("DepartmentChecklistSettings")} SET {Col("LastDigestSweepUtc")}={P}Now WHERE {Col("DepartmentId")}={P}DepartmentId", new { DepartmentId = departmentId, Now = nowUtc }, ct);
		}
	}
}
