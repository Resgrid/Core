using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services.Invoicing;
using PaymentConnectConfig = Resgrid.Config.PaymentConnectConfig;

namespace Resgrid.Tests.Services
{
	/// <summary>Plan B2.5a / B2.7 "Health": the Stripe Connect webhook health read behind the v4 Health endpoint.</summary>
	[TestFixture]
	public class InvoicePaymentsServiceHealthTests
	{
		private bool _enabled;
		private string _secretKey;
		private string _webhookSecret;
		private string _publicBaseUrl;
		private bool _probeEnabled;
		private bool _liveMode;

		private Mock<IFeatureToggleService> _toggles;
		private Mock<IStripeConnectEndpointProbe> _probe;

		[SetUp]
		public void SetUp()
		{
			_enabled = PaymentConnectConfig.Enabled;
			_secretKey = PaymentConnectConfig.StripeSecretKey;
			_webhookSecret = PaymentConnectConfig.StripeConnectWebhookSecret;
			_publicBaseUrl = PaymentConnectConfig.PublicBaseUrl;
			_probeEnabled = PaymentConnectConfig.WebhookEndpointProbeEnabled;
			_liveMode = PaymentConnectConfig.StripeLiveMode;

			PaymentConnectConfig.Enabled = true;
			PaymentConnectConfig.StripeSecretKey = "sk_test_platform";
			PaymentConnectConfig.StripeConnectWebhookSecret = "whsec_connect";
			PaymentConnectConfig.PublicBaseUrl = "https://api.example.test/";
			PaymentConnectConfig.WebhookEndpointProbeEnabled = true;
			PaymentConnectConfig.StripeLiveMode = false;

			_toggles = new Mock<IFeatureToggleService>();
			_probe = new Mock<IStripeConnectEndpointProbe>();
			InvoicePaymentsService.ResetEndpointProbeCache();
		}

		[TearDown]
		public void TearDown()
		{
			PaymentConnectConfig.Enabled = _enabled;
			PaymentConnectConfig.StripeSecretKey = _secretKey;
			PaymentConnectConfig.StripeConnectWebhookSecret = _webhookSecret;
			PaymentConnectConfig.PublicBaseUrl = _publicBaseUrl;
			PaymentConnectConfig.WebhookEndpointProbeEnabled = _probeEnabled;
			PaymentConnectConfig.StripeLiveMode = _liveMode;
			InvoicePaymentsService.ResetEndpointProbeCache();
		}

		private InvoicePaymentsService Build() => new InvoicePaymentsService(_toggles.Object, _probe.Object);

		private void ClusterFlag(bool? enabledGlobally, bool archived = false)
		{
			var flag = enabledGlobally.HasValue
				? new FeatureFlag { FlagKey = FeatureFlagKeys.PaymentsStripeConnect, IsEnabledGlobally = enabledGlobally.Value, IsArchived = archived }
				: null;
			_toggles.Setup(t => t.GetFlagByKeyAsync(FeatureFlagKeys.PaymentsStripeConnect, It.IsAny<bool>())).ReturnsAsync(flag);
		}

