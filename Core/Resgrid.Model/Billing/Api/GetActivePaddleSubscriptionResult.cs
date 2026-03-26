namespace Resgrid.Model.Billing.Api;

public class GetActivePaddleSubscriptionResult : BillingApiResponseBase
{
	public GetActivePaddleSubscriptionData Data { get; set; }
}

public class GetActivePaddleSubscriptionData
{
	public string SubscriptionId { get; set; }
	public long TotalQuantity { get; set; }
}
