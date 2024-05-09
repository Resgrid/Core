using Stripe;

namespace Resgrid.Model.Billing.Api;

/// <summary>
/// Current Plan for Department
/// </summary>
public class GetCanceledPlanFromStripeResult : BillingApiResponseBase
{
	/// <summary>
	/// Response Data
	/// </summary>
	public GetCanceledPlanFromStripeData Data { get; set; }
}

public class GetCanceledPlanFromStripeData
{
	public string SubscriptionId { get; set; }
	public long TotalQuantity { get; set; }
}
