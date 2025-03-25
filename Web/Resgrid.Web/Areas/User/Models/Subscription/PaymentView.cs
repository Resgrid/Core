namespace Resgrid.Web.Areas.User.Models.Subscription
{
	public class PaymentView
	{
		public string credit_card_billing_address { get; set; }
		public string credit_card_billing_zip { get; set; }
		public string country { get; set; }
		public string credit_card_first_name { get; set; }
		public string credit_card_last_name { get; set; }
		public int PlanId { get; set; }
		public bool IsUpgrade { get; set; }
		public string coupon_code { get; set; }
		public string affiliate_code { get; set; }
	}
}