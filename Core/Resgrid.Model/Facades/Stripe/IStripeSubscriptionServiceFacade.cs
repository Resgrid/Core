using System.Collections.Generic;
using Stripe;

namespace Resgrid.Model.Facades.Stripe
{
	public interface IStripeSubscriptionServiceFacade
	{
		StripeSubscription Get(string customerId, string subscriptionId);
		StripeSubscription Create(string customerId, string planId, StripeSubscriptionCreateOptions createOptions = null);
		StripeSubscription Update(string customerId, string subscriptionId, StripeSubscriptionUpdateOptions updateOptions);
		StripeSubscription Cancel(string customerId, string subscriptionId, bool cancelAtPeriodEnd = false);
		IEnumerable<StripeSubscription> List(string customerId, StripeListOptions listOptions = null);
	}
}