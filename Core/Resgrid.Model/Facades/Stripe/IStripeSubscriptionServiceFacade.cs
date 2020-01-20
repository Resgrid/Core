using System.Collections.Generic;
using Stripe;

namespace Resgrid.Model.Facades.Stripe
{
	public interface IStripeSubscriptionServiceFacade
	{
		Subscription Get(string customerId, string subscriptionId);
		Subscription Create(string customerId, string planId, SubscriptionCreateOptions createOptions = null);
		Subscription Update(string customerId, string subscriptionId, SubscriptionUpdateOptions updateOptions);
		Subscription Cancel(string customerId, string subscriptionId, bool cancelAtPeriodEnd = false);
		IEnumerable<Subscription> List(string customerId, ListOptions listOptions = null);
	}
}
