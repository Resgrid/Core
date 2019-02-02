using System;
using System.Linq;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Facades.Stripe;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Stripe;

namespace Resgrid.Services
{
	public class PaymentProviderService : IPaymentProviderService
	{
		#region Private Methods and Constructors

		public PaymentProviderService()
		{

		}
		#endregion Private Methods and Constructors

		public PaymentProviderEvent SaveEvent(PaymentProviderEvent providerEvent)
		{
			return null;
		}

		public Payment ProcessStripePayment(StripeCharge charge)
		{
			return null;
		}

		public Payment ProcessStripeSubscriptionUpdate(StripeSubscription stripeSubscription)
		{
			return null;
		}

		public Payment ProcessStripeSubscriptionCancellation(StripeSubscription stripeSubscription)
		{		
			return null;
		}

		public Payment ProcessStripeSubscriptionRefund(StripeCharge stripeCharge)
		{
			return null;
		}

		public Payment ProcessStripeChargeFailed(StripeCharge stripeCharge)
		{
			return null;
		}
	}
}
