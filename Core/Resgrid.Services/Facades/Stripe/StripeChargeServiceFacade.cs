using Resgrid.Model.Facades.Stripe;
using Stripe;

namespace Resgrid.Services.Facades.Stripe
{
	public class StripeChargeServiceFacade : IStripeChargeServiceFacade
	{
		private readonly StripeChargeService _stripeChargeService;

		public StripeChargeServiceFacade()
		{
			_stripeChargeService = new StripeChargeService();
		}

		public StripeCharge Get(string chargeId)
		{
			return _stripeChargeService.Get(chargeId);
		}
	}
}