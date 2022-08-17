using System.Collections.Generic;
using System.Threading.Tasks;
using Stripe;

namespace Resgrid.Model.Facades.Stripe
{
	public interface IStripeSubscriptionServiceFacade
	{
		Task<Subscription> Get(string customerId, string subscriptionId);
		Task<Subscription> Create(string customerId, string planId, SubscriptionCreateOptions createOptions = null);
		Task<Subscription> Update(string customerId, string subscriptionId, SubscriptionUpdateOptions updateOptions);
		Subscription Cancel(string customerId, string subscriptionId, bool cancelAtPeriodEnd = false);
		Task<IEnumerable<Subscription>> List(string customerId, ListOptions listOptions = null);
		Task<Subscription> GetCurrentActiveSubAsync(string customerId);
		Task<bool> AddAddonToSubscription(string customerId, Model.Plan plan, PlanAddon addon);
		Task<bool> CancelSubscriptionItem(string subscriptionId, string subscriptionItemId);
		Task<Subscription> GetCurrentActiveOrCanceledSubAsync(string customerId);
		Task<Subscription> GetCurrentCanceledSubAsync(string customerId);
		Task<Subscription> GetCurrentCanceledSubWithAddonAsync(string customerId, PlanAddon addon);
	}
}
