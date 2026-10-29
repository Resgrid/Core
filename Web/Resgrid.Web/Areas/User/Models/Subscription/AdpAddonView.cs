using System;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	/// <summary>
	/// The ADP addon purchase and management pages (plan section 17.1).
	///
	/// Modelled on <see cref="BuyAddonView"/> with one hard difference: every ADP billing action is
	/// restricted to <c>Department.ManagingUserId</c>, server-side. <see cref="IsManagingMember"/>
	/// only decides what the page draws — the controller re-checks it on every action, because a
	/// hidden button is not an authorization control.
	///
	/// The billing facts and the protection facts are deliberately kept as separate properties and
	/// never merged into one "is it on" flag. Buying the addon encrypts nothing, and cancelling
	/// decrypts nothing; only the enrollment and offboarding migrations move ciphertext, and a page
	/// that blurred the two would tell a member their data was safe before it was.
	/// </summary>
	public class AdpAddonView : BaseUserModel
	{
		public Department Department { get; set; }

		public PlanAddon PlanAddon { get; set; }

		public string PlanAddonId { get; set; }

		/// <summary>Yearly price, already formatted for the department's currency.</summary>
		public string Price { get; set; }

		/// <summary>Caller is the department's managing member — the only identity that may buy or cancel.</summary>
		public bool IsManagingMember { get; set; }

		/// <summary>The addon requires an active paid plan; a free department may look but not buy.</summary>
		public bool HasPaidPlan { get; set; }

		/// <summary>An addon row exists for the department, cancelled or not.</summary>
		public bool HasAddon { get; set; }

		/// <summary>The addon is cancelled and running out its paid period.</summary>
		public bool IsCancelled { get; set; }

		/// <summary>End of the current billing period, from the addon row.</summary>
		public DateTime? EndingOn { get; set; }

		/// <summary>Durable protection state. Disabled with an active addon means "bought, not yet enrolled".</summary>
		public DepartmentDataProtectionState ProtectionState { get; set; }

		/// <summary>What billing has been paid through, as recorded on the protection policy.</summary>
		public DateTime? PaidThroughOn { get; set; }

		/// <summary>
		/// End of the lapse grace window, when payment is outstanding. Present means "we are waiting
		/// for money and protection is running anyway until this date".
		/// </summary>
		public DateTime? GraceEndsOn { get; set; }

		/// <summary>When a scheduled offboarding migration takes effect.</summary>
		public DateTime? OffboardingEffectiveOn { get; set; }

		/// <summary>True while payment is outstanding and the grace window has not run out.</summary>
		public bool IsInGrace => GraceEndsOn.HasValue && GraceEndsOn.Value > DateTime.UtcNow;

		public string ErrorMessage { get; set; }

		/// <summary>The ADP enrollment toggle is on for the department, so the add-on is offered to buy.</summary>
		public bool Offered { get; set; }

		/// <summary>
		/// The yearly checkout can open right now (Billing API: toggle on, provider price configured and valid, nothing held). When it
		/// cannot, the page points at sales instead of drawing a button the checkout would refuse.
		/// </summary>
		public bool CanPurchaseOnline { get; set; }

		/// <summary>Stripe or Paddle: which checkout the page's script opens.</summary>
		public string CheckoutProvider { get; set; }

		/// <summary>The held add-on is the legacy item on the base Stripe subscription (M0126), not the yearly checkout.</summary>
		public bool IsLegacyItem { get; set; }

		/// <summary>The yearly checkout's own subscription is renewing and may be cancelled at the end of its paid year.</summary>
		public bool CanCancelYearly { get; set; }

		/// <summary>The provider return outcome (<c>complete</c> / <c>cancelled</c>), as on the other add-on billing pages.</summary>
		public string Result { get; set; }

		/// <summary>How many times the page has refreshed itself while a completed checkout waits for its webhook.</summary>
		public int Attempt { get; set; }

		/// <summary>A completed checkout returned but the provider webhook has not activated the add-on yet.</summary>
		public bool AwaitingActivation => Result == AddonBillingResults.Complete && !HasAddon;
	}
}
