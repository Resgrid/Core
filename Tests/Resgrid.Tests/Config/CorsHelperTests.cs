using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;

namespace Resgrid.Tests.Config
{
	[TestFixture]
	public class CorsHelperTests
	{
		private string _originalBaseUrl;
		private string _originalApiBaseUrl;
		private string _originalEventingBaseUrl;
		private string _originalCorsAllowedOrigins;

		[SetUp]
		public void SetUp()
		{
			_originalBaseUrl = SystemBehaviorConfig.ResgridBaseUrl;
			_originalApiBaseUrl = SystemBehaviorConfig.ResgridApiBaseUrl;
			_originalEventingBaseUrl = SystemBehaviorConfig.ResgridEventingBaseUrl;
			_originalCorsAllowedOrigins = ApiConfig.CorsAllowedOrigins;

			SystemBehaviorConfig.ResgridBaseUrl = "https://qaweb.resgrid.dev";
			SystemBehaviorConfig.ResgridApiBaseUrl = "https://qaapi.resgrid.dev";
			SystemBehaviorConfig.ResgridEventingBaseUrl = "https://qaevents.resgrid.dev";
			ApiConfig.CorsAllowedOrigins = "";
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.ResgridBaseUrl = _originalBaseUrl;
			SystemBehaviorConfig.ResgridApiBaseUrl = _originalApiBaseUrl;
			SystemBehaviorConfig.ResgridEventingBaseUrl = _originalEventingBaseUrl;
			ApiConfig.CorsAllowedOrigins = _originalCorsAllowedOrigins;
		}

		[Test]
		public void should_allow_configured_base_hosts_and_their_subdomains()
		{
			CorsHelper.IsAllowedOrigin("https://qaapi.resgrid.dev").Should().BeTrue();
			CorsHelper.IsAllowedOrigin("https://sub.qaweb.resgrid.dev").Should().BeTrue();
		}

		[Test]
		public void should_allow_sibling_apps_under_the_shared_parent_domain()
		{
			// qadispatch.resgrid.dev is not a subdomain of any configured base host, but it
			// shares the resgrid.dev parent - this is the dispatch/unit/responder web case.
			CorsHelper.IsAllowedOrigin("https://qadispatch.resgrid.dev").Should().BeTrue();
			CorsHelper.IsAllowedOrigin("https://resgrid.dev").Should().BeTrue();
		}

		[Test]
		public void should_allow_subdomains_when_base_url_is_already_the_apex()
		{
			SystemBehaviorConfig.ResgridBaseUrl = "https://resgrid.com";
			SystemBehaviorConfig.ResgridApiBaseUrl = "https://api.resgrid.com";
			SystemBehaviorConfig.ResgridEventingBaseUrl = "https://events.resgrid.com";

			CorsHelper.IsAllowedOrigin("https://dispatch.resgrid.com").Should().BeTrue();
			CorsHelper.IsAllowedOrigin("https://resgrid.com").Should().BeTrue();
		}

		[Test]
		public void should_reject_unrelated_and_lookalike_domains()
		{
			CorsHelper.IsAllowedOrigin("https://evil.com").Should().BeFalse();
			CorsHelper.IsAllowedOrigin("https://evilresgrid.dev").Should().BeFalse();
			CorsHelper.IsAllowedOrigin("https://resgrid.dev.evil.com").Should().BeFalse();
			CorsHelper.IsAllowedOrigin("https://qaapi.resgrid.dev.evil.com").Should().BeFalse();
		}

		[Test]
		public void should_not_widen_the_parent_domain_past_a_public_registry_suffix()
		{
			SystemBehaviorConfig.ResgridBaseUrl = "https://web.resgrid.co.uk";
			SystemBehaviorConfig.ResgridApiBaseUrl = "https://api.resgrid.co.uk";
			SystemBehaviorConfig.ResgridEventingBaseUrl = "https://events.resgrid.co.uk";

			// Siblings under resgrid.co.uk are fine, but the parent must never widen to co.uk.
			CorsHelper.IsAllowedOrigin("https://dispatch.resgrid.co.uk").Should().BeTrue();
			CorsHelper.IsAllowedOrigin("https://someoneelse.co.uk").Should().BeFalse();
		}

		[Test]
		public void should_not_widen_single_label_or_ip_hosts()
		{
			SystemBehaviorConfig.ResgridBaseUrl = "https://localhost";
			SystemBehaviorConfig.ResgridApiBaseUrl = "https://192.168.1.20";
			SystemBehaviorConfig.ResgridEventingBaseUrl = "";

			CorsHelper.IsAllowedOrigin("https://localhost").Should().BeTrue();
			CorsHelper.IsAllowedOrigin("https://192.168.1.20").Should().BeTrue();
			CorsHelper.IsAllowedOrigin("https://192.168.1.21").Should().BeFalse();
			CorsHelper.IsAllowedOrigin("https://example.com").Should().BeFalse();
		}

		[Test]
		public void should_match_configured_origins_with_a_scheme_exactly()
		{
			ApiConfig.CorsAllowedOrigins = "http://localhost:8081, https://mydispatch.example.com";

			CorsHelper.IsAllowedOrigin("http://localhost:8081").Should().BeTrue();
			CorsHelper.IsAllowedOrigin("http://localhost:9999").Should().BeFalse();
			CorsHelper.IsAllowedOrigin("https://localhost:8081").Should().BeFalse();
			CorsHelper.IsAllowedOrigin("https://mydispatch.example.com").Should().BeTrue();
			CorsHelper.IsAllowedOrigin("http://mydispatch.example.com").Should().BeFalse();
		}

		[Test]
		public void should_match_bare_host_entries_on_any_scheme_and_port()
		{
			ApiConfig.CorsAllowedOrigins = "mydispatch.example.com";

			CorsHelper.IsAllowedOrigin("https://mydispatch.example.com").Should().BeTrue();
			CorsHelper.IsAllowedOrigin("http://mydispatch.example.com:3000").Should().BeTrue();
			CorsHelper.IsAllowedOrigin("https://sub.mydispatch.example.com").Should().BeFalse();
		}

		[Test]
		public void should_allow_everything_with_a_wildcard_entry()
		{
			ApiConfig.CorsAllowedOrigins = "*";

			CorsHelper.IsAllowedOrigin("https://anything.example.com").Should().BeTrue();
			CorsHelper.IsAllowedOrigin("http://localhost:1234").Should().BeTrue();
		}

		[Test]
		public void should_reject_missing_or_malformed_origins()
		{
			CorsHelper.IsAllowedOrigin(null).Should().BeFalse();
			CorsHelper.IsAllowedOrigin("").Should().BeFalse();
			CorsHelper.IsAllowedOrigin("   ").Should().BeFalse();
			CorsHelper.IsAllowedOrigin("not-a-url").Should().BeFalse();
			CorsHelper.IsAllowedOrigin("null").Should().BeFalse();
		}
	}
}
