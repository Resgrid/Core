using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Makes inbound email processing idempotent per provider message id (M0271). Fails open: when the receipt store cannot be
	/// read or written the email is processed as before, because blocking a dispatch is worse than paging twice. The writes after
	/// the claim take no cancellation token on purpose: the provider dropping the connection cancels the request, and the receipt
	/// must still record what was saved so the redelivery that follows finishes it instead of starting over.
	/// </summary>
	public interface IInboundEmailReceiptsService
	{
		/// <summary>Claims a delivery: decides whether to process it, resume it, acknowledge it or ask for it later.</summary>
		Task<InboundEmailReceiptClaim> ClaimAsync(string providerMessageId, InboundEmailKinds kind, CancellationToken cancellationToken = default);

		/// <summary>
		/// Claims a v4 request carrying an <c>Idempotency-Key</c> header. The key is scoped to the department (one department's
		/// key never finds another's call) and hashed when it would not fit the column. A blank key is untracked.
		/// </summary>
		Task<InboundEmailReceiptClaim> ClaimApiRequestAsync(InboundEmailKinds kind, int departmentId, string idempotencyKey, CancellationToken cancellationToken = default);

		/// <summary>Records the call or message the delivery saved and the steps it still owes, before those steps run.</summary>
		Task MarkSavedAsync(InboundEmailReceiptClaim claim, int entityId, InboundEmailPendingSteps pendingSteps);

		/// <summary>Records that a pending step finished, so a retry does not repeat it.</summary>
		Task CompleteStepAsync(InboundEmailReceiptClaim claim, InboundEmailPendingSteps step);

		Task MarkCompletedAsync(InboundEmailReceiptClaim claim);

		/// <summary>Gives back the claim after a failure, so the provider's next delivery is handled at once.</summary>
		Task ReleaseAsync(InboundEmailReceiptClaim claim);

		/// <summary>Deletes receipts older than the retention window. Returns how many went.</summary>
		Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
	}
}
