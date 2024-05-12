namespace Resgrid.Model.Billing.Api
{
	/// <summary>
	/// Current Plan for Department
	/// </summary>
	public class GetCurrentPaymentForDepartment : BillingApiResponseBase
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public Payment Data { get; set; }
	}
}
