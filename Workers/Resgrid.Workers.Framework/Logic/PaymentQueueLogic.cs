using System;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Stripe;
using Newtonsoft.Json;
using Stripe.Checkout;

namespace Resgrid.Workers.Framework.Logic
{
	public class PaymentQueueLogic
	{
		public static async Task<bool> ProcessPaymentQueueItem(CqrsEvent qi)
		{
			bool success = true;

			if (qi != null)
			{
				try
				{
					switch ((CqrsEventTypes)qi.Type)
					{
						case CqrsEventTypes.None:
							break;
						case CqrsEventTypes.StripeChargeSucceeded:
							var succeededCharge = JsonConvert.DeserializeObject<Charge>(qi.Data);

							if (succeededCharge != null)
							{
								var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

								await paymentProviderService.ProcessStripePaymentAsync(succeededCharge);
							}
							break;
						case CqrsEventTypes.StripeChargeFailed:
							var failedCharge = JsonConvert.DeserializeObject<Charge>(qi.Data);

							if (failedCharge != null)
							{
								var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

								await paymentProviderService.ProcessStripeChargeFailedAsync(failedCharge);
							}
							break;
						case CqrsEventTypes.StripeChargeRefunded:
							var refundedCharge = JsonConvert.DeserializeObject<Charge>(qi.Data);

							if (refundedCharge != null)
							{
								var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

								await paymentProviderService.ProcessStripeSubscriptionRefundAsync(refundedCharge);
							}
							break;
						case CqrsEventTypes.StripeSubUpdated:
							var updatedSubscription = JsonConvert.DeserializeObject<Subscription>(qi.Data);

							if (updatedSubscription != null)
							{
								var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

								await paymentProviderService.ProcessStripeSubscriptionUpdateAsync(updatedSubscription);
							}
							break;
						case CqrsEventTypes.StripeSubDeleted:
							var deletedSubscription = JsonConvert.DeserializeObject<Subscription>(qi.Data);

							if (deletedSubscription != null)
							{
								var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

								await paymentProviderService.ProcessStripeSubscriptionCancellationAsync(deletedSubscription);
							}
							break;
						case CqrsEventTypes.StripeCheckoutCompleted:
							var stripeCheckoutSession = JsonConvert.DeserializeObject<Session>(qi.Data);

							if (stripeCheckoutSession != null)
							{
								var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

								await paymentProviderService.ProcessStripeCheckoutCompletedAsync(stripeCheckoutSession);
							}
							break;
						case CqrsEventTypes.StripeCheckoutUpdated:
							var stripeCheckoutSessionUpdated = JsonConvert.DeserializeObject<Session>(qi.Data);

							if (stripeCheckoutSessionUpdated != null)
							{
								var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

								await paymentProviderService.ProcessStripeCheckoutUpdateAsync(stripeCheckoutSessionUpdated);
							}
							break;
						case CqrsEventTypes.StripeInvoicePaid:
							var invoicePaid = JsonConvert.DeserializeObject<Invoice>(qi.Data);

							if (invoicePaid != null)
							{
								var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

								await paymentProviderService.ProcessStripeInvoicePaidAsync(invoicePaid);
							}
							break;

						// ADP addon billing events (plan 17.2). The Billing API resolves the department
						// and emits a typed event; Core applies it to the durable protection state.
						// Every one of these is idempotent in the service - providers retry.
						case CqrsEventTypes.AdpAddonActivated:
						case CqrsEventTypes.AdpAddonRenewed:
						case CqrsEventTypes.AdpAddonCancelled:
						case CqrsEventTypes.AdpAddonPaymentFailed:
							var adpEvent = JsonConvert.DeserializeObject<AdpAddonBillingEvent>(qi.Data);

							if (adpEvent != null)
							{
								var dataProtectionService = Bootstrapper.GetKernel().Resolve<IDepartmentDataProtectionService>();

								var adpResult = await dataProtectionService.ApplyAddonBillingEventAsync(adpEvent);

								// Value-free by construction: a department id, the event kind and the
								// outcome. Nothing about the department's data is knowable from here.
								Logging.LogInfo($"ADP billing event {adpEvent.Kind} for department " +
									$"{adpEvent.DepartmentId} applied with result {adpResult}.");
							}
							break;

						default:
							throw new ArgumentOutOfRangeException();
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					Logging.SendExceptionEmail(ex, "ProcessPaymentQueueItem");
				}
			}

			return success;
		}
	}
}
