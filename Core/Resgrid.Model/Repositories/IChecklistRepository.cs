using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;

namespace Resgrid.Model.Repositories
{
	public interface IChecklistRepository
	{
		Task LockDepartmentAsync(int departmentId, CancellationToken ct = default);
		Task LockAccessFenceAsync(CancellationToken ct = default);
		Task ApplyAccessStateAsync(int departmentId, bool enabled, DateTime nowUtc, CancellationToken ct = default, bool inventoryEnabled = true);
		Task<List<ChecklistOccurrence>> CalendarOccurrencesAsync(int departmentId, DateTime fromUtc, DateTime untilUtc, int skip, CancellationToken ct = default);
		Task<List<ChecklistShiftStart>> ShiftStartsAsync(int departmentId, DateTime fromUtc, DateTime untilUtc, CancellationToken ct = default);
		Task AdvanceDigestSweepAsync(int departmentId, DateTime nowUtc, CancellationToken ct = default);
		Task<T> GetAsync<T>(int departmentId, string id, CancellationToken ct = default) where T : ChecklistRow;
		Task<List<T>> ListAsync<T>(int departmentId, string parentId = null, int skip = 0, int take = 100, CancellationToken ct = default) where T : ChecklistRow;
		Task<List<T>> ListChildrenAsync<T>(int departmentId, IReadOnlyCollection<string> parentIds, int skip = 0, int take = 100, CancellationToken ct = default) where T : ChecklistRow;
		Task WriteAsync<T>(T row, bool insert, CancellationToken ct = default) where T : ChecklistRow;
		Task ReplaceAnswersAsync(int departmentId, string completionId, IEnumerable<ChecklistCompletionItem> items, CancellationToken ct = default);
		Task DeleteFileAsync(int departmentId, string id, CancellationToken ct = default);
		Task<ChecklistCompletionFile> GetFileMetadataAsync(int departmentId, string id);
		Task<List<int>> SchedulingDepartmentsAsync(int afterDepartmentId, CancellationToken ct = default);
		Task<List<ChecklistSchedule>> ActiveSchedulesAsync(int departmentId, string afterId, CancellationToken ct = default);
		Task<List<ChecklistOccurrence>> ScheduledOccurrencesAsync(int departmentId, string scheduleId, DateTime fromUtc, DateTime untilUtc, CancellationToken ct = default);
		Task<List<ChecklistOccurrence>> DueOccurrencesAsync(int departmentId, DateTime untilUtc, int skip, CancellationToken ct = default);
		Task<List<ChecklistOccurrence>> OccurrencesInWindowAsync(int departmentId, string scheduleId, DateTime fromUtc, DateTime untilUtc, CancellationToken ct = default);
		Task CancelUnstartedOccurrencesAsync(int departmentId, string scheduleId, DateTime? fromUtc, DateTime nowUtc, CancellationToken ct = default);
		Task<bool> WorkshiftExistsAsync(int departmentId, string workshiftId, CancellationToken ct = default);
		Task<List<DateTime>> WorkshiftStartsAsync(int departmentId, string workshiftId, DateTime fromUtc, DateTime untilUtc, CancellationToken ct = default);
	}
}
