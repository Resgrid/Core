using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.InteropExtensions;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Stripe;
using Newtonsoft.Json;
using Stripe.Checkout;
using Message = Microsoft.Azure.ServiceBus.Message;

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
					//_client = QueueClient.CreateFromConnectionString(Config.ServiceBusConfig.AzureQueueSystemConnectionString, Config.ServiceBusConfig.PaymentQueueName);
					_client = new QueueClient(Config.ServiceBusConfig.AzureQueueSystemConnectionString, Config.ServiceBusConfig.PaymentQueueName);
				}
				catch (TimeoutException) { }
			}
		}

		public void Process(SystemQueueItem item)
		{
			var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
			{
				MaxConcurrentCalls = 1,
				AutoComplete = false
			};

			// Register the function that will process messages
			_client.RegisterMessageHandler(ProcessQueueMessage, messageHandlerOptions);

			//ProcessQueueMessage(_client.Receive());
		}

		public async Task<Tuple<bool, string>> ProcessQueueMessage(Message message, CancellationToken token)
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
							//message.Complete();
							await _client.CompleteAsync(message.SystemProperties.LockToken);
						}

						success = await ProcessPaymentQueueItem(qi);
					}

					try
					{
						if (success)
							await _client.CompleteAsync(message.SystemProperties.LockToken);
							//message.Complete();
					}
					catch (MessageLockLostException)
					{
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					Logging.SendExceptionEmail(ex, "PaymentQueueLogic");

					await _client.AbandonAsync(message.SystemProperties.LockToken); 
					//message.Abandon();
					success = false;
					result = ex.ToString();
				}
			}

			return new Tuple<bool, string>(success, result);
		}

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

		static Task ExceptionReceivedHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
		{
			//Console.WriteLine($"Message handler encountered an exception {exceptionReceivedEventArgs.Exception}.");
			//var context = exceptionReceivedEventArgs.ExceptionReceivedContext;
			//Console.WriteLine("Exception context for troubleshooting:");
			//Console.WriteLine($"- Endpoint: {context.Endpoint}");
			//Console.WriteLine($"- Entity Path: {context.EntityPath}");
			//Console.WriteLine($"- Executing Action: {context.Action}");
			return Task.CompletedTask;
		}
	}
}
