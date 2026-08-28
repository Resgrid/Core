using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Providers.ProtectedData;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// App-tier broker client transport rules: HTTPS only (the request carries the workload key,
	/// the caller's grant and protected field values), and every configuration fault reads as
	/// "broker unavailable" — fail closed, never a partial fallback.
	/// </summary>
	[TestFixture]
	public class ProtectedDataBrokerClientTests
	{
		private string _originalBaseUrl;

		[SetUp]
		public void SetUp()
		{
			_originalBaseUrl = DataProtectionConfig.BrokerBaseUrl;
		}

		[TearDown]
		public void TearDown()
		{
			DataProtectionConfig.BrokerBaseUrl = _originalBaseUrl;
		}

		private sealed class RefusingHandler : HttpMessageHandler
		{
			public int Requests;

			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				Requests++;
				throw new HttpRequestException("no network in tests");
			}
		}

		[Test]
		public async Task Plaintext_http_broker_url_reads_as_unconfigured_and_never_sends()
		{
			DataProtectionConfig.BrokerBaseUrl = "http://broker.test:8080";
			var handler = new RefusingHandler();
			using var client = new ProtectedDataBrokerClient(handler);

			client.IsConfigured.Should().BeFalse();
			(await client.IsHealthyAsync()).Should().BeFalse();

			var result = await client.DecryptAsync(42, "grant", "req-1",
				new[] { new Resgrid.Model.Providers.ProtectedFieldOperationItem { FieldId = "f", RowKey = "1", Value = "rgdp:1:1:AAAA" } });

			result.Success.Should().BeFalse();
			result.ErrorCode.Should().Be("broker_unavailable");
			handler.Requests.Should().Be(0, "no byte may leave the host toward a plaintext broker endpoint");
		}

		[Test]
		public void Https_broker_url_is_configured()
		{
			DataProtectionConfig.BrokerBaseUrl = "https://broker.test:8443";
			using var client = new ProtectedDataBrokerClient(new RefusingHandler());

			client.IsConfigured.Should().BeTrue();
		}

		[Test]
		public async Task Empty_broker_url_reads_as_unconfigured()
		{
			DataProtectionConfig.BrokerBaseUrl = "";
			using var client = new ProtectedDataBrokerClient(new RefusingHandler());

			client.IsConfigured.Should().BeFalse();
			(await client.IsHealthyAsync()).Should().BeFalse();
		}
	}
}
