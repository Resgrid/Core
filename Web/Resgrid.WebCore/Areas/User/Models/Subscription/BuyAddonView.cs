using System;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	public class BuyAddonView : BaseUserModel
	{
		public Department Department { get; set; }
		public string Message { get; set; }
		public string PlanAddonId { get; set; }
		public PlanAddon PlanAddon { get; set; }
		public string AddonName { get; set; }
		public PaymentAddon	CurrentPaymentAddon { get; set; }


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
		public long Quantity { get; set; }
	}
}
