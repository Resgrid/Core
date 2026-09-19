using System;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Invoicing;
using Resgrid.Providers.Payments;
using Stripe;
using PaymentConnectConfig = Resgrid.Config.PaymentConnectConfig;

namespace Resgrid.Tests.Providers
{
	/// <summary>Plan B2.7: signature fixtures and envelope parsing for every consumed Connect event, without touching the network.</summary>
	[TestFixture]
	public class StripeConnectPaymentProviderTests
	{
		private const string Secret = "whsec_connect_unit_test";
		private string _savedSecret; private string _savedKey; private string _savedClient;

		[SetUp]
		public void SetUp()
		{
			_savedSecret = PaymentConnectConfig.StripeConnectWebhookSecret; _savedKey = PaymentConnectConfig.StripeSecretKey; _savedClient = PaymentConnectConfig.StripeClientId;
			PaymentConnectConfig.StripeConnectWebhookSecret = Secret;
			PaymentConnectConfig.StripeSecretKey = "sk_test_platform";
			PaymentConnectConfig.StripeClientId = "ca_unit";
		}

		[TearDown]
		public void TearDown()
		{
			PaymentConnectConfig.StripeConnectWebhookSecret = _savedSecret; PaymentConnectConfig.StripeSecretKey = _savedKey; PaymentConnectConfig.StripeClientId = _savedClient;
		}

		private static string Sign(string payload, string secret, DateTimeOffset? at = null)
		{
			var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
			using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
			var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
			return $"t={timestamp},v1={signature}";
		}

		private static string EventJson(string type, string dataObject, string account = "acct_1ABC", bool livemode = false) =>
			$"{{\"id\":\"evt_{type.Replace('.', '_')}\",\"object\":\"event\",\"api_version\":\"2020-08-27\",\"created\":1700000000,\"pending_webhooks\":1,\"request\":{{\"id\":null,\"idempotency_key\":null}},\"livemode\":{(livemode ? "true" : "false")},\"type\":\"{type}\",\"account\":\"{account}\",\"data\":{{\"object\":{dataObject}}}}}";

		private const string SessionPaid = "{\"id\":\"cs_1\",\"object\":\"checkout.session\",\"payment_status\":\"paid\",\"status\":\"complete\",\"amount_total\":25000,\"currency\":\"usd\",\"payment_intent\":\"pi_1\",\"client_reference_id\":\"req-1\",\"customer_details\":{\"email\":\"payer@customer.test\"}}";
		private const string SessionUnpaid = "{\"id\":\"cs_2\",\"object\":\"checkout.session\",\"payment_status\":\"unpaid\",\"status\":\"complete\",\"amount_total\":25000,\"currency\":\"usd\",\"payment_intent\":\"pi_2\"}";

		[Test]
		public void A_valid_signature_yields_an_envelope_and_the_body_is_never_trusted_otherwise()
		{
			var provider = new StripeConnectPaymentProvider();
			var body = EventJson("checkout.session.completed", SessionPaid);

			provider.TryParseWebhook(body, Sign(body, Secret), out var envelope, out var error).Should().BeTrue(error);
			envelope.Kind.Should().Be(PaymentEventKinds.PaymentSucceeded);
			envelope.ExternalAccountId.Should().Be("acct_1ABC");
			envelope.ExternalReference.Should().Be("cs_1"); envelope.PaymentIntentId.Should().Be("pi_1");
			envelope.Amount.Should().Be(250m); envelope.Currency.Should().Be("USD"); envelope.PayerEmail.Should().Be("payer@customer.test");
			envelope.LiveMode.Should().BeFalse();

			provider.TryParseWebhook(body.Replace("25000", "25001"), Sign(body, Secret), out _, out error).Should().BeFalse("tampered body"); error.Should().StartWith("signature_invalid");
			provider.TryParseWebhook(body, Sign(body, Secret, DateTimeOffset.UtcNow.AddMinutes(-10)), out _, out error).Should().BeFalse("stale timestamp"); error.Should().StartWith("signature_invalid");
			provider.TryParseWebhook(body, Sign(body, "whsec_saas_endpoint"), out _, out error).Should().BeFalse("the SaaS endpoint's secret must never verify a Connect event"); error.Should().StartWith("signature_invalid");
			provider.TryParseWebhook(body, null, out _, out error).Should().BeFalse(); error.Should().Be("signature_missing");
			provider.TryParseWebhook("not json", Sign("not json", Secret), out _, out error).Should().BeFalse();

			PaymentConnectConfig.StripeConnectWebhookSecret = "";
			provider.TryParseWebhook(body, Sign(body, Secret), out _, out error).Should().BeFalse(); error.Should().Be("webhook_secret_missing");
		}

