using System.Collections.Generic;

namespace Resgrid.Model.Billing.Api
{
	/// <summary>
	/// Current Plan for Department
	/// </summary>
	public class GetAllPaymentAddonsForDepartmentResult : BillingApiResponseBase
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<PaymentAddon> Data { get; set; }
	}
}
