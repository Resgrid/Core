using Stripe;
using System.Collections.Generic;

namespace Resgrid.Model.Facades.Stripe
{
	public interface IStripeInvoiceServiceFacade
	{
		StripeInvoice Get(string invoiceId);
		List<StripeInvoice> List(string customerId);
		StripeInvoice Create(string customerId);
		StripeInvoice Pay(string invoiceId);
	}
}
