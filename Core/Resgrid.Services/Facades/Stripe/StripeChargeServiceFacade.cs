using Resgrid.Model.Facades.Stripe;
using Stripe;

namespace Resgrid.Services.Facades.Stripe
{
	public class StripeChargeServiceFacade : IStripeChargeServiceFacade
	{
		private readonly ChargeService _stripeChargeService;

		public StripeChargeServiceFacade()
		{
			_stripeChargeService = new ChargeService();
		}

		public Charge Get(string chargeId)
		{
			return _stripeChargeService.Get(chargeId);
		}
	}
}
