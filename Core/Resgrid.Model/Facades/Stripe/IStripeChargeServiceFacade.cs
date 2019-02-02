using System.Collections.Generic;
using Stripe;

namespace Resgrid.Model.Facades.Stripe
{
	public interface IStripeChargeServiceFacade
	{
		StripeCharge Get(string chargeId);
	}
}