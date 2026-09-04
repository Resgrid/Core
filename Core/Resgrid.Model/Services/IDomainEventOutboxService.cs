using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Reusable transactional outbox transport (RMS plan section 5.6, cross-plan adjustment 7). A producer
	/// enqueues inside its own unit of work; after commit it asks for an in-process dispatch of the rows
	/// it wrote, and worker command 40 sweeps whatever that missed. Producers own event names, schemas
	/// and payload contracts; this service owns identity, sequence, leases, retries and delivery state.
	/// </summary>
	public interface IDomainEventOutboxService
	{
		/// <summary>Writes the row inside the caller's current unit of work. Returns the row (with EventId and Sequence).</summary>
		Task<DomainEventOutboxEntry> EnqueueAsync(int departmentId, string producerSubsystem, DomainEventEnvelope envelope, CancellationToken cancellationToken = default);

		/// <summary>Post-commit fast path: dispatches the given rows if still pending. Never throws to the caller.</summary>
		Task<int> DispatchAfterCommitAsync(IEnumerable<long> domainEventOutboxIds, CancellationToken cancellationToken = default);

		/// <summary>Durable sweep used by worker command 40: leases and dispatches pending rows.</summary>
		Task<int> DispatchPendingAsync(string leaseOwner, int batchSize, CancellationToken cancellationToken = default);

		Task<DomainEventOutboxHealth> GetHealthAsync();

		Task<int> PurgeDispatchedAsync(int olderThanDays, CancellationToken cancellationToken = default);
	}
}
