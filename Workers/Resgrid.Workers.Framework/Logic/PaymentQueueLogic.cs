using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.ServiceBus.Messaging;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Stripe;
using Resgrid.Model.Repositories;
using Resgrid.Model.Events;
using KellermanSoftware.CompareNetObjects;
using Microsoft.AspNet.Identity.EntityFramework6;
using System.Linq;
using System.Collections.Generic;
using Resgrid.Model.Providers;
using Newtonsoft.Json;
using Stripe.Checkout;

namespace Resgrid.Workers.Framework.Logic
{
	public class PaymentQueueLogic
	{
		private QueueClient _client = null;

		public PaymentQueueLogic()
		{
			while (_client == null)
			{
				try
				{
					_client = QueueClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureQueueSystemConnectionString, Config.ServiceBusConfig.PaymentQueueName);
				}
				catch (TimeoutException) { }
			}
		}

		public void Process(SystemQueueItem item)
		{
			ProcessQueueMessage(_client.Receive());
		}

		public static Tuple<bool, string> ProcessQueueMessage(BrokeredMessage message)
		{
			bool success = true;
			string result = "";

			if (message != null)
			{
				try
				{
					var body = message.GetBody<string>();

					if (!String.IsNullOrWhiteSpace(body))
					{
						CqrsEvent qi = null;
						try
						{
							qi = ObjectSerialization.Deserialize<CqrsEvent>(body);
						}
						catch (Exception ex)
						{
							success = false;
							result = "Unable to parse message body Exception: " + ex.ToString();
							message.Complete();
						}

						success = ProcessPaymentQueueItem(qi);
					}

					try
					{
						if (success)
							message.Complete();
					}
					catch (MessageLockLostException)
					{
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					Logging.SendExceptionEmail(ex, "PaymentQueueLogic");

					message.Abandon();
					success = false;
					result = ex.ToString();
				}
			}

			return new Tuple<bool, string>(success, result);
		}

		public static bool ProcessPaymentQueueItem(CqrsEvent qi)
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

								paymentProviderService.ProcessStripePayment(succeededCharge);
							}
							break;
						case CqrsEventTypes.StripeChargeFailed:
							var failedCharge = JsonConvert.DeserializeObject<Charge>(qi.Data);

							if (failedCharge != null)
							{
								var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

								paymentProviderService.ProcessStripeChargeFailed(failedCharge);
							}
							break;
						case CqrsEventTypes.StripeChargeRefunded:
							var refundedCharge = JsonConvert.DeserializeObject<Charge>(qi.Data);

							if (refundedCharge != null)
							{
								var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

								paymentProviderService.ProcessStripeSubscriptionRefund(refundedCharge);
							}
							break;
						case CqrsEventTypes.StripeSubUpdated:
							var updatedSubscription = JsonConvert.DeserializeObject<Subscription>(qi.Data);

							if (updatedSubscription != null)
							{
								var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

								paymentProviderService.ProcessStripeSubscriptionUpdate(updatedSubscription);
							}
							break;
						case CqrsEventTypes.StripeSubDeleted:
							var deletedSubscription = JsonConvert.DeserializeObject<Subscription>(qi.Data);

							if (deletedSubscription != null)
							{
								var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

								paymentProviderService.ProcessStripeSubscriptionCancellation(deletedSubscription);
							}
							break;
						case CqrsEventTypes.StripeCheckoutCompleted:
							var stripeCheckoutSession = JsonConvert.DeserializeObject<Session>(qi.Data);

							if (stripeCheckoutSession != null)
							{
								var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

								paymentProviderService.ProcessStripeCheckoutCompleted(stripeCheckoutSession);
							}
							break;
						case CqrsEventTypes.StripeCheckoutUpdated:
							var stripeCheckoutSessionUpdated = JsonConvert.DeserializeObject<Session>(qi.Data);

							if (stripeCheckoutSessionUpdated != null)
							{
								var paymentProviderService = Bootstrapper.GetKernel().Resolve<IPaymentProviderService>();

								paymentProviderService.ProcessStripeCheckoutUpdate(stripeCheckoutSessionUpdated);
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
