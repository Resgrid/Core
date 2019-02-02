using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Facades.Stripe;
using Stripe;

namespace Resgrid.Services.Facades.Stripe
{
	public class StripeInvoiceServiceFacade : IStripeInvoiceServiceFacade
	{
		private readonly StripeInvoiceService _stripeInvoiceService;

		public StripeInvoiceServiceFacade()
		{
			_stripeInvoiceService = new StripeInvoiceService();
		}

		public StripeInvoice Get(string invoiceId)
		{
			return _stripeInvoiceService.Get(invoiceId);
		}

		public List<StripeInvoice> List(string customerId)
		{
			return _stripeInvoiceService.List(new StripeInvoiceListOptions { CustomerId = customerId }).ToList();
		}

		public StripeInvoice Create(string customerId)
		{
			return _stripeInvoiceService.Create(customerId);
		}

		public StripeInvoice Pay(string invoiceId)
		{
			return _stripeInvoiceService.Pay(invoiceId);
		}
	}
}