		[Test]
		public async Task Config_off_reports_disabled_and_healthy_without_touching_the_flag_or_stripe()
		{
			PaymentConnectConfig.Enabled = false;
			ClusterFlag(true);

			var health = await Build().GetWebhookHealthAsync();

			health.Enabled.Should().BeFalse();
			health.Healthy.Should().BeTrue();
			health.WebhookConfigured.Should().BeFalse();
			health.EndpointRegistered.Should().BeNull();
			_toggles.Verify(t => t.GetFlagByKeyAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
			_probe.Verify(p => p.IsEndpointRegisteredAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
		}

		[Test]
		public async Task Cluster_flag_missing_or_off_reports_disabled_and_healthy()
		{
			ClusterFlag(null);
			(await Build().GetWebhookHealthAsync()).Enabled.Should().BeFalse();

			ClusterFlag(false);
			var health = await Build().GetWebhookHealthAsync();
			health.Enabled.Should().BeFalse();
			health.Healthy.Should().BeTrue();

			ClusterFlag(true, archived: true);
			(await Build().GetWebhookHealthAsync()).Enabled.Should().BeFalse();

			_probe.Verify(p => p.IsEndpointRegisteredAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
		}

		[Test]
		public async Task Enabled_without_a_webhook_secret_is_not_configured_and_unhealthy()
		{
			ClusterFlag(true);
			PaymentConnectConfig.StripeConnectWebhookSecret = "";

			var health = await Build().GetWebhookHealthAsync();

			health.Enabled.Should().BeTrue();
			health.WebhookConfigured.Should().BeFalse();
			health.EndpointRegistered.Should().BeNull();
			health.Healthy.Should().BeFalse();
			_probe.Verify(p => p.IsEndpointRegisteredAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
		}

		[Test]
		public async Task Enabled_and_configured_asks_stripe_with_the_cluster_webhook_url_and_required_events()
		{
			ClusterFlag(true);
			_probe.Setup(p => p.IsEndpointRegisteredAsync("https://api.example.test/api/PaymentWebhooks/stripe", false, InvoicePaymentsService.StripeConnectRequiredEvents))
				.ReturnsAsync(true);

			var health = await Build().GetWebhookHealthAsync();

			health.Enabled.Should().BeTrue();
			health.WebhookConfigured.Should().BeTrue();
			health.EndpointRegistered.Should().BeTrue();
			health.Healthy.Should().BeTrue();
		}

		[Test]
		public async Task Missing_endpoint_is_unhealthy_but_an_unknown_probe_result_is_not()
		{
			ClusterFlag(true);
			_probe.Setup(p => p.IsEndpointRegisteredAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync(false);
			(await Build().GetWebhookHealthAsync()).Healthy.Should().BeFalse();

			InvoicePaymentsService.ResetEndpointProbeCache();
			_probe.Setup(p => p.IsEndpointRegisteredAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync((bool?)null);
			var unknown = await Build().GetWebhookHealthAsync();
			unknown.EndpointRegistered.Should().BeNull();
			unknown.Healthy.Should().BeTrue();
		}

		[Test]
		public async Task Probe_failure_degrades_to_unknown_and_the_call_still_succeeds()
		{
			ClusterFlag(true);
			_probe.Setup(p => p.IsEndpointRegisteredAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<string>>()))
				.ThrowsAsync(new System.Net.Http.HttpRequestException("stripe unreachable"));

			var health = await Build().GetWebhookHealthAsync();

			health.Enabled.Should().BeTrue();
			health.WebhookConfigured.Should().BeTrue();
			health.EndpointRegistered.Should().BeNull();
			health.Healthy.Should().BeTrue();
		}

		[Test]
		public async Task Probe_result_is_cached_per_process_for_fifteen_minutes()
		{
			ClusterFlag(true);
			_probe.Setup(p => p.IsEndpointRegisteredAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync(true);

			await Build().GetWebhookHealthAsync();
			await Build().GetWebhookHealthAsync();

			_probe.Verify(p => p.IsEndpointRegisteredAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Once);
		}

		[Test]
		public async Task Probe_is_skipped_when_disabled_by_configuration()
		{
			ClusterFlag(true);
			PaymentConnectConfig.WebhookEndpointProbeEnabled = false;

			var health = await Build().GetWebhookHealthAsync();

			health.EndpointRegistered.Should().BeNull();
			health.Healthy.Should().BeTrue();
			_probe.Verify(p => p.IsEndpointRegisteredAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
		}

		[Test]
		public void Webhook_url_is_built_from_the_public_base_url_only()
		{
			PaymentConnectConfig.PublicBaseUrl = "https://api.example.test///";
			PaymentConnectConfig.GetWebhookUrl().Should().Be("https://api.example.test/api/PaymentWebhooks/stripe");

			PaymentConnectConfig.PublicBaseUrl = "";
			PaymentConnectConfig.GetWebhookUrl().Should().BeEmpty();
		}
	}
}
