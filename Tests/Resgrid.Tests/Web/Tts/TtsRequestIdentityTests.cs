using System;
using System.Net;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Web.Tts.Configuration;

namespace Resgrid.Tests.Web.Tts
{
	[TestFixture]
	public class TtsRequestIdentityTests
	{
		private string _originalIngressProxyNetwork;
		private int _originalIngressProxyNetworkCidr;

		[SetUp]
		public void SetUp()
		{
			_originalIngressProxyNetwork = WebConfig.IngressProxyNetwork;
			_originalIngressProxyNetworkCidr = WebConfig.IngressProxyNetworkCidr;
		}

		[TearDown]
		public void TearDown()
		{
			WebConfig.IngressProxyNetwork = _originalIngressProxyNetwork;
			WebConfig.IngressProxyNetworkCidr = _originalIngressProxyNetworkCidr;
		}

		[Test]
		public void configure_forwarded_headers_should_register_configured_ingress_proxy_network()
		{
			WebConfig.IngressProxyNetwork = "10.42.0.0";
			WebConfig.IngressProxyNetworkCidr = 16;
			var configureMethod = typeof(TtsRequestIdentity)
				.GetMethod(nameof(TtsRequestIdentity.ConfigureForwardedHeaders), BindingFlags.Public | BindingFlags.Static)!;
			var optionsType = configureMethod.GetParameters()[0].ParameterType;
			var options = Activator.CreateInstance(optionsType!);

			configureMethod.Invoke(null, new object[] { options! });

			optionsType!.GetProperty("ForwardedHeaders")!.GetValue(options)!.ToString().Should().Be("XForwardedFor, XForwardedProto");

			var knownNetworks = ((System.Collections.IEnumerable)optionsType.GetProperty("KnownNetworks")!.GetValue(options)!)
				.Cast<object>()
				.Select(x => new
				{
					Prefix = (IPAddress)x.GetType().GetProperty("Prefix")!.GetValue(x)!,
					PrefixLength = (int)x.GetType().GetProperty("PrefixLength")!.GetValue(x)!
				})
				.ToList();

			knownNetworks.Should().Contain(x => x.Prefix.Equals(IPAddress.Parse("10.42.0.0")) && x.PrefixLength == 16);
			knownNetworks.Should().Contain(x => x.Prefix.Equals(IPAddress.Parse("::ffff:10.42.0.0")) && x.PrefixLength == 16);
		}

		[Test]
		public void resolve_rate_limit_partition_key_should_use_normalized_client_ip()
		{
			var httpContext = new DefaultHttpContext();
			httpContext.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:203.0.113.10");

			var clientId = TtsRequestIdentity.ResolveRateLimitPartitionKey(httpContext);

			clientId.Should().Be("203.0.113.10");
		}

		[Test]
		public void resolve_rate_limit_partition_key_should_fall_back_to_unknown_without_client_ip()
		{
			var httpContext = new DefaultHttpContext();

			var clientId = TtsRequestIdentity.ResolveRateLimitPartitionKey(httpContext);

			clientId.Should().Be("unknown");
		}
	}
}
