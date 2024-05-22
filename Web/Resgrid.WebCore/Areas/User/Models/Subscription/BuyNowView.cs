using System;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	public class BuyNowView : BaseUserModel
	{
		public Department Department { get; set; }
		public string Message { get; set; }
		public int PlanId { get; set; }
		public Plan Plan { get; set; }
		public bool Upgrade { get; set; }
		public Payment Payment { get; set; }
		public double UpgradePrice { get; set; }
		public double Price { get; set; }
		public string Frequency { get; set; }
		public bool FrequencyChange { get; set; }
		public string StripeKey { get; set; }
		public string BrainTreeClientToken { get; set; }
		public int BillingCycles { get; set; }
		public DateTime? NextBillingCycle { get; set; }
	}
}