using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Field Records rollout events (RMS-1D, registry M0180).</summary>
	public interface IRmsFieldRolloutEventsRepository : IRepository<RmsFieldRolloutEvent>
	{
		/// <summary>Every event in the window, oldest first. Bounded by <paramref name="take"/> so a dashboard cannot pull the archive.</summary>
		Task<IEnumerable<RmsFieldRolloutEvent>> GetForWindowAsync(int departmentId, DateTime sinceUtc, int take);

		/// <summary>Inserts a batch in one round trip; returns how many rows were written.</summary>
		Task<int> InsertBatchAsync(IEnumerable<RmsFieldRolloutEvent> events, CancellationToken cancellationToken = default);

		/// <summary>Deletes events older than the cutoff; rollout telemetry is operational, not a record.</summary>
		Task<int> DeleteOlderThanAsync(int departmentId, DateTime cutoffUtc, CancellationToken cancellationToken = default);
	}
}
