namespace Resgrid.Model.Billing.Api
{
	/// <summary>
	/// Current Plan for Department
	/// </summary>
	public class GetHasActiveSubForDepartmentFromStripeResult : BillingApiResponseBase
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public bool Data { get; set; }
	}
}
