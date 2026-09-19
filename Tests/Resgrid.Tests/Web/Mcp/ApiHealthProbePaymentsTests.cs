using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Web.Mcp.Infrastructure;

namespace Resgrid.Tests.Web.Mcp
{
	/// <summary>Plan B2.5a: the MCP health endpoint relays the API's Stripe Connect webhook block and never invents a verdict.</summary>
	[TestFixture]
	public class ApiHealthProbePaymentsTests
	{
		private const string HealthyPayload = @"{
			""Data"": {
				""SiteId"": ""0"", ""ApiVersion"": ""v4"", ""DatabaseOnline"": true, ""CacheOnline"": true,
				""PaymentsStripeConnectEnabled"": true,
				""PaymentsWebhookConfigured"": true,
				""PaymentsWebhookEndpointRegistered"": true,
				""PaymentsWebhookLastReceivedOn"": ""2026-09-18T14:05:00Z"",
				""PaymentsWebhookLastAppliedOn"": null,
				""PaymentsWebhookStale"": false,
				""PaymentsWebhookRejectedLastHour"": 0,
				""PaymentsWebhookFailedLastHour"": 2,
				""PaymentsOverdueOpenRequests"": 1,
				""PaymentsLastReconcileOn"": null,
				""PaymentsWebhookHealthy"": false
			},
			""Status"": ""Success""
		}";

		[Test]
		public void Parses_every_payments_field_from_the_v4_payload()
		{
			var result = ApiHealthProbe.ParsePayments(HealthyPayload);

			result.Available.Should().BeTrue();
			result.StripeConnectEnabled.Should().BeTrue();
			result.WebhookConfigured.Should().BeTrue();
			result.WebhookEndpointRegistered.Should().BeTrue();
			result.WebhookLastReceivedOn.Should().Be(new DateTime(2026, 9, 18, 14, 5, 0, DateTimeKind.Utc));
			result.WebhookLastAppliedOn.Should().BeNull();
			result.WebhookStale.Should().BeFalse();
			result.WebhookRejectedLastHour.Should().Be(0);
			result.WebhookFailedLastHour.Should().Be(2);
			result.OverdueOpenRequests.Should().Be(1);
			result.LastReconcileOn.Should().BeNull();
			result.WebhookHealthy.Should().BeFalse();
		}

		[Test]
		public void Disabled_cluster_relays_enabled_false_and_healthy_true()
		{
			var result = ApiHealthProbe.ParsePayments(@"{ ""Data"": { ""PaymentsStripeConnectEnabled"": false, ""PaymentsWebhookHealthy"": true } }");

			result.Available.Should().BeTrue();
			result.StripeConnectEnabled.Should().BeFalse();
			result.WebhookEndpointRegistered.Should().BeNull();
			result.WebhookHealthy.Should().BeTrue();
		}

		[Test]
		public void An_api_without_the_payments_fields_is_unavailable_not_healthy()
		{
			var result = ApiHealthProbe.ParsePayments(@"{ ""Data"": { ""SiteId"": ""0"", ""DatabaseOnline"": true } }");

			result.Available.Should().BeFalse();
			result.WebhookHealthy.Should().BeNull();
		}

		[TestCase("")]
		[TestCase("not json")]
		[TestCase("{ }")]
		[TestCase(@"{ ""Data"": null }")]
		public void Garbage_or_empty_payloads_are_unavailable(string json)
		{
			var result = ApiHealthProbe.ParsePayments(json);

			result.Available.Should().BeFalse();
			result.WebhookHealthy.Should().BeNull();
		}

		[Test]
		public async Task Reads_the_v4_health_route_and_relays_the_block()
		{
			var handler = new StubHandler(request =>
			{
				request.Method.Should().Be(HttpMethod.Get);
				request.RequestUri.AbsolutePath.Should().Be("/" + ApiHealthProbe.HealthPath);
				return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(HealthyPayload) };
			});
			using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };

			var result = await ApiHealthProbe.ReadPaymentsAsync(client);

			result.Available.Should().BeTrue();
			result.WebhookHealthy.Should().BeFalse();
			result.OverdueOpenRequests.Should().Be(1);
		}

		[Test]
		public async Task Non_success_status_or_transport_failure_is_unavailable_and_never_throws()
		{
			using var failing = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))) { BaseAddress = new Uri("https://api.example.test/") };
			(await ApiHealthProbe.ReadPaymentsAsync(failing)).Available.Should().BeFalse();

			using var throwing = new HttpClient(new StubHandler(_ => throw new HttpRequestException("down"))) { BaseAddress = new Uri("https://api.example.test/") };
			(await ApiHealthProbe.ReadPaymentsAsync(throwing)).Available.Should().BeFalse();

			(await ApiHealthProbe.ReadPaymentsAsync(null)).Available.Should().BeFalse();
		}

		private sealed class StubHandler : HttpMessageHandler
		{
			private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
			public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) { _respond = respond; }
			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
				=> Task.FromResult(_respond(request));
		}
	}
}
