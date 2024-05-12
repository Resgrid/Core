using System.Collections.Generic;

namespace Resgrid.Model.Billing.Api
{
	/// <summary>
	/// Current Plan for Department
	/// </summary>
	public class GetAllPlanAddonsByTypeResult : BillingApiResponseBase
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<PlanAddon> Data { get; set; }
	}
}
