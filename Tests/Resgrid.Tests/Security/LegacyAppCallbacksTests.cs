using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;

namespace Resgrid.Tests.Security
{
	[TestFixture]
	public class LegacyAppCallbacksTests
	{
		[TestCase(UserSessionClientApplication.Responder, "resgrid://auth/callback")]
		[TestCase(UserSessionClientApplication.Unit, "resgridunit://auth/callback")]
		[TestCase(UserSessionClientApplication.Dispatch, "resgriddispatch://auth/callback")]
		[TestCase(UserSessionClientApplication.Command, "resgridic://auth/callback")]
		public void Each_app_has_its_own_callback(UserSessionClientApplication client, string expected)
		{
			LegacyAppCallbacks.For(client).Callback.Should().Be(expected);
		}

		[TestCase(UserSessionClientApplication.Api)]
		[TestCase(UserSessionClientApplication.UnknownLegacy)]
		[TestCase(UserSessionClientApplication.Web)]
		[TestCase(UserSessionClientApplication.BigBoard)]
		[TestCase(UserSessionClientApplication.Mcp)]
		public void A_caller_that_is_not_one_of_the_apps_gets_responders(UserSessionClientApplication client)
		{
			LegacyAppCallbacks.For(client).Should().BeSameAs(LegacyAppCallbacks.Default);
			LegacyAppCallbacks.Default.Callback.Should().Be("resgrid://auth/callback");
		}

		[Test]
		public void Names_are_the_ones_a_tagged_relay_state_uses()
		{
			LegacyAppCallbacks.All.Select(a => a.Name).Should().Equal("responder", "unit", "dispatch", "ic");
			LegacyAppCallbacks.ForName("dispatch").Client.Should().Be(UserSessionClientApplication.Dispatch);
			LegacyAppCallbacks.ForName("Dispatch").Should().BeNull();
			LegacyAppCallbacks.ForName("web").Should().BeNull();
			LegacyAppCallbacks.ForName(null).Should().BeNull();
		}

		[Test]
		public void The_web_sso_page_lists_every_apps_redirect_uris_from_the_web_origins_setting()
		{
			var saved = Resgrid.Config.SsoConfig.AppWebOrigins;
			try
			{
				Resgrid.Config.SsoConfig.AppWebOrigins = "unit=https://unit.example.org";

				var listed = new Resgrid.Web.Areas.User.Models.Security.SsoConfigEditView().OidcAppRedirectUris;

				listed.Should().Equal(LegacyAppCallbacks.RedirectUris("unit=https://unit.example.org"));
				listed.Should().ContainSingle(u => u.Web).Which.Uri.Should().Be("https://unit.example.org/auth/callback");
			}
			finally
			{
				Resgrid.Config.SsoConfig.AppWebOrigins = saved;
			}
		}

		[TestCase(UserSessionClientApplication.Responder, "/auth/callback")]
		[TestCase(UserSessionClientApplication.Unit, "/auth/callback")]
		[TestCase(UserSessionClientApplication.Dispatch, "/login/sso")]
		[TestCase(UserSessionClientApplication.Command, "/auth/callback")]
		public void Each_apps_web_edition_returns_to_the_page_its_sign_in_names(UserSessionClientApplication client, string expected)
		{
			// Expo's makeRedirectUri on web is the page's origin plus the path the app's hook passes it.
			LegacyAppCallbacks.For(client).WebPath.Should().Be(expected);
		}

		[Test]
		public void Redirect_uris_list_each_apps_native_uri_then_its_web_page()
		{
			var uris = LegacyAppCallbacks.RedirectUris(
				"responder=https://responder.resgrid.com;unit=https://unit.resgrid.com;dispatch=https://dispatch.resgrid.com");

			uris.Select(u => $"{u.Name}|{u.DisplayName}|{u.Web}|{u.Uri}").Should().Equal(
				"responder|Resgrid Responder|False|resgrid://auth/callback",
				"responder|Resgrid Responder|True|https://responder.resgrid.com/auth/callback",
				"unit|Resgrid Unit|False|resgridunit://auth/callback",
				"unit|Resgrid Unit|True|https://unit.resgrid.com/auth/callback",
				"dispatch|Resgrid Dispatch|False|resgriddispatch://auth/callback",
				"dispatch|Resgrid Dispatch|True|https://dispatch.resgrid.com/login/sso",
				"ic|Resgrid IC|False|resgridic://auth/callback");
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("  ")]
		[TestCase(";;")]
		public void With_no_web_origins_only_the_native_uris_are_listed(string webOrigins)
		{
			LegacyAppCallbacks.RedirectUris(webOrigins).Select(u => u.Uri).Should().Equal(LegacyAppCallbacks.All.Select(a => a.Callback));
			LegacyAppCallbacks.RedirectUris(webOrigins).Should().OnlyContain(u => !u.Web);
		}

		[Test]
		public void The_default_setting_lists_the_development_web_hosts()
		{
			// Like the other URL settings, the default names the development hosts; IC has no web edition.
			var defaults = LegacyAppCallbacks.RedirectUris(Resgrid.Config.SsoConfig.AppWebOrigins);

			defaults.Where(u => u.Web).Select(u => u.Uri).Should().Equal(
				"https://responder.resgrid.local/auth/callback", "https://unit.resgrid.local/auth/callback",
				"https://dispatch.resgrid.local/login/sso");
		}

