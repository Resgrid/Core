using System;

namespace Resgrid.Model
{
	/// <summary>What happened to a department's ADP addon in the billing provider.</summary>
	public enum AdpAddonBillingEventKind
	{
		/// <summary>First purchase, or a repurchase after a lapse. Makes the department wizard-eligible.</summary>
		Activated = 1,

		/// <summary>The yearly cycle renewed; extends the paid-through date and nothing else.</summary>
		Renewed = 2,

		/// <summary>
		/// Cancelled in the provider. Protection continues to the end of the paid cycle — this
		/// SCHEDULES offboarding for that instant, it never decrypts anything.
		/// </summary>
		Cancelled = 3,

		/// <summary>
		/// A payment failed and the provider's dunning window is running. Protection continues
		/// untouched; exhausted dunning arrives later as <see cref="Cancelled"/>.
		/// </summary>
		PaymentFailed = 4
	}

	/// <summary>
	/// A typed, idempotent domain event describing an ADP addon change, emitted by the Billing API
	/// and applied in Core (ADP plan section 17.2). The department is resolved on the billing side,
	/// where the provider customer id maps to a department — Core never sees a provider customer.
	///
	/// Billing truth and data-safety truth stay separate (plan decision 14): NOTHING in this event
	/// disables decryption, suppresses grants or downgrades clients. It can only move the durable
	/// lifecycle state; only a completed offboarding migration changes ciphertext.
	/// </summary>
	public class AdpAddonBillingEvent
	{
		public int DepartmentId { get; set; }

		public AdpAddonBillingEventKind Kind { get; set; }

		/// <summary>
		/// The provider's subscription identifier. Recorded on the policy so an operator can tie a
		/// department's protection state back to the subscription that pays for it.
		/// </summary>
		public string ExternalSubscriptionRef { get; set; }

		/// <summary>End of the paid cycle currently in force (Activated/Renewed).</summary>
		public DateTime? PaidThroughUtc { get; set; }

		/// <summary>
		/// When protection should end (Cancelled). The end of the paid cycle for an ordinary
		/// cancellation; now, for a chargeback or refund — but even then offboarding runs through
		/// the normal worker path rather than flipping anything instantly.
		/// </summary>
		public DateTime? EffectiveEndUtc { get; set; }

		/// <summary>Provider dunning descriptor, carried for the audit line only (PaymentFailed).</summary>
		public string DunningState { get; set; }

		/// <summary>True when this cancellation came from a chargeback or refund rather than the member.</summary>
		public bool IsChargeback { get; set; }

		/// <summary>True when the cancellation is the end of an exhausted dunning cycle.</summary>
		public bool IsDunningExhausted { get; set; }

		public DateTime OccurredOnUtc { get; set; }

		/// <summary>
		/// The provider's event id. Providers retry and duplicate webhooks, so this is what lets the
		/// handler prove it has already applied this exact event.
		/// </summary>
		public string ProviderEventId { get; set; }

		/// <summary>Which provider this came from, for the audit line.</summary>
		public string ProviderName { get; set; }
	}
}
