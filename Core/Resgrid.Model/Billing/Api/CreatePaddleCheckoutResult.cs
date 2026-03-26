namespace Resgrid.Model.Billing.Api;

public class CreatePaddleCheckoutResult : BillingApiResponseBase
{
	public CreatePaddleCheckoutData Data { get; set; }
}

public class CreatePaddleCheckoutData
{
	public string PriceId { get; set; }
	public string CustomerId { get; set; }
	public string Environment { get; set; }
}
