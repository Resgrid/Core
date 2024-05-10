namespace Resgrid.Model.Billing.Api
{
	/// <summary>
	/// Current Plan for Department
	/// </summary>
	public class GetCurrentPlanForDepartmentResult : BillingApiResponseBase
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public Model.Plan Data { get; set; }
	}
}
