using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class LegacySamlRelayTests
	{
		private const string Nonce = "3f2b8c1e-5d7a-4e9b-8a61-0c4d2e7f9b13";

		[Test]
		public void An_app_relay_state_returns_to_that_app_and_is_echoed()
		{
			var appReturn = LegacySamlRelay.For($"dispatch.{Nonce}");

			appReturn.Client.Should().Be("dispatch");
			appReturn.CallbackTarget.Should().Be("resgriddispatch://auth/callback");
			appReturn.EchoedRelayState.Should().Be($"dispatch.{Nonce}");
		}

		[Test]
		public void No_relay_state_returns_to_responder_with_nothing_to_echo()
		{
			var appReturn = LegacySamlRelay.For(null);

			appReturn.Client.Should().Be(LegacySamlRelay.DefaultClient);
			appReturn.CallbackTarget.Should().Be("resgrid://auth/callback");
			appReturn.EchoedRelayState.Should().BeNull();
		}

		[Test]
		public void The_nonce_must_be_16_to_64_url_safe_characters()
		{
			LegacySamlRelay.For("ic." + new string('a', 15)).EchoedRelayState.Should().BeNull();
			LegacySamlRelay.For("ic." + new string('a', 16)).EchoedRelayState.Should().NotBeNull();
			LegacySamlRelay.For("ic." + new string('a', 64)).EchoedRelayState.Should().NotBeNull();
			LegacySamlRelay.For("ic." + new string('a', 65)).EchoedRelayState.Should().BeNull();
			LegacySamlRelay.For("ic." + new string('a', 20) + "/").EchoedRelayState.Should().BeNull();
			LegacySamlRelay.For("ic." + new string('a', 20) + " ").EchoedRelayState.Should().BeNull();
		}

		[Test]
		public void The_deep_link_escapes_every_value_and_appends_the_echo_last()
		{
			var link = LegacySamlRelay.DeepLink(LegacySamlRelay.For($"unit.{Nonce}"), "saml-relay:ABC", "a+b/c=");

			link.Should().Be($"resgridunit://auth/callback?saml_response=saml-relay%3AABC&department_token=a%2Bb%2Fc%3D&relay_state=unit.{Nonce}");
		}

		[Test]
		public void An_untagged_deep_link_has_no_relay_state()
		{
			LegacySamlRelay.DeepLink(LegacySamlRelay.For(""), "saml-relay:ABC", "t").Should().Be("resgrid://auth/callback?saml_response=saml-relay%3AABC&department_token=t");
		}
	}
}
