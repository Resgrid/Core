using Stripe;
using Stripe.Checkout;

namespace Resgrid.Model.Services
{
	public interface IPaymentProviderService
	{
		PaymentProviderEvent SaveEvent(PaymentProviderEvent providerEvent);

		// Stripe Processing
		Payment ProcessStripePayment(Charge charge);
		Payment ProcessStripeSubscriptionCancellation(Subscription stripeSubscription);
		Payment ProcessStripeSubscriptionUpdate(Subscription stripeSubscription);
		Payment ProcessStripeSubscriptionRefund(Charge stripeCharge);
		Payment ProcessStripeChargeFailed(Charge stripeCharge);
		Session CreateStripeSessionForSub(int departmentId, string stripeCustomerId, string stripePlanId, int planId, string emailAddress, string departmentName);
		Session CreateStripeSessionForUpdate(int departmentId, string stripeCustomerId, string emailAddress, string departmentName);
		Payment ProcessStripeCheckoutCompleted(Session session);
		void ProcessStripeCheckoutUpdate(Session session);
		Subscription GetActiveStripeSubscription(string stripeCustomerId);
		Invoice ChangeActiveSubscription(string stripeCustomerId, string stripePlanId);
	}
}
