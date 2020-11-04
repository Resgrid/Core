using System.Collections.Generic;
using Resgrid.Model.Facades.Stripe;
using Stripe;

namespace Resgrid.Services.Facades.Stripe
{
	public class StripeSubscriptionServiceFacade : IStripeSubscriptionServiceFacade
	{
		private readonly SubscriptionService _stripeSubscriptionService;

		public StripeSubscriptionServiceFacade()
		{
			_stripeSubscriptionService = new SubscriptionService();
		}

		public Subscription Get(string customerId, string subscriptionId)
		{
			return _stripeSubscriptionService.Get(subscriptionId);
		}

		public Subscription Create(string customerId, string planId, SubscriptionCreateOptions createOptions = null)
		{
			if (createOptions == null)
				createOptions = new SubscriptionCreateOptions();

			createOptions.Customer = customerId;
			createOptions.Items = new List<SubscriptionItemOptions>();
			createOptions.Items.Add(new SubscriptionItemOptions { Plan = planId, Quantity = 1 });

			return _stripeSubscriptionService.Create(createOptions);
		}

		public Subscription Update(string customerId, string subscriptionId, SubscriptionUpdateOptions updateOptions)
		{
			if (updateOptions == null)
				updateOptions = new SubscriptionUpdateOptions();

			return _stripeSubscriptionService.Update(subscriptionId, updateOptions);
		}

		public Subscription Cancel(string customerId, string subscriptionId, bool cancelAtPeriodEnd = false)
		{

			return _stripeSubscriptionService.Cancel(subscriptionId, new SubscriptionCancelOptions { });
		}

		public IEnumerable<Subscription> List(string customerId, ListOptions listOptions = null)
		{
			return _stripeSubscriptionService.List(new SubscriptionListOptions { Customer = customerId });
		}
	}
}
