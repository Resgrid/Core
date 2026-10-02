using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// The relying-party rules that keep every passkey bound to the app that registered it (workbook section 5): one RP ID
	/// per client, no parent-domain RP, exact origins. Any violation turns every passkey gate off.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class RelyingPartyRegistryTests
	{
		private const string AndroidOrigin = "android:apk-key-hash:47DEQpj8HBSa-_TImW-5JCeuQeRkm5NMpJWZG3hSuFU";

		private const string Hosted =
			"web=app.resgrid.com|https://app.resgrid.com;" +
			"responder=responder.resgrid.com|https://responder.resgrid.com," + AndroidOrigin + ";" +
			"unit=unit.resgrid.com|https://unit.resgrid.com;" +
			"dispatch=dispatch.resgrid.com|https://dispatch.resgrid.com;" +
			"ic=ic.resgrid.com|https://ic.resgrid.com";

		private bool _registration, _login, _adp, _approval;

		[SetUp]
		public void SetUp()
		{
			_registration = PasskeyConfig.RegistrationEnabled;
			_login = PasskeyConfig.LoginAcceptanceEnabled;
			_adp = PasskeyConfig.AdpAcceptanceEnabled;
			_approval = PasskeyConfig.ResponderApprovalEnabled;
		}

		[TearDown]
		public void TearDown()
		{
			PasskeyConfig.RegistrationEnabled = _registration;
			PasskeyConfig.LoginAcceptanceEnabled = _login;
			PasskeyConfig.AdpAcceptanceEnabled = _adp;
			PasskeyConfig.ResponderApprovalEnabled = _approval;
		}

		[Test]
		public void The_hosted_layout_validates_with_one_rp_per_client()
		{
			var registry = new RelyingPartyRegistry(Hosted);

			registry.Readiness.IsReady.Should().BeTrue();
			registry.Readiness.Problems.Should().BeEmpty();
			registry.Get(UserSessionClientApplication.Web).RpId.Should().Be("app.resgrid.com");
			registry.Get(UserSessionClientApplication.Command).RpId.Should().Be("ic.resgrid.com", "'ic' is the IC app, recorded as Command");
			registry.Get(UserSessionClientApplication.Responder).Origins.Should().BeEquivalentTo("https://responder.resgrid.com", AndroidOrigin);
		}

		[Test]
		public void A_client_without_an_entry_has_no_relying_party_and_never_borrows_another()
		{
			var registry = new RelyingPartyRegistry("web=app.resgrid.com|https://app.resgrid.com");

			registry.Readiness.IsReady.Should().BeTrue();
			registry.Get(UserSessionClientApplication.Unit).Should().BeNull();
			registry.Get(UserSessionClientApplication.BigBoard).Should().BeNull();
		}

		[Test]
		public void An_empty_configuration_is_not_ready()
		{
			var registry = new RelyingPartyRegistry("");

			registry.Readiness.IsReady.Should().BeFalse();
			registry.Get(UserSessionClientApplication.Web).Should().BeNull();
		}

		[TestCase("unit=app.resgrid.com|https://app.resgrid.com;web=app.resgrid.com|https://app.resgrid.com", TestName = "Two clients sharing one RP ID")]
		[TestCase("web=resgrid.com|https://resgrid.com;unit=unit.resgrid.com|https://unit.resgrid.com", TestName = "A parent-domain RP ID")]
		[TestCase("web=app.resgrid.com|https://app.resgrid.com;web=web.resgrid.com|https://web.resgrid.com", TestName = "Two entries for one client")]
		[TestCase("bigboard=bb.resgrid.com|https://bb.resgrid.com", TestName = "A client that has no passkeys")]
		[TestCase("web=https://app.resgrid.com|https://app.resgrid.com", TestName = "An RP ID with a scheme")]
		[TestCase("web=app.resgrid.com|https://app.resgrid.com/login", TestName = "An origin with a path")]
		[TestCase("web=app.resgrid.com|http://app.resgrid.com", TestName = "Plain http outside local development")]
		[TestCase("web=app.resgrid.com|https://evil.example.com", TestName = "An origin outside the RP ID")]
		[TestCase("web=app.resgrid.com|https://notapp.resgrid.com", TestName = "An origin that only ends with the RP ID text")]
		[TestCase("web=app.resgrid.com|app://bundle", TestName = "A custom-scheme (Electron) origin")]
		[TestCase("unit=unit.resgrid.com|android:apk-key-hash:tooShort", TestName = "A malformed Android origin")]
		[TestCase("web=app.resgrid.com", TestName = "An entry without origins")]
		public void Any_rule_violation_makes_the_whole_configuration_not_ready(string configuration)
		{
			var registry = new RelyingPartyRegistry(configuration);

			registry.Readiness.IsReady.Should().BeFalse();
			registry.Readiness.Problems.Should().NotBeEmpty();
			registry.Get(UserSessionClientApplication.Web).Should().BeNull("nothing is usable from a configuration that failed validation");
		}

		[Test]
		public void Localhost_may_use_plain_http_for_development()
		{
			var registry = new RelyingPartyRegistry("web=localhost|http://localhost:5151");

			registry.Readiness.IsReady.Should().BeTrue();
			registry.Get(UserSessionClientApplication.Web).Origins.Should().BeEquivalentTo("http://localhost:5151");
		}

		[Test]
		public void Origins_are_normalised_to_scheme_host_and_non_default_port()
		{
			var registry = new RelyingPartyRegistry("web=app.resgrid.com|HTTPS://App.Resgrid.com:443/,https://app.resgrid.com:8443");

			registry.Get(UserSessionClientApplication.Web).Origins.Should().BeEquivalentTo("https://app.resgrid.com", "https://app.resgrid.com:8443");
		}

		[Test]
		public void Gates_stay_off_until_the_configuration_is_ready()
		{
			PasskeyConfig.RegistrationEnabled = true;
			PasskeyConfig.LoginAcceptanceEnabled = true;
			PasskeyConfig.AdpAcceptanceEnabled = true;
			PasskeyConfig.ResponderApprovalEnabled = true;

			var broken = new PasskeyFeatureGates(new RelyingPartyRegistry(""));
			broken.RegistrationEnabled.Should().BeFalse();
			broken.LoginAcceptanceEnabled.Should().BeFalse();
			broken.AdpAcceptanceEnabled.Should().BeFalse();
			broken.ResponderApprovalEnabled.Should().BeFalse();

			var ready = new PasskeyFeatureGates(new RelyingPartyRegistry(Hosted));
			ready.RegistrationEnabled.Should().BeTrue();
			ready.LoginAcceptanceEnabled.Should().BeTrue();
		}

		[Test]
		public void Every_gate_ships_off()
		{
			// Guards the defaults themselves: a gate turned on in code instead of configuration would ship enabled.
			foreach (var field in typeof(PasskeyConfig).GetFields())
				if (field.FieldType == typeof(bool))
					((bool)field.GetValue(null)).Should().BeFalse($"PasskeyConfig.{field.Name} must default to off");
		}
	}
}
