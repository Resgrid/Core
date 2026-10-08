using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>System operation requests (SystemOperationRequests, M0267). Every state change is a conditional write on the current status.</summary>
	public interface ISystemOperationRequestsRepository : IRepository<SystemOperationRequest>
	{
		/// <summary>Newest first.</summary>
		Task<List<SystemOperationRequest>> GetRecentAsync(int take);

		/// <summary>A Pending request for the same operation and target, if one is already waiting.</summary>
		Task<SystemOperationRequest> GetPendingAsync(int operationType, int? targetDepartmentId);

		/// <summary>
		/// Moves the oldest Pending request to Running for this worker and returns it, or null when nothing is waiting.
		/// Two workers never claim the same row: the claim only succeeds while the row is still Pending.
		/// </summary>
		Task<SystemOperationRequest> ClaimNextPendingAsync(string workerName, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>Refreshes HeartbeatOn, and Progress when one is given, on a Running request.</summary>
		Task<bool> HeartbeatAsync(string requestId, string progress, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>Records the outcome of a Running request.</summary>
		Task<bool> FinishAsync(string requestId, int status, string result, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>Withdraws a request nobody has claimed yet.</summary>
		Task<bool> CancelPendingAsync(string requestId, string cancelledBy, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>Fails every Running request whose heartbeat (or start) is older than the cutoff: its worker stopped.</summary>
		Task<int> FailAbandonedAsync(DateTime heartbeatBefore, string result, DateTime now, CancellationToken cancellationToken = default);
	}
}
