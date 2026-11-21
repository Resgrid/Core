using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Core proxy for the Billing API's DedicatedNumberBilling controller (call-intake-and-ai-destinations-plan.md D1; cloned from
	/// Incident Back Office). Checkout and reconciliation live in the Billing API; Core only relays. Returns null / false when billing
	/// is not configured or unreachable so callers fail closed; checkout is never an entitlement.
	/// </summary>
	public interface IDedicatedNumberBillingService
	{
		Task<DedicatedNumberBillingStatus> GetAsync(int departmentId);

		/// <summary>
		/// Starts (or resumes) the department's checkout. <paramref name="returnTo"/> picks the page a Stripe checkout comes back to; a
		/// resumed checkout keeps the page it was started from, and Paddle's overlay returns to the page that opened it.
		/// </summary>
		Task<DedicatedNumberCheckout> BeginCheckoutAsync(int departmentId, DedicatedNumberCheckoutReturns returnTo = DedicatedNumberCheckoutReturns.BillingPage);

		Task<bool> CancelRenewalAsync(int departmentId);
	}
}

namespace Resgrid.Model
{
	/// <summary>
	/// Where a Dedicated Number checkout returns the buyer (the Billing API turns it into Stripe's success and cancel URLs). A closed list,
	/// so no caller can send the provider's redirect anywhere else; an unknown value is the billing page.
	/// </summary>
	public enum DedicatedNumberCheckoutReturns
	{
		/// <summary>The add-on billing page, /User/DedicatedNumberBilling (from the Subscription page card).</summary>
		BillingPage = 0,

		/// <summary>The Dedicated Number page, /User/DedicatedNumber, where the department then picks its number.</summary>
		NumberPage = 1
	}

	public static class DedicatedNumberCheckoutReturnPaths
	{
		public const string BillingPage = "/User/DedicatedNumberBilling";
		public const string NumberPage = "/User/DedicatedNumber";

		/// <summary>The site-relative path for <paramref name="returnTo"/>; anything but <see cref="DedicatedNumberCheckoutReturns.NumberPage"/> is the billing page.</summary>
		public static string For(DedicatedNumberCheckoutReturns returnTo) => returnTo == DedicatedNumberCheckoutReturns.NumberPage ? NumberPage : BillingPage;

		/// <summary>A wire value (the Billing API's request body) as a known return page; unknown values are the billing page.</summary>
		public static DedicatedNumberCheckoutReturns Parse(int value) =>
			value == (int)DedicatedNumberCheckoutReturns.NumberPage ? DedicatedNumberCheckoutReturns.NumberPage : DedicatedNumberCheckoutReturns.BillingPage;
	}

	public sealed class DedicatedNumberBillingStatus
	{
		public string Provider { get; set; }
		public string Currency { get; set; }
		public decimal MonthlyAmount { get; set; }
		public bool CheckoutAvailable { get; set; }
		public bool Active { get; set; }
		public bool CanCancel { get; set; }
		public bool Cancelled { get; set; }
		public DateTime? PaidThroughUtc { get; set; }

		/// <summary>The department's base plan is an active paid entity plan (the add-on is sold only with one).</summary>
		public bool HasEntityPlan { get; set; }
	}

	public sealed class DedicatedNumberCheckout
	{
		public string Provider { get; set; }
		public string Url { get; set; }
		public string TransactionId { get; set; }
	}

	/// <summary>Provider routing and an expiring checkout reference only; never a card, grant or invoice payload (mirror of AiBillingAccount, M0323).</summary>
	public sealed class DedicatedNumberBillingAccount
	{
		public int DepartmentId { get; set; }
		public string Provider { get; set; }
		public string CustomerId { get; set; }
		public string PlanAddonId { get; set; }
		public string PriceId { get; set; }
		public string SubscriptionId { get; set; }
		public string CheckoutId { get; set; }
		public string CheckoutUrl { get; set; }
		public DateTime? CheckoutExpiresOn { get; set; }
		public string CheckoutAttempt { get; set; }
		public DateTime UpdatedOn { get; set; }
	}
}
