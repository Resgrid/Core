using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;

namespace Resgrid.Model.Services
{
	public interface IChecklistReminderService
	{
		Task<ChecklistReminderSweepResult> SweepAsync(DateTime utcNow, CancellationToken ct = default);
	}
}
