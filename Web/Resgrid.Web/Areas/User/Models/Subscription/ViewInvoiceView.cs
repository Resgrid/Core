using Resgrid.Model;
using Stripe;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	public class ViewInvoiceView
	{
		public Payment Payment { get; set; }
		public Charge Charge { get; set; }
	}
}
