using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stripe;

namespace Resgrid.Model.Facades.Stripe
{
	public interface IStripeSubscriptionServiceFacade
	{
		Task<Subscription> Get(string customerId, string subscriptionId);
		Task<Subscription> Create(string customerId, string planId, SubscriptionCreateOptions createOptions = null);
		Task<Subscription> Update(string customerId, string subscriptionId, SubscriptionUpdateOptions updateOptions);
		Task<Subscription> CancelAsync(string customerId, string subscriptionId, bool cancelAtPeriodEnd = false, CancellationToken cancellationToken = default(CancellationToken));
		Task<IEnumerable<Subscription>> List(string customerId, ListOptions listOptions = null);
		Task<Subscription> GetCurrentActiveSubAsync(string customerId);
		Task<bool> AddAddonToSubscription(string customerId, Model.Plan plan, PlanAddon addon);
		Task<bool> CancelSubscriptionItem(string subscriptionId, string subscriptionItemId);
		Task<Subscription> GetCurrentActiveOrCanceledSubAsync(string customerId);
		Task<Subscription> GetCurrentCanceledSubAsync(string customerId);
		Task<Subscription> GetCurrentCanceledSubWithAddonAsync(string customerId, PlanAddon addon);
		Task<Subscription> AdjustPTTAddonQuantityToSubscription(Subscription sub, long quantity, PlanAddon addon, CancellationToken cancellationToken = default(CancellationToken));
		Task<Subscription> CreatePTTAddonSubscription(string customerId, long quantity, PlanAddon addon, CancellationToken cancellationToken = default(CancellationToken));
	}
}
