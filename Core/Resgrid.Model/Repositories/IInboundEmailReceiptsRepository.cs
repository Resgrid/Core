using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Inbound email receipts (InboundEmailReceipts, M0271). Every write is a single conditional statement, so two deliveries of
	/// the same email racing each other never both win, and none of them throws on the expected collision.
	/// </summary>
	public interface IInboundEmailReceiptsRepository : IRepository<InboundEmailReceipt>
	{
		/// <summary>Inserts the receipt unless one already exists for its provider message id. True when this call inserted it.</summary>
		Task<bool> TryInsertAsync(InboundEmailReceipt receipt, CancellationToken cancellationToken = default);

		Task<InboundEmailReceipt> GetByProviderMessageIdAsync(string providerMessageId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Takes the claim on a receipt still in <paramref name="state"/> whose claim was given back or taken before
		/// <paramref name="leaseCutoff"/>, and counts the delivery. True when this call took it.
		/// </summary>
		Task<bool> TryReclaimAsync(string receiptId, int state, DateTime now, DateTime leaseCutoff, CancellationToken cancellationToken = default);

		/// <summary>Counts a delivery that was answered without processing.</summary>
		Task<bool> RecordDeliveryAsync(string receiptId, CancellationToken cancellationToken = default);

		/// <summary>Processing to Saved: records the saved entity and what it is still owed, and renews the claim.</summary>
		Task<bool> MarkSavedAsync(string receiptId, int entityId, int pendingSteps, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>Clears finished steps from a Saved receipt's pending steps.</summary>
		Task<bool> CompleteStepsAsync(string receiptId, int steps, CancellationToken cancellationToken = default);

		/// <summary>Any unfinished state to Completed, giving back the claim.</summary>
		Task<bool> MarkCompletedAsync(string receiptId, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>Gives back the claim on an unfinished receipt so the next delivery can take it at once.</summary>
		Task<bool> ReleaseAsync(string receiptId, CancellationToken cancellationToken = default);

		Task<int> PurgeReceivedBeforeAsync(DateTime before, CancellationToken cancellationToken = default);
	}
}
