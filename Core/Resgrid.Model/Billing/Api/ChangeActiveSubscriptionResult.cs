using Stripe;

namespace Resgrid.Model.Billing.Api;

/// <summary>
/// Current Plan for Department
/// </summary>
public class ChangeActiveSubscriptionResult : BillingApiResponseBase
{
	/// <summary>
	/// Response Data
	/// </summary>
	public ChangeActiveSubscriptionData Data { get; set; }
}

public class ChangeActiveSubscriptionData
{
	public string InvoiceId { get; set; }
	public string CustomerId { get; set; }
}
