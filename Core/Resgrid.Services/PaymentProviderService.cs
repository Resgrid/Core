using Resgrid.Model;
using Resgrid.Model.Services;
using Stripe;
using Stripe.Checkout;

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

		public Payment ProcessStripePayment(Charge charge)
		{
			return null;
		}

		public Payment ProcessStripeSubscriptionUpdate(Subscription stripeSubscription)
		{
			return null;
		}

		public Payment ProcessStripeSubscriptionCancellation(Subscription stripeSubscription)
		{		
			return null;
		}

		public Payment ProcessStripeSubscriptionRefund(Charge stripeCharge)
		{
			return null;
		}

		public Payment ProcessStripeChargeFailed(Charge stripeCharge)
		{
			return null;
		}

		public Session CreateStripeSessionForSub(int departmentId, string stripeCustomerId, string stripePlanId, int planId, string emailAddress, string departmentName)
		{
			return null;
		}

		public Session CreateStripeSessionForUpdate(int departmentId, string stripeCustomerId, string emailAddress, string departmentName)
		{
			return null;
		}

		public Payment ProcessStripeCheckoutCompleted(Session session)
		{
			return null;
		}

		public void ProcessStripeCheckoutUpdate(Session session)
		{
			return;
		}

		public Subscription GetActiveStripeSubscription(string stripeCustomerId)
		{
			return null;
		}

		public Invoice ChangeActiveSubscription(string stripeCustomerId, string stripePlanId)
		{
			return null;
		}
	}
}
