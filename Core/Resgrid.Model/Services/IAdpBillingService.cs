using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Core proxy for the Billing API's AdpBilling controller: the Advanced Data Protection yearly checkout (AdpAddonConfig; cloned
	/// from Enhanced AI with a yearly interval). Checkout and reconciliation live in the Billing API; Core only relays.
	/// </summary>
	public interface IAdpBillingService
	{
		Task<AdpBillingStatus> GetAsync(int departmentId);
		Task<AdpCheckout> BeginCheckoutAsync(int departmentId);
		Task<bool> CancelRenewalAsync(int departmentId);
	}
}

namespace Resgrid.Model
{
	public sealed class AdpBillingStatus
	{
		public string Provider { get; set; }
		public string Currency { get; set; }
		public decimal YearlyAmount { get; set; }
		public bool CheckoutAvailable { get; set; }
		/// <summary>A paid yearly interval from this checkout is running (legacy subscription-item ADP is reported separately by Core).</summary>
		public bool Active { get; set; }
		public bool CanCancel { get; set; }
		public bool Cancelled { get; set; }
		public DateTime? PaidThroughUtc { get; set; }
	}

	public sealed class AdpCheckout
	{
		public string Provider { get; set; }
		public string Url { get; set; }
		public string TransactionId { get; set; }
	}

	/// <summary>Provider routing and an expiring checkout reference only; never a card, grant or invoice payload (mirror of AiBillingAccount, M0298).</summary>
	public sealed class AdpBillingAccount
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
