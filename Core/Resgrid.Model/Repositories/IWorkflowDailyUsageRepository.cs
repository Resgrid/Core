using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for tracking daily outbound-messaging usage per department
	/// to enforce per-day send limits on workflow Email and SMS actions.
	/// </summary>
	public interface IWorkflowDailyUsageRepository : IRepository<WorkflowDailyUsage>
	{
		/// <summary>
		/// Returns the current send count for a given department, action type, and UTC date.
		/// Returns 0 when no record exists yet.
		/// </summary>
		Task<int> GetDailySendCountAsync(int departmentId, int actionType, DateTime utcDate);

		/// <summary>
		/// Atomically increments the send count for the given department/action/date.
		/// Inserts a new record with count=1 if none exists; otherwise increments the existing row.
		/// </summary>
		Task IncrementAsync(int departmentId, int actionType, DateTime utcDate, CancellationToken cancellationToken = default);
	}
}

