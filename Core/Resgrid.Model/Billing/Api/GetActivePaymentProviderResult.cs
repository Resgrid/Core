namespace Resgrid.Model.Billing.Api;

public class GetActivePaymentProviderResult : BillingApiResponseBase
{
	public GetActivePaymentProviderData Data { get; set; }
}

public class GetActivePaymentProviderData
{
	public int ActiveProvider { get; set; }
	public string ProviderName { get; set; }
}