		[Test]
		public void Every_consumed_event_type_maps_to_its_kind()
		{
			Parse("checkout.session.completed", SessionUnpaid).Kind.Should().Be(PaymentEventKinds.PaymentProcessing, "an unpaid completed session is an asynchronous method still clearing");
			Parse("checkout.session.async_payment_succeeded", SessionUnpaid).Kind.Should().Be(PaymentEventKinds.PaymentSucceeded);
			Parse("checkout.session.async_payment_failed", SessionUnpaid).Kind.Should().Be(PaymentEventKinds.PaymentFailed);
			var expired = Parse("checkout.session.expired", SessionUnpaid);
			expired.Kind.Should().Be(PaymentEventKinds.RequestExpired); expired.ExternalReference.Should().Be("cs_2");

			var intent = Parse("payment_intent.succeeded", "{\"id\":\"pi_9\",\"object\":\"payment_intent\",\"amount\":10000,\"amount_received\":10000,\"currency\":\"cad\",\"latest_charge\":\"ch_9\",\"receipt_email\":\"r@x.test\"}");
			intent.Kind.Should().Be(PaymentEventKinds.PaymentSucceeded); intent.PaymentIntentId.Should().Be("pi_9"); intent.ChargeId.Should().Be("ch_9"); intent.Amount.Should().Be(100m); intent.Currency.Should().Be("CAD");

			var refund = Parse("charge.refunded", "{\"id\":\"ch_1\",\"object\":\"charge\",\"amount\":25000,\"amount_refunded\":10000,\"currency\":\"usd\",\"payment_intent\":\"pi_1\",\"refunded\":false}");
			refund.Kind.Should().Be(PaymentEventKinds.PaymentRefunded); refund.RefundedAmount.Should().Be(100m); refund.PaymentIntentId.Should().Be("pi_1"); refund.ChargeId.Should().Be("ch_1");

			var dispute = Parse("charge.dispute.created", "{\"id\":\"dp_1\",\"object\":\"dispute\",\"amount\":25000,\"currency\":\"usd\",\"charge\":\"ch_1\",\"payment_intent\":\"pi_1\",\"status\":\"needs_response\"}");
			dispute.Kind.Should().Be(PaymentEventKinds.PaymentDisputed); dispute.DisputeLost.Should().BeFalse(); dispute.ChargeId.Should().Be("ch_1");

			var lost = Parse("charge.dispute.closed", "{\"id\":\"dp_1\",\"object\":\"dispute\",\"amount\":25000,\"currency\":\"usd\",\"charge\":\"ch_1\",\"payment_intent\":\"pi_1\",\"status\":\"lost\"}");
			lost.Kind.Should().Be(PaymentEventKinds.DisputeClosed); lost.DisputeLost.Should().BeTrue();

			var revoked = Parse("account.application.deauthorized", "{\"id\":\"ca_unit\",\"object\":\"application\",\"name\":\"Resgrid\"}");
			revoked.Kind.Should().Be(PaymentEventKinds.ConnectionRevoked); revoked.ExternalAccountId.Should().Be("acct_1ABC");

			var updated = Parse("account.updated", "{\"id\":\"acct_1ABC\",\"object\":\"account\",\"charges_enabled\":false,\"capabilities\":{\"card_payments\":\"active\",\"us_bank_account_ach_payments\":\"inactive\"}}");
			updated.Kind.Should().Be(PaymentEventKinds.ConnectionUpdated); updated.ChargesEnabled.Should().BeFalse();
			updated.Capabilities["card_payments"].Should().BeTrue(); updated.Capabilities["us_bank_account_ach_payments"].Should().BeFalse(); updated.Capabilities["charges_enabled"].Should().BeFalse();

			Parse("customer.created", "{\"id\":\"cus_1\",\"object\":\"customer\"}").Kind.Should().Be(PaymentEventKinds.Unknown);
		}

		[Test]
		public void Live_mode_travels_on_the_envelope()
		{
			Parse("checkout.session.completed", SessionPaid, livemode: true).LiveMode.Should().BeTrue();
		}

		[Test]
		public void Connect_url_carries_the_platform_client_id_state_and_redirect_and_amounts_are_minor_units()
		{
			var url = new StripeConnectPaymentProvider().BuildConnectUrl("st ate", "https://resgrid.local/User/Invoicing/PaymentConnectCallback/stripe");
			url.Should().StartWith(StripeConnectPaymentProvider.AuthorizeEndpoint)
				.And.Contain("client_id=ca_unit").And.Contain("scope=read_write").And.Contain("state=st%20ate")
				.And.Contain("redirect_uri=https%3A%2F%2Fresgrid.local%2FUser%2FInvoicing%2FPaymentConnectCallback%2Fstripe");

			StripeConnectPaymentProvider.ToMinorUnits(19.99m).Should().Be(1999);
			StripeConnectPaymentProvider.ToMinorUnits(0.005m).Should().Be(1);
			StripeConnectPaymentProvider.FromMinorUnits(1999).Should().Be(19.99m);
			StripeConnectPaymentProvider.FromMinorUnits(null).Should().BeNull();

			PaymentConnectConfig.StripeClientId = "";
			Action act = () => new StripeConnectPaymentProvider().BuildConnectUrl("s", "r");
			act.Should().Throw<InvalidOperationException>().WithMessage("payments_provider_unavailable");
		}

		[Test]
		public void The_null_adapter_refuses_everything_and_never_verifies()
		{
			var provider = new NullPaymentConnectProvider();
			provider.IsConfigured.Should().BeFalse();
			Action connect = () => provider.BuildConnectUrl("s", "r");
			connect.Should().Throw<InvalidOperationException>().WithMessage(NullPaymentConnectProvider.DisabledCode);
			provider.TryParseWebhook("{}", "t=1,v1=x", out var envelope, out var error).Should().BeFalse();
			envelope.Should().BeNull(); error.Should().Be(NullPaymentConnectProvider.DisabledCode);
		}

		private static PaymentProviderEventEnvelope Parse(string type, string dataObject, bool livemode = false)
		{
			var stripeEvent = EventUtility.ParseEvent(EventJson(type, dataObject, livemode: livemode), throwOnApiVersionMismatch: false);
			return StripeConnectPaymentProvider.ToEnvelope(stripeEvent);
		}
	}
}
