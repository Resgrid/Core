using System.Collections.Generic;
using Resgrid.Model.Facades.Stripe;
using Stripe;

namespace Resgrid.Services.Facades.Stripe
{
	public class StripeSubscriptionServiceFacade : IStripeSubscriptionServiceFacade
	{
		private readonly StripeSubscriptionService _stripeSubscriptionService;

		public StripeSubscriptionServiceFacade()
		{
#if DEBUG
			_stripeSubscriptionService = new StripeSubscriptionService(Config.PaymentProviderConfig.TestKey);
#else
			_stripeSubscriptionService = new StripeSubscriptionService(Config.PaymentProviderConfig.ProductionKey);
#endif
		}
		public StripeSubscription Get(string customerId, string subscriptionId)
		{
			return _stripeSubscriptionService.Get(customerId, subscriptionId);
		}

		public StripeSubscription Create(string customerId, string planId, StripeSubscriptionCreateOptions createOptions = null)
		{
			return _stripeSubscriptionService.Create(customerId, planId, createOptions);
		}

		public StripeSubscription Update(string customerId, string subscriptionId, StripeSubscriptionUpdateOptions updateOptions)
		{
			return _stripeSubscriptionService.Update(customerId, subscriptionId, updateOptions);
		}

		public StripeSubscription Cancel(string customerId, string subscriptionId, bool cancelAtPeriodEnd = false)
		{
			return _stripeSubscriptionService.Cancel(customerId, subscriptionId, cancelAtPeriodEnd);
		}

		public IEnumerable<StripeSubscription> List(string customerId, StripeListOptions listOptions = null)
		{
			return _stripeSubscriptionService.List(customerId, listOptions);
		}
	}
}
