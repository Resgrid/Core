namespace Resgrid.Model.Billing.Api
{
	/// <summary>
	/// Current Plan for Department
	/// </summary>
	public class GetPlanCountsForDepartmentResult : BillingApiResponseBase
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public DepartmentPlanCount Data { get; set; }
	}
}
