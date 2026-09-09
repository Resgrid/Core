using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;

namespace Resgrid.Model.Repositories
{
	public interface IChecklistReminderRepository
	{
		Task<List<int>> DepartmentsAsync(int afterDepartmentId, CancellationToken ct = default);
		Task<List<ChecklistReminder>> ForRecipientAsync(int departmentId, string userId, int skip, CancellationToken ct = default);
		// Caller holds the checklist department transaction lock for enqueue and claim.
		Task EnqueueAsync(ChecklistReminder reminder, CancellationToken ct = default);
		Task<List<ChecklistReminder>> ClaimAsync(int departmentId, DateTime now, bool digest, CancellationToken ct = default);
		Task FinishAsync(ChecklistReminder reminder, ChecklistReminderStatus status, DateTime now, CancellationToken ct = default);
	}
}
