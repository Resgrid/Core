using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Facades.Stripe;
using Stripe;

namespace Resgrid.Services.Facades.Stripe
{
	public class StripeInvoiceServiceFacade : IStripeInvoiceServiceFacade
	{
		private readonly InvoiceService _stripeInvoiceService;

		public StripeInvoiceServiceFacade()
		{
			_stripeInvoiceService = new InvoiceService();
		}

		public Invoice Get(string invoiceId)
		{
			return _stripeInvoiceService.Get(invoiceId);
		}

		public List<Invoice> List(string customerId)
		{
			return _stripeInvoiceService.List(new InvoiceListOptions { Customer = customerId }).ToList();
		}

		public Invoice Create(string customerId)
		{
			return _stripeInvoiceService.Create(new InvoiceCreateOptions { Customer = customerId });
		}

		public Invoice Pay(string invoiceId)
		{
			return _stripeInvoiceService.Pay(invoiceId, new InvoicePayOptions { });
		}
	}
}
