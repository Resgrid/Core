namespace Resgrid.Model.Billing.Api
{
	/// <summary>
	/// Current Plan for Department
	/// </summary>
	public class GetPlanAddonByExternalResult : BillingApiResponseBase
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public PlanAddon Data { get; set; }
	}
}
