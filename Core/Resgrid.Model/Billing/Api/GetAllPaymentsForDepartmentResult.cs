using System.Collections.Generic;

namespace Resgrid.Model.Billing.Api
{
	/// <summary>
	/// Current Plan for Department
	/// </summary>
	public class GetAllPaymentsForDepartmentResult : BillingApiResponseBase
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<Payment> Data { get; set; }
	}
}
