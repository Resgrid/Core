using Stripe.Checkout;

namespace Resgrid.Model.Billing.Api;

/// <summary>
/// Current Plan for Department
/// </summary>
public class CreateStripeSessionForUpdateResult : BillingApiResponseBase
{
	/// <summary>
	/// Response Data
	/// </summary>
	public CreateStripeSessionForUpdateData Data { get; set; }
	//public Session Data { get; set; }
}

public class CreateStripeSessionForUpdateData
{
	public string SessionId { get; set; }
	public string CustomerId { get; set; }
}
