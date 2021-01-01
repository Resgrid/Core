using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Stripe;
using Stripe.Checkout;

namespace Resgrid.Web.Services.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	[ApiExplorerSettings(IgnoreApi = true)]
	public class StripeHandlerController : ControllerBase
	{
		private readonly IPaymentProviderService _paymentProviderService;
		private readonly IPaymentProvider _cqrsProvider;

		public StripeHandlerController(IPaymentProviderService paymentProviderService, IPaymentProvider cqrsProvider)
		{
			_paymentProviderService = paymentProviderService;
			_cqrsProvider = cqrsProvider;
		}

		[HttpPost]
		public async Task<IActionResult> Index()
		{
			Event stripeEvent = null;
			PaymentProviderEvent providerEvent = null;

			try
			{
				string json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
				stripeEvent = EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"], PaymentProviderConfig.TestWebhookSigningKey, 300L, false);

				providerEvent = new PaymentProviderEvent();
				providerEvent.ProviderType = (int)PaymentMethods.Stripe;
				providerEvent.RecievedOn = DateTime.UtcNow;
				providerEvent.Data = JsonConvert.SerializeObject(stripeEvent);
				providerEvent.Processed = false;
				providerEvent.CustomerId = "SYSTEM";
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest();
			}

			if (stripeEvent == null)
				return BadRequest();

			try
			{
				switch (stripeEvent.Type)
				{
					case "charge.succeeded":
						var succeededCharge = JsonConvert.DeserializeObject<Charge>(stripeEvent.Data.RawObject.ToString());
						providerEvent.CustomerId = succeededCharge.CustomerId;
						providerEvent.Processed = true;
						providerEvent.Type = typeof(Charge).FullName;

						CqrsEvent stripeChargeSucceededEvent = new CqrsEvent();
						stripeChargeSucceededEvent.Type = (int)CqrsEventTypes.StripeChargeSucceeded;
						stripeChargeSucceededEvent.Data = stripeEvent.Data.RawObject.ToString();

#if !DEBUG
						await _cqrsProvider.EnqueuePaymentEventAsync(stripeChargeSucceededEvent);
#else
						await _paymentProviderService.ProcessStripePaymentAsync(succeededCharge);
#endif
						break;
					case "charge.failed":
						var failedCharge = JsonConvert.DeserializeObject<Charge>(stripeEvent.Data.RawObject.ToString());
						providerEvent.CustomerId = failedCharge.CustomerId;
						providerEvent.Type = typeof(Charge).FullName;

						CqrsEvent stripeChargeFailedEvent = new CqrsEvent();
						stripeChargeFailedEvent.Type = (int)CqrsEventTypes.StripeChargeFailed;
						stripeChargeFailedEvent.Data = stripeEvent.Data.RawObject.ToString();

#if !DEBUG
						await _cqrsProvider.EnqueuePaymentEventAsync(stripeChargeFailedEvent);
#else
						await _paymentProviderService.ProcessStripeChargeFailedAsync(failedCharge);
#endif
						break;
					case "charge.refunded":
						var refundedCharge = JsonConvert.DeserializeObject<Charge>(stripeEvent.Data.RawObject.ToString());
						providerEvent.CustomerId = refundedCharge.CustomerId;
						providerEvent.Type = typeof(Charge).FullName;

						CqrsEvent stripeChargeRefundedEvent = new CqrsEvent();
						stripeChargeRefundedEvent.Type = (int)CqrsEventTypes.StripeChargeRefunded;
						stripeChargeRefundedEvent.Data = stripeEvent.Data.RawObject.ToString();

#if !DEBUG
						await _cqrsProvider.EnqueuePaymentEventAsync(stripeChargeRefundedEvent);
#else
						await _paymentProviderService.ProcessStripeSubscriptionRefundAsync(refundedCharge);
#endif
						break;

					case "customer.subscription.updated":
						var updatedSubscription = JsonConvert.DeserializeObject<Subscription>(stripeEvent.Data.RawObject.ToString());
						providerEvent.CustomerId = updatedSubscription.CustomerId;
						providerEvent.Processed = true;
						providerEvent.Type = typeof(Subscription).FullName;

						CqrsEvent stripeSubUpdatedEvent = new CqrsEvent();
						stripeSubUpdatedEvent.Type = (int)CqrsEventTypes.StripeSubUpdated;
						stripeSubUpdatedEvent.Data = stripeEvent.Data.RawObject.ToString();

#if !DEBUG
						await _cqrsProvider.EnqueuePaymentEventAsync(stripeSubUpdatedEvent);
#else
						await _paymentProviderService.ProcessStripeSubscriptionUpdateAsync(updatedSubscription);
#endif
						break;
					case "customer.subscription.deleted":
						var deletedSubscription = JsonConvert.DeserializeObject<Subscription>(stripeEvent.Data.RawObject.ToString());
						providerEvent.CustomerId = deletedSubscription.CustomerId;
						providerEvent.Processed = true;
						providerEvent.Type = typeof(Subscription).FullName;

						CqrsEvent stripeSubDeletedEvent = new CqrsEvent();
						stripeSubDeletedEvent.Type = (int)CqrsEventTypes.StripeSubDeleted;
						stripeSubDeletedEvent.Data = stripeEvent.Data.RawObject.ToString();

#if !DEBUG
						await _cqrsProvider.EnqueuePaymentEventAsync(stripeSubDeletedEvent);
#else
						await _paymentProviderService.ProcessStripeSubscriptionCancellationAsync(deletedSubscription);
#endif

						break;
					case "customer.subscription.created":
						var createdSubscription = JsonConvert.DeserializeObject<Subscription>(stripeEvent.Data.RawObject.ToString());
						providerEvent.CustomerId = createdSubscription.CustomerId;
						providerEvent.Type = typeof(Subscription).FullName;
						break;
					case "checkout.session.completed":
						var session = JsonConvert.DeserializeObject<Session>(stripeEvent.Data.RawObject.ToString());
						providerEvent.CustomerId = session.CustomerId;
						providerEvent.Processed = true;
						providerEvent.Type = typeof(Session).FullName;

						CqrsEvent stripeSessionCompletedEvent = new CqrsEvent();
						stripeSessionCompletedEvent.Type = (int)CqrsEventTypes.StripeCheckoutCompleted;
						stripeSessionCompletedEvent.Data = stripeEvent.Data.RawObject.ToString();

						if (!String.IsNullOrWhiteSpace(session.Mode) && session.Mode.ToLower() == "setup")
						{
							var service = new SetupIntentService();
							var setupIntent = service.Get(session.SetupIntentId);
							providerEvent.CustomerId = setupIntent.Metadata["customer_id"];

							stripeSessionCompletedEvent.Type = (int)CqrsEventTypes.StripeCheckoutUpdated;
#if !DEBUG
							await _cqrsProvider.EnqueuePaymentEventAsync(stripeSessionCompletedEvent);
#else
							await _paymentProviderService.ProcessStripeCheckoutUpdateAsync(session);
#endif
						}
						break;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				Logging.SendExceptionEmail(ex, "StripeHandler");
				return BadRequest();
			}
			finally
			{
				await _paymentProviderService.SaveEventAsync(providerEvent);
			}

			return Ok();
		}
	}
}
