using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistWorkflowTests
	{
		internal sealed partial class MemoryStore
		{
			public List<ChecklistShiftStart> ShiftStarts = new();
			public Task LockAccessFenceAsync(CancellationToken ct = default) => Task.CompletedTask;
			public async Task ApplyAccessStateAsync(int departmentId, bool enabled, DateTime nowUtc, CancellationToken ct = default, bool inventoryEnabled = true)
			{
				foreach (var row in (await ListAsync<ChecklistSchedule>(departmentId)).Where(s => s.IsActive && s.IsSuspended != !(enabled && (s.TargetType != 5 || inventoryEnabled))))
				{
					await CancelUnstartedOccurrencesAsync(departmentId, row.Id, null, nowUtc, ct);
					row.IsSuspended = !(enabled && (row.TargetType != 5 || inventoryEnabled)); if (!row.IsSuspended) { row.Revision++; row.ActiveFromUtc = row.GeneratedThroughUtc = row.LastSweepUtc = nowUtc; }
					await WriteAsync(row, false, ct);
				}
			}
			public async Task<List<ChecklistOccurrence>> CalendarOccurrencesAsync(int departmentId, DateTime fromUtc, DateTime untilUtc, int skip, CancellationToken ct = default) => (await ListAsync<ChecklistOccurrence>(departmentId, take: int.MaxValue)).Where(o => o.ScheduleId != null && o.PeriodStartUtc < untilUtc && o.WindowEndUtc > fromUtc && o.State != 6).OrderBy(o => o.PeriodStartUtc).Skip(skip).Take(500).ToList();
			public Task<List<ChecklistShiftStart>> ShiftStartsAsync(int departmentId, DateTime fromUtc, DateTime untilUtc, CancellationToken ct = default) => Task.FromResult(ShiftStarts.Where(s => s.StartUtc > fromUtc && s.StartUtc <= untilUtc).ToList());
			public async Task AdvanceDigestSweepAsync(int departmentId, DateTime nowUtc, CancellationToken ct = default)
			{ foreach (var row in await ListAsync<DepartmentChecklistSettings>(departmentId)) { row.LastDigestSweepUtc = nowUtc; await WriteAsync(row, false, ct); } }
		}
	}
}
