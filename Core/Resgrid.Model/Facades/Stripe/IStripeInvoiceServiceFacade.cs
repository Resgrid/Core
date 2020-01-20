using Stripe;
using System.Collections.Generic;

namespace Resgrid.Model.Facades.Stripe
{
	public interface IStripeInvoiceServiceFacade
	{
		Invoice Get(string invoiceId);
		List<Invoice> List(string customerId);
		Invoice Create(string customerId);
		Invoice Pay(string invoiceId);
	}
}
