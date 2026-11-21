using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Providers.NumberProvider;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// A dedicated number points its webhooks at the configured API host, with the HTTP methods Web.Services' TwilioController accepts,
	/// and carries the country's regulatory bundle when the operator set one up.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class DedicatedNumberCarrierSettingsTests
	{
		private (string Api, string Bundles) _saved;

		[SetUp]
		public void SetUp() => _saved = (SystemBehaviorConfig.ResgridApiBaseUrl, DedicatedNumberAddonConfig.RegulatoryBundles);

		[TearDown]
		public void TearDown() => (SystemBehaviorConfig.ResgridApiBaseUrl, DedicatedNumberAddonConfig.RegulatoryBundles) = _saved;

		[Test]
		public void Webhooks_follow_the_configured_api_host_and_the_controller_methods()
		{
			SystemBehaviorConfig.ResgridApiBaseUrl = "https://api.resgrid.example/";

			DedicatedNumberCarrierSettings.SmsUrl().ToString().Should().Be("https://api.resgrid.example/api/Twilio/IncomingMessage");
			DedicatedNumberCarrierSettings.VoiceUrl().ToString().Should().Be("https://api.resgrid.example/api/Twilio/InboundVoice");
			DedicatedNumberCarrierSettings.SmsMethod.Should().Be("POST", "IncomingMessage reads Twilio's form body on POST");
			DedicatedNumberCarrierSettings.VoiceMethod.Should().Be("GET", "InboundVoice is [HttpGet] and binds from the query string");
		}

		[TestCase("")]
		[TestCase("resgridapi")]
		[TestCase("http://api.resgrid.example")]
		public void A_missing_or_insecure_api_host_stops_the_purchase(string baseUrl)
		{
			SystemBehaviorConfig.ResgridApiBaseUrl = baseUrl;

			DedicatedNumberCarrierSettings.SmsUrl().Should().BeNull();
			DedicatedNumberCarrierSettings.VoiceUrl().Should().BeNull();
		}

		[Test]
		public void Regulatory_bundles_are_read_per_country()
		{
			DedicatedNumberAddonConfig.RegulatoryBundles = "GB=BU111, de = BU222;FR=";

			DedicatedNumberCarrierSettings.BundleSidFor("GB").Should().Be("BU111");
			DedicatedNumberCarrierSettings.BundleSidFor("DE").Should().Be("BU222");
			DedicatedNumberCarrierSettings.BundleSidFor("fr").Should().BeNull();
			DedicatedNumberCarrierSettings.BundleSidFor("US").Should().BeNull("US and Canadian numbers need no bundle");

			DedicatedNumberAddonConfig.RegulatoryBundles = "";
			DedicatedNumberCarrierSettings.BundleSidFor("GB").Should().BeNull();
		}
	}
}