		[TestCase("https://unit.example.org", "https://unit.example.org/auth/callback")]
		[TestCase("https://unit.example.org/", "https://unit.example.org/auth/callback", TestName = "A trailing slash")]
		[TestCase("https://unit.example.org:8443", "https://unit.example.org:8443/auth/callback")]
		[TestCase("https://unit.example.org:443", "https://unit.example.org/auth/callback", TestName = "The default port, dropped as a browser does")]
		[TestCase("HTTPS://Unit.Example.ORG", "https://unit.example.org/auth/callback", TestName = "Upper case, lowered as a browser does")]
		[TestCase("http://localhost:8081", "http://localhost:8081/auth/callback")]
		[TestCase("http://127.0.0.1:19006", "http://127.0.0.1:19006/auth/callback")]
		[TestCase("http://[::1]:8081", "http://[::1]:8081/auth/callback")]
		[TestCase("http://unit.resgrid.local", "http://unit.resgrid.local/auth/callback")]
		[TestCase("  https://unit.example.org  ", "https://unit.example.org/auth/callback", TestName = "Surrounding spaces")]
		[TestCase("https://unit.example.org\n", "https://unit.example.org/auth/callback", TestName = "A trailing newline, trimmed like spaces")]
		public void A_clean_origin_is_listed_as_the_apps_web_page(string origin, string expected)
		{
			LegacyAppCallbacks.RedirectUris($"unit={origin}").Should().ContainSingle(u => u.Web).Which.Uri.Should().Be(expected);
		}

		[TestCase("http://unit.example.org", TestName = "Plain http on a real host")]
		[TestCase("http://localhost.example.org", TestName = "Plain http on a host that only starts with localhost")]
		[TestCase("http://unit.local.example.org", TestName = "Plain http on a host that only contains .local")]
		[TestCase("https://unit.example.org/app", TestName = "A path")]
		[TestCase("https://unit.example.org/.", TestName = "A dot path")]
		[TestCase("https://unit.example.org//", TestName = "Two slashes")]
		[TestCase("https://unit.example.org?x=1", TestName = "A query")]
		[TestCase("https://unit.example.org#x", TestName = "A fragment")]
		[TestCase("https://user@unit.example.org", TestName = "User info")]
		[TestCase("https://unit.example.org:99999", TestName = "A port out of range")]
		[TestCase("https://unit.example.org.", TestName = "A trailing dot")]
		[TestCase("https://unit..example.org", TestName = "An empty label")]
		[TestCase("unit.example.org", TestName = "No scheme")]
		[TestCase("ftp://unit.example.org", TestName = "Another scheme")]
		[TestCase("resgridunit://auth", TestName = "An app scheme")]
		[TestCase("javascript:alert(1)", TestName = "Script")]
		[TestCase("https://unit.example.org\nhttps://evil.example", TestName = "A second line")]
		[TestCase("", TestName = "Empty")]
		public void An_origin_that_is_not_clean_is_not_listed(string origin)
		{
			LegacyAppCallbacks.RedirectUris($"unit={origin};dispatch=https://dispatch.example.org").Select(u => u.Uri).Should().Equal(
				"resgrid://auth/callback", "resgridunit://auth/callback", "resgriddispatch://auth/callback",
				"https://dispatch.example.org/login/sso", "resgridic://auth/callback");
		}

		[TestCase("web=https://app.example.org", TestName = "Not an app")]
		[TestCase("=https://unit.example.org", TestName = "No app")]
		[TestCase("https://unit.example.org", TestName = "No equals sign")]
		[TestCase("command=https://ic.example.org", TestName = "The enum name, not the app's")]
		public void An_entry_that_names_no_app_is_not_listed(string webOrigins)
		{
			LegacyAppCallbacks.RedirectUris(webOrigins).Should().OnlyContain(u => !u.Web);
		}

		[Test]
		public void An_apps_first_clean_entry_wins_and_app_names_ignore_case_and_spaces()
		{
			var uris = LegacyAppCallbacks.RedirectUris(
				" IC = https://ic.example.org ; unit=http://bad.example.org; unit=https://unit.example.org;unit=https://unit2.example.org");

			uris.Where(u => u.Web).Select(u => $"{u.Name}={u.Uri}").Should().Equal(
				"unit=https://unit.example.org/auth/callback", "ic=https://ic.example.org/auth/callback");
		}

		[Test]
		public void Every_scheme_is_lowercase_and_its_apps_own()
		{
			var schemes = LegacyAppCallbacks.All.Select(a => new Uri(a.Callback)).ToList();

			// Android matches intent-filter schemes case-sensitively and IdPs compare redirect URIs as exact strings.
			foreach (var app in LegacyAppCallbacks.All)
			{
				var scheme = app.Callback.Substring(0, app.Callback.IndexOf(':'));
				scheme.Should().Be(scheme.ToLowerInvariant(), $"{app.Name}'s scheme must be lowercase");
			}
			schemes.Select(u => u.Scheme).Should().OnlyHaveUniqueItems();
			schemes.Should().OnlyContain(u => u.Host == "auth" && u.AbsolutePath == "/callback" && u.Query.Length == 0);
		}
	}
}
