using Stripe.BillingPortal;

namespace Resgrid.Model.Billing.Api;

/// <summary>
/// Current Plan for Department
/// </summary>
public class CreateStripeBillingPortalSessionResult : BillingApiResponseBase
{
	/// <summary>
	/// Response Data
	/// </summary>
	public CreateStripeBillingPortalSessionData Data { get; set; }
}

public class CreateStripeBillingPortalSessionData
{
	public string SessionId { get; set; }
	public string CustomerId { get; set; }
	public string Url { get; set; }
}
