using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Queues and tracks on-demand system operations (<see cref="SystemOperationCatalog"/>): BackOffice requests them, the
	/// worker (command 76) claims and runs them. Also owns the cache sentinel the worker uses to notice Redis lost its data.
	/// </summary>
	public interface ISystemOperationsService
	{
		/// <summary>
		/// Queues an operation. A request that matches one already waiting (same operation and target) is not queued twice;
		/// the waiting one is returned with <see cref="SystemOperationRequestResult.Created"/> false.
		/// </summary>
		Task<SystemOperationRequestResult> RequestAsync(SystemOperationTypes operationType, int? targetDepartmentId, SystemOperationSources source,
			string requestedBy, string reason, CancellationToken cancellationToken = default);

		/// <summary>Newest first.</summary>
		Task<List<SystemOperationRequest>> GetRecentRequestsAsync(int count = 50);

		Task<SystemOperationRequest> GetRequestByIdAsync(string requestId);

		/// <summary>Withdraws a request the worker has not claimed yet. False when it is already running or finished.</summary>
		Task<bool> CancelPendingRequestAsync(string requestId, string cancelledBy, CancellationToken cancellationToken = default);

		/// <summary>Worker: claims the oldest waiting request, or returns null.</summary>
		Task<SystemOperationRequest> ClaimNextRequestAsync(string workerName, CancellationToken cancellationToken = default);

		/// <summary>Worker: keeps a running request alive, optionally with a new progress line.</summary>
		Task<bool> ReportProgressAsync(string requestId, string progress, CancellationToken cancellationToken = default);

		/// <summary>Worker: records how a running request ended.</summary>
		Task<bool> CompleteRequestAsync(string requestId, bool succeeded, string result, CancellationToken cancellationToken = default);

		/// <summary>Worker: fails running requests whose worker stopped heartbeating (it crashed or was redeployed mid-run).</summary>
		Task<int> FailAbandonedRequestsAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Worker: touches the cache sentinel and returns true when it was missing, meaning the cache came back without its
		/// data (or this is the first check ever). False when the cache is off or unreachable: nothing can be concluded then.
		/// </summary>
		Task<bool> DetectCacheDataLossAsync();

		/// <summary>Whether the cache is reachable, and since when it has held its data (when the sentinel was written).</summary>
		Task<SystemOperationsCacheStatus> GetCacheStatusAsync();

		/// <summary>
		/// Drops the department's cached entries so they reload from the database. Best effort per cache group; returns the
		/// groups that failed (empty when all were cleared).
		/// </summary>
		Task<List<string>> ClearDepartmentCachesAsync(int departmentId);
	}

	public sealed class SystemOperationRequestResult
	{
		public bool Created { get; set; }

		/// <summary>The queued request, or the one already waiting.</summary>
		public SystemOperationRequest Request { get; set; }

		/// <summary>Why nothing was queued (unknown operation, bad target); null on success.</summary>
		public string Error { get; set; }

		public bool Succeeded => Error == null && Request != null;
	}

	public sealed class SystemOperationsCacheStatus
	{
		/// <summary>SystemBehaviorConfig.CacheEnabled as this process sees it.</summary>
		public bool CacheEnabled { get; set; }

		public bool Connected { get; set; }

		/// <summary>When the worker first found the cache without its sentinel: it has held its data since then. Null when unknown.</summary>
		public DateTime? DataPresentSinceUtc { get; set; }
	}
}
