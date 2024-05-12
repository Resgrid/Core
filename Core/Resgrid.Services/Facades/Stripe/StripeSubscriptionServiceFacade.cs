using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
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

		public async Task<Subscription> Get(string customerId, string subscriptionId)
		{
			return await _stripeSubscriptionService.GetAsync(subscriptionId);
		}

		public async Task<Subscription> Create(string customerId, string planId, SubscriptionCreateOptions createOptions = null)
		{
			if (createOptions == null)
				createOptions = new SubscriptionCreateOptions();

			createOptions.Customer = customerId;
			createOptions.Items = new List<SubscriptionItemOptions>();
			createOptions.Items.Add(new SubscriptionItemOptions { Plan = planId, Quantity = 1 });

			return await _stripeSubscriptionService.CreateAsync(createOptions);
		}

		public async Task<Subscription> Update(string customerId, string subscriptionId, SubscriptionUpdateOptions updateOptions)
		{
			if (updateOptions == null)
				updateOptions = new SubscriptionUpdateOptions();

			return await _stripeSubscriptionService.UpdateAsync(subscriptionId, updateOptions);
		}

		public Subscription Cancel(string customerId, string subscriptionId, bool cancelAtPeriodEnd = false)
		{
			return  _stripeSubscriptionService.Cancel(subscriptionId, new SubscriptionCancelOptions { });
		}

		public async Task<IEnumerable<Subscription>> List(string customerId, ListOptions listOptions = null)
		{
			return await _stripeSubscriptionService.ListAsync(new SubscriptionListOptions { Customer = customerId });
		}

		public async Task<Subscription> GetCurrentActiveSub(string customerId)
		{
			var subs = await _stripeSubscriptionService.ListAsync(new SubscriptionListOptions { Customer = customerId, Status = "active" });

			if (subs == null || subs.Data == null || subs.Data.Count <= 0)
				return null;

			return subs.Data[0];
		}

		public async Task<bool> AddAddonToSubscription(string customerId, string addonId)
		{
			var sub = await GetCurrentActiveSub(customerId);

			var options = new SubscriptionUpdateOptions();
			var addonItem = new SubscriptionItemOptions();
			addonItem.Price = addonId;
			options.Items.Add(addonItem);
			options.ProrationBehavior = "always_invoice";

			var newSub = await _stripeSubscriptionService.UpdateAsync(sub.Id, options);

			if (newSub != null && newSub.Items != null && newSub.Items.Data.Count > 0)
			{
				var newAddonItem = newSub.Items.Data.FirstOrDefault(x => x.Price != null && x.Price.Id == addonId);

				if (addonItem != null)
					return true;
			}

			return false;
		}

		public async Task<Subscription> GetCurrentActiveSubAsync(string customerId)
		{
			return null;
		}
		
		public async Task<bool> AddAddonToSubscription(string customerId, Model.Plan plan, PlanAddon addon)
		{
			return true;
		}

		public async Task<bool> CancelSubscriptionItem(string subscriptionId, string subscriptionItemId)
		{
			return true;
		}

		public async Task<Subscription> GetCurrentActiveOrCanceledSubAsync(string customerId)
		{
			return null;
		}

		public async Task<Subscription> GetCurrentCanceledSubAsync(string customerId)
		{
			return null;
		}

		public async Task<Subscription> GetCurrentCanceledSubWithAddonAsync(string customerId, PlanAddon addon)
		{
			return null;
		}

		public Task<Subscription> CancelAsync(string customerId, string subscriptionId, bool cancelAtPeriodEnd = false, CancellationToken cancellationToken = default)
		{
			return null;
		}

		public Task<Subscription> AdjustPTTAddonQuantityToSubscription(Subscription sub, long quantity, PlanAddon addon, CancellationToken cancellationToken = default)
		{
			return null;
		}

		public Task<Subscription> CreatePTTAddonSubscription(string customerId, long quantity, PlanAddon addon, CancellationToken cancellationToken = default)
		{
			return null;
		}
	}
}
