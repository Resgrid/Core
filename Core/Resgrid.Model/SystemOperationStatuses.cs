namespace Resgrid.Model
{
	/// <summary>Lifecycle of a SystemOperationRequests row. Persisted: never renumber.</summary>
	public enum SystemOperationStatuses
	{
		/// <summary>Waiting for the worker to claim it.</summary>
		Pending = 0,

		/// <summary>Claimed by a worker, which keeps HeartbeatOn fresh while it runs.</summary>
		Running = 1,

		Completed = 2,

		/// <summary>The operation reported a failure, or its worker stopped heartbeating and the row was abandoned.</summary>
		Failed = 3,

		/// <summary>Withdrawn by staff before a worker claimed it.</summary>
		Cancelled = 4
	}
}
