namespace Resgrid.Model.Invoicing
{
	/// <summary>Invoice lifecycle (plan decision 6). Transitions happen only inside InvoicingService; Overdue is set by the invoice maintenance worker.</summary>
	public enum InvoiceStatus
	{
		Draft = 0,
		Sent = 1,
		PartiallyPaid = 2,
		Paid = 3,
		Overdue = 4,
		Void = 5
	}

	/// <summary>What a rate card item charges for (plan decision 5).</summary>
	public enum RateCardItemTypes
	{
		/// <summary>Per hour of unit time on the call (time on scene).</summary>
		HourlyUnit = 0,
		/// <summary>Per hour of personnel time on the call.</summary>
		HourlyPersonnel = 1,
		/// <summary>One flat charge per call.</summary>
		FlatPerCall = 2,
		/// <summary>A fixed fee added once per invoice or on demand.</summary>
		FixedFee = 3,
		/// <summary>Per mile or kilometre, quantity entered by the user.</summary>
		Mileage = 4,
		/// <summary>Materials or supplies, quantity entered by the user.</summary>
		Material = 5
	}

	/// <summary>How an invoice payment was received. Online is written only by the Phase B2 provider path.</summary>
	public enum InvoicePaymentMethods
	{
		Check = 0,
		Cash = 1,
		Ach = 2,
		CardExternal = 3,
		Other = 4,
		Online = 5
	}

	/// <summary>State of a recorded payment after refunds or disputes (Phase B2). Manual payments stay Succeeded.</summary>
	public enum InvoicePaymentStatuses
	{
		Succeeded = 0,
		Refunded = 1,
		PartiallyRefunded = 2,
		Disputed = 3,
		DisputeLost = 4
	}

	/// <summary>Online payment providers (Phase B2). Stripe only in v1; 2–4 are reserved for later adapters.</summary>
	public enum PaymentProviders
	{
		Stripe = 1,
		Square = 2,
		PayPal = 3,
		AuthorizeNet = 4
	}

	/// <summary>Lifecycle of a department's connection to its own provider account (Phase B2).</summary>
	public enum PaymentConnectionStatuses
	{
		Pending = 0,
		Connected = 1,
		ActionRequired = 2,
		Disconnected = 3,
		Revoked = 4
	}

	/// <summary>Lifecycle of one hosted-checkout attempt against an invoice (Phase B2).</summary>
	public enum PaymentRequestStatuses
	{
		Created = 0,
		Opened = 1,
		Processing = 2,
		Completed = 3,
		Expired = 4,
		Cancelled = 5,
		Failed = 6
	}
}
