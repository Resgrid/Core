using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Services;
using Resgrid.Services;
using RestSharp;
using RestSharp.Serializers.NewtonsoftJson;

namespace Resgrid.Tests.Services
{
	[TestFixture, NonParallelizable]
	public class ReadinessProBillingClientTests
	{
		[Test]
		public async Task Billing_client_is_lazy_when_unconfigured_and_shared_across_service_scopes()
		{
			var oldUrl = SystemBehaviorConfig.BillingApiBaseUrl; var oldKey = ApiConfig.BackendInternalApikey;
			try
			{
				SystemBehaviorConfig.BillingApiBaseUrl = ""; ApiConfig.BackendInternalApikey = "synthetic-key";
				var builder = new ContainerBuilder(); builder.RegisterModule<ServicesModule>();
				using var container = builder.Build(Autofac.Builder.ContainerBuildOptions.IgnoreStartableComponents); using var first = container.BeginLifetimeScope(); using var second = container.BeginLifetimeScope();
				(await first.Resolve<IReadinessProBillingService>().GetAsync(77)).Should().BeNull();
				SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.invalid";
				first.ResolveNamed<RestClient>("readiness-billing-client").Should().BeSameAs(second.ResolveNamed<RestClient>("readiness-billing-client"));
			}
			finally { SystemBehaviorConfig.BillingApiBaseUrl = oldUrl; ApiConfig.BackendInternalApikey = oldKey; }
		}

		[Test]
		public async Task Consecutive_billing_calls_keep_the_shared_transport_usable_and_preserve_failure_results()
		{
			var oldUrl = SystemBehaviorConfig.BillingApiBaseUrl; var oldKey = ApiConfig.BackendInternalApikey;
			try
			{
				SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.invalid"; ApiConfig.BackendInternalApikey = "synthetic-key";
				var handler = new BillingHandler();
				using var client = new RestClient(new RestClientOptions(SystemBehaviorConfig.BillingApiBaseUrl) { ConfigureMessageHandler = _ => handler }, configureSerialization: s => s.UseNewtonsoftJson());
				var service = new ReadinessProBillingService(() => client);
				(await service.GetAsync(77)).Provider.Should().Be("Stripe");
				(await service.CancelRenewalAsync(77)).Should().BeTrue();
				handler.Fail = true;
				(await service.GetAsync(77)).Should().BeNull(); (await service.CancelRenewalAsync(77)).Should().BeFalse();
				handler.Calls.Should().Be(4);
			}
			finally { SystemBehaviorConfig.BillingApiBaseUrl = oldUrl; ApiConfig.BackendInternalApikey = oldKey; }
		}

		private sealed class BillingHandler : HttpMessageHandler
		{
			public int Calls;
			public bool Fail;
			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				Calls++; request.Headers.GetValues("X-API-Key").Should().ContainSingle().Which.Should().Be("synthetic-key");
				return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
				{
					Content = new StringContent(request.Method == HttpMethod.Post ? "true" : "{\"Provider\":\"Stripe\"}", System.Text.Encoding.UTF8, "application/json")
				});
			}
		}
	}
}
