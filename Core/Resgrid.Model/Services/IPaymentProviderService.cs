using Stripe;

namespace Resgrid.Model.Services
{
	public interface IPaymentProviderService
	{
		PaymentProviderEvent SaveEvent(PaymentProviderEvent providerEvent);

		// Stripe Processing
		Payment ProcessStripePayment(StripeCharge charge);
		Payment ProcessStripeSubscriptionCancellation(StripeSubscription stripeSubscription);
		Payment ProcessStripeSubscriptionUpdate(StripeSubscription stripeSubscription);
		Payment ProcessStripeSubscriptionRefund(StripeCharge stripeCharge);
		Payment ProcessStripeChargeFailed(StripeCharge stripeCharge);
	}
}