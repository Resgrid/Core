namespace Resgrid.Model
{
	public enum InboundEmailClaimOutcomes
	{
		/// <summary>No usable provider message id, or the receipt store failed: process the email unguarded, as before M0271.</summary>
		Untracked = 0,

		/// <summary>First delivery, or an earlier attempt stopped before saving anything: process the email.</summary>
		Process = 1,

		/// <summary>An earlier attempt saved the call or message and then failed: finish its pending steps, create nothing.</summary>
		Resume = 2,

		/// <summary>Already handled: acknowledge and do nothing.</summary>
		AlreadyProcessed = 3,

		/// <summary>Another attempt holds the claim right now: ask the provider to deliver again later.</summary>
		InProgress = 4
	}

	/// <summary>What <c>IInboundEmailReceiptsService.ClaimAsync</c> decided for one delivery, and the receipt it holds.</summary>
	public sealed class InboundEmailReceiptClaim
	{
		public static readonly InboundEmailReceiptClaim Untracked = new InboundEmailReceiptClaim(InboundEmailClaimOutcomes.Untracked, null, null, InboundEmailPendingSteps.None);

		public InboundEmailReceiptClaim(InboundEmailClaimOutcomes outcome, string receiptId, int? entityId, InboundEmailPendingSteps pendingSteps)
		{
			Outcome = outcome;
			ReceiptId = receiptId;
			EntityId = entityId;
			PendingSteps = pendingSteps;
		}

		public InboundEmailClaimOutcomes Outcome { get; }

		/// <summary>Null when the delivery is not tracked; every receipt write is then skipped.</summary>
		public string ReceiptId { get; }

		/// <summary>On <see cref="InboundEmailClaimOutcomes.Resume"/>, the call or message the earlier attempt saved.</summary>
		public int? EntityId { get; }

		public InboundEmailPendingSteps PendingSteps { get; }

		public bool IsTracked => ReceiptId != null;

		public bool Has(InboundEmailPendingSteps step) => step != InboundEmailPendingSteps.None && (PendingSteps & step) == step;
	}
}
