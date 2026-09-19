using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Core proxy for the Billing API's BusinessOperationsBilling controller (Workforce &amp; Business Operations plan,
	/// decision 42; cloned from Readiness Pro). Checkout and reconciliation live in the Billing API; Core only relays.
	/// </summary>
	public interface IBusinessOperationsBillingService
	{
		Task<BusinessOperationsBillingStatus> GetAsync(int departmentId);
		Task<BusinessOperationsCheckout> BeginCheckoutAsync(int departmentId);
		Task<bool> CancelRenewalAsync(int departmentId);
	}
}

namespace Resgrid.Model
{
	public sealed class BusinessOperationsBillingStatus
	{
		public string Provider { get; set; }
		public string Currency { get; set; }
		public decimal MonthlyAmount { get; set; }
		public bool CheckoutAvailable { get; set; }
		public bool Active { get; set; }
		public bool CanCancel { get; set; }
		public bool Cancelled { get; set; }
		public DateTime? PaidThroughUtc { get; set; }
	}

	public sealed class BusinessOperationsCheckout
	{
		public string Provider { get; set; }
		public string Url { get; set; }
		public string TransactionId { get; set; }
	}

	/// <summary>Provider routing and an expiring checkout reference only; never a card, grant or invoice payload (mirror of ReadinessProBillingAccount, M0211).</summary>
	public sealed class BusinessOperationsBillingAccount
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
