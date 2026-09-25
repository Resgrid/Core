using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// Runs the real ForwardedHeadersMiddleware with the shared configuration: only a peer inside the ingress proxy
	/// network may replace the caller's address with X-Forwarded-For. The IPv4-mapped cases are the ones Kestrel produces
	/// for IPv4 peers on a dual-stack socket, and the ones the previous "::ffff:network / IPv4 prefix" setup got wrong by
	/// trusting every IPv4 client.
	/// </summary>
	[TestFixture]
	public sealed class ForwardedHeadersSetupTests
	{
		private const string ForwardedClient = "198.51.100.9";

		[TestCase("10.42.3.4")]
		[TestCase("::ffff:10.42.3.4")]
		public async Task Proxy_InTheIngressNetwork_ShouldSetTheCallersAddress(string peer)
		{
			var remoteAddress = await ResolveRemoteAddress("10.42.0.0", 16, peer);

			Assert.That(remoteAddress, Is.EqualTo(IPAddress.Parse(ForwardedClient)));
		}

		[TestCase("203.0.113.5")]
		[TestCase("::ffff:203.0.113.5")]
		[TestCase("10.43.0.1")]
		[TestCase("::ffff:10.43.0.1")]
		public async Task Client_OutsideTheIngressNetwork_ShouldNotBeAbleToSpoofItsAddress(string peer)
		{
			var remoteAddress = await ResolveRemoteAddress("10.42.0.0", 16, peer);

			Assert.That(remoteAddress, Is.EqualTo(IPAddress.Parse(peer)));
		}

		/// <summary>
		/// The same boundary checked on the KnownNetworks entries directly. .NET 9's middleware (production) matches a
		/// mapped peer against these Microsoft.AspNetCore.HttpOverrides.IPNetwork entries, which count ::ffff:203.0.113.5 as
		/// inside ::ffff:10.42.0.0/16. .NET 10's middleware matches System.Net.IPNetwork, which does not, so on a .NET 10
		/// test runtime the middleware tests above cannot catch that mistake and this test must.
		/// </summary>
		[TestCase("10.42.3.4", true)]
		[TestCase("::ffff:10.42.3.4", true)]
		[TestCase("203.0.113.5", false)]
		[TestCase("::ffff:203.0.113.5", false)]
		public void KnownNetworks_ShouldContainOnlyTheIngressProxyNetwork(string peer, bool trusted)
		{
			var options = new ForwardedHeadersOptions();

			ForwardedHeadersSetup.Configure(options, "10.42.0.0", 16);

			Assert.That(options.KnownNetworks.Any(x => x.Contains(IPAddress.Parse(peer))), Is.EqualTo(trusted));
		}

		[TestCase("::ffff:10.42.0.0", 16)]
		[TestCase("::ffff:10.42.0.0", 112)]
		public async Task Network_ConfiguredInMappedForm_ShouldTrustOnlyThatNetwork(string network, int cidr)
		{
			Assert.That(await ResolveRemoteAddress(network, cidr, "::ffff:10.42.3.4"), Is.EqualTo(IPAddress.Parse(ForwardedClient)));
			Assert.That(await ResolveRemoteAddress(network, cidr, "::ffff:203.0.113.5"), Is.EqualTo(IPAddress.Parse("::ffff:203.0.113.5")));
		}

		[TestCase("", 16)]
		[TestCase("not-an-address", 16)]
		[TestCase("10.42.0.0", 33)]
		[TestCase("10.42.0.0", -1)]
		public async Task Network_ThatDoesNotParse_ShouldTrustNoProxy(string network, int cidr)
		{
			var remoteAddress = await ResolveRemoteAddress(network, cidr, "10.42.3.4");

			Assert.That(remoteAddress, Is.EqualTo(IPAddress.Parse("10.42.3.4")));
		}

		[Test]
		public void Configure_ShouldHonourForwardedForAndProto()
		{
			var options = new ForwardedHeadersOptions();

			ForwardedHeadersSetup.Configure(options, "10.42.0.0", 16);

			Assert.That(options.ForwardedHeaders, Is.EqualTo(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto));
		}

		private static async Task<IPAddress> ResolveRemoteAddress(string proxyNetwork, int proxyNetworkCidr, string peer)
		{
			var options = new ForwardedHeadersOptions();
			ForwardedHeadersSetup.Configure(options, proxyNetwork, proxyNetworkCidr);

			var context = new DefaultHttpContext();
			context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
			context.Request.Headers["X-Forwarded-For"] = ForwardedClient;

			var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance,
				Microsoft.Extensions.Options.Options.Create(options));
			await middleware.Invoke(context);

			return context.Connection.RemoteIpAddress;
		}
	}
}
