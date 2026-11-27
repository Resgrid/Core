using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;
using Resgrid.Web.Controllers;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Models.v4.Lms;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using ApiLmsController = Resgrid.Web.Services.Controllers.v4.LmsController;
using File = System.IO.File;
using Match = System.Text.RegularExpressions.Match;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// Round 10 A8, the web side: the v4 Lms/GetLessonLaunch contract, the anonymous cookie-free launch page (/lms-app) for each kind with its
	/// strict policy and framing, the state endpoint, refusals, rate limits, the LTI return page, the content policy of an app launch, and guards
	/// on the controller (its dependencies, no sign-in, no cookie) and the page script (no "*" target, the token never in a URL).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class LmsAppLaunchWebTests
	{
		private const int D = LmsWorld.DepartmentId;
		private const string Site = "https://web.example.test";
		private string _previousPassphrase, _previousSite, _previousOrigins;
		private LmsAppLaunchTests _helpers;

		[SetUp]
		public void SetUp()
		{
			_previousPassphrase = SystemBehaviorConfig.ExternalLinkUrlParamPassphrase;
			_previousSite = SystemBehaviorConfig.ResgridBaseUrl;
			_previousOrigins = LmsStandardsConfig.AppFrameOrigins;
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = "round-10-a8-web-passphrase";
			SystemBehaviorConfig.ResgridBaseUrl = Site;
			LmsStandardsConfig.AppFrameOrigins = "";
			LmsAppController.Limiter.Clear();
			ApiLmsController.LessonLaunchLimiter.Clear();
			_helpers = new LmsAppLaunchTests();
			_helpers.SetUp();
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = "round-10-a8-web-passphrase";
			SystemBehaviorConfig.ResgridBaseUrl = Site;
		}

		[TearDown]
		public void TearDown()
		{
			_helpers.TearDown();
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = _previousPassphrase;
			SystemBehaviorConfig.ResgridBaseUrl = _previousSite;
			LmsStandardsConfig.AppFrameOrigins = _previousOrigins;
			LmsAppController.Limiter.Clear();
			ApiLmsController.LessonLaunchLimiter.Clear();
			ApiClaims._httpContextAccessor = null;
		}

		#region Helpers

		private static LmsAppController Web(ILmsAppLaunchService apps, out DefaultHttpContext http)
		{
			http = new DefaultHttpContext();
			http.Request.Scheme = "https";
			http.Request.Host = new HostString("web.example.test");
			http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
			var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.LmsStandards.LmsStandards>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key, true));
			return new LmsAppController(apps, localizer.Object) { ControllerContext = new ControllerContext { HttpContext = http } };
		}

		private static string Html(IActionResult result, int status = 200)
		{
			var content = result.Should().BeOfType<ContentResult>().Subject;
			content.StatusCode.Should().Be(status, content.Content);
			content.ContentType.Should().StartWith("text/html");
			return content.Content;
		}

		private static string Attribute(string html, string name)
		{
			var match = Regex.Match(html, "\\s" + Regex.Escape(name) + "=\"([^\"]*)\"");
			return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
		}

		/// <summary>The page loads only this site's script and stylesheet: no inline script, style or handler, and the token in no URL.</summary>
		private static void PageIsStrict(string html, string sessionToken)
		{
			Regex.Matches(html, "<script\\b[^>]*>").Cast<Match>().Should().OnlyContain(m => m.Value.Contains("src=\"" + LmsAppPages.ScriptPath));
			Regex.IsMatch(html, "<script[^>]*>[^<]+</script>").Should().BeFalse("no inline script");
			html.Should().NotContain("<style").And.NotContain(" style=");
			Regex.IsMatch(html, "\\son[a-z]+=", RegexOptions.IgnoreCase).Should().BeFalse("no inline handlers");
			if (sessionToken != null)
			{
				Attribute(html, "data-session").Should().Be(sessionToken);
				foreach (Match url in Regex.Matches(html, "\\s(src|href|action)=\"([^\"]*)\""))
					WebUtility.HtmlDecode(url.Groups[2].Value).Should().NotContain(sessionToken, "the page session never goes into a URL");
			}
		}

		private static void HeadersAreStrict(DefaultHttpContext http, bool framedByApps = false)
		{
			var csp = http.Response.Headers["Content-Security-Policy"].ToString();
			csp.Should().Contain("default-src 'none'").And.Contain("script-src 'self'").And.Contain("frame-src 'self'").And.Contain("base-uri 'none'").And.NotContain("unsafe");
			if (framedByApps)
				http.Response.Headers.ContainsKey("X-Frame-Options").Should().BeFalse();
			else
			{
				csp.Should().EndWith("frame-ancestors 'none'");
				http.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
			}
			http.Response.Headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
			http.Response.Headers["Cache-Control"].ToString().Should().Be("no-store");
			http.Response.Headers.ContainsKey("Set-Cookie").Should().BeFalse("the launch page sets no cookie");
		}

		private static string TokenOf(string launchUrl) => System.Web.HttpUtility.ParseQueryString(new Uri(launchUrl).Query)["token"];

		#endregion Helpers

		#region The launch page

		[Test]
		public async Task A_scorm_launch_page_runs_the_package_in_the_sandboxed_frame_under_a_strict_unframeable_policy()
		{
			var world = new LmsAppLaunchTests.AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();
			var issue = await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null);
			var controller = Web(world.Apps, out var http);

			var html = Html(await controller.Launch(issue.Token, CancellationToken.None));

			HeadersAreStrict(http);
			var iframe = Regex.Match(html, "<iframe[^>]*>").Value;
			Attribute(iframe, "sandbox").Should().Be("allow-scripts allow-forms allow-popups allow-modals allow-downloads");
			iframe.Should().NotContain("allow-same-origin").And.NotContain("allow-top-navigation");
			Attribute(iframe, "src").Should().StartWith("/lms-content/").And.EndWith("/index.html");
			Attribute(iframe, "referrerpolicy").Should().Be("no-referrer");
			Attribute(html, "data-mode").Should().Be("content");
			Attribute(html, "data-kind").Should().Be("Scorm12");
			Attribute(html, "data-enrollment").Should().Be(enrollment.LmsEnrollmentId.ToString());
			Attribute(html, "data-lesson").Should().Be(lesson.LmsLessonId.ToString());
			Attribute(html, "data-origins").Should().Be("[]");
			Attribute(html, "data-post-initial").Should().Be("false");
			JObject.Parse(Attribute(html, "data-state"))["status"].ToString().Should().Be("notAttempted");
			html.Should().Contain("href=\"" + LmsAppPages.StylesheetPath).And.NotContain("<nav", "a single-SCO package has no item menu");
			var session = world.W.Sessions.Rows.Single(s => s.Kind == (int)LmsRuntimeKinds.AppSession);
			PageIsStrict(html, Attribute(html, "data-session"));
			LmsLearnerIdentity.HashToken(Attribute(html, "data-session")).Should().Be(session.TokenHash);

			// The used link opens nothing again.
			var again = Web(world.Apps, out _);
			var refused = Html(await again.Launch(issue.Token, CancellationToken.None), 410);
			Attribute(refused, "data-mode").Should().Be("error");
			refused.Should().Contain("Std.Error.CodeExpired").And.NotContain("<iframe");
		}

		[Test]
		public async Task The_app_origins_alone_may_frame_the_page_and_the_content_of_an_app_launch()
		{
			LmsStandardsConfig.AppFrameOrigins = "https://responder.example.test app://bundle";
			var world = new LmsAppLaunchTests.AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();
			var issue = await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null);
			var controller = Web(world.Apps, out var http);

			var html = Html(await controller.Launch(issue.Token, CancellationToken.None));

			HeadersAreStrict(http, framedByApps: true);
			http.Response.Headers["Content-Security-Policy"].ToString().Should().EndWith("frame-ancestors https://responder.example.test app://bundle");
			JArray.Parse(Attribute(html, "data-origins")).Select(o => (string)o).Should().Equal("https://responder.example.test", "app://bundle");

			// The package's documents: the app origins join frame-ancestors only for the app's launch.
			var src = Attribute(Regex.Match(html, "<iframe[^>]*>").Value, "src");
			var contentToken = LmsStandardsWorld.TokenOf(src);
			var appPolicy = await PackagePolicyAsync(world, contentToken);
			appPolicy.Should().Contain("frame-ancestors 'self' https://web.example.test https://responder.example.test app://bundle");
			appPolicy.Should().StartWith("sandbox allow-scripts", "still sandboxed");
			appPolicy.Replace("frame-ancestors 'self' https://web.example.test https://responder.example.test app://bundle", "frame-ancestors 'self' https://web.example.test")
				.Should().Be(LmsContentController.ContentPolicyFor("https://web.example.test"), "nothing else changes");

			var web = await world.W.Runtime.LaunchAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null, Site);
			(await PackagePolicyAsync(world, LmsStandardsWorld.TokenOf(web.Url))).Should().NotContain("responder.example.test", "a web launch is never framed by the apps");
		}

		private static async Task<string> PackagePolicyAsync(LmsAppLaunchTests.AppWorld world, string contentToken)
		{
			var http = new DefaultHttpContext();
			http.Request.Scheme = "https";
			http.Request.Host = new HostString("web.example.test");
			var content = new LmsContentController(world.W.Runtime) { ControllerContext = new ControllerContext { HttpContext = http } };
			var result = await content.PackageFile(contentToken, "index.html", CancellationToken.None);
			result.Should().BeOfType<FileContentResult>();
			return http.Response.Headers["Content-Security-Policy"].ToString();
		}

		[Test]
		public async Task A_cmi5_launch_page_frames_the_au_with_its_fetch_url()
		{
			var world = new LmsAppLaunchTests.AppWorld();
			var (enrollment, lesson) = await world.Cmi5Async();
			var issue = await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null);
			var html = Html(await Web(world.Apps, out var http).Launch(issue.Token, CancellationToken.None));

			HeadersAreStrict(http);
			Attribute(html, "data-kind").Should().Be("Cmi5");
			var src = Attribute(Regex.Match(html, "<iframe[^>]*>").Value, "src");
			src.Should().StartWith("/lms-content/").And.Contain("fetch=" + Uri.EscapeDataString("https://web.example.test/lms-content/cmi5/fetch/"));
			PageIsStrict(html, Attribute(html, "data-session"));
		}

		[Test]
		public async Task A_multi_sco_page_relaunches_its_items_by_form_and_the_item_post_needs_the_page_session()
		{
			var world = new LmsAppLaunchTests.AppWorld();
			var (enrollment, lesson) = await world.ScormAsync(v2004: true);
			var issue = await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null);
			var html = Html(await Web(world.Apps, out _).Launch(issue.Token, CancellationToken.None));
			var session = Attribute(html, "data-session");

			Regex.Matches(html, "<form method=\"post\" action=\"/lms-app/item\">").Count.Should().Be(2);
			html.Should().Contain("<input type=\"hidden\" name=\"item\" value=\"i2\">");
			var second = Html(await Web(world.Apps, out var http).Item(session, "i2", CancellationToken.None));
			HeadersAreStrict(http);
			Attribute(Regex.Match(second, "<iframe[^>]*>").Value, "src").Should().Contain("/part2/index.html");
			Regex.Match(second, "<button[^>]*aria-current=\"page\"[^>]*>[^<]*").Value.Should().Contain("Part 2");

			Html(await Web(world.Apps, out _).Item("forged", "i2", CancellationToken.None), 410).Should().Contain("Std.Error.CodeExpired");
		}

		[Test]
		public async Task An_lti_launch_page_posts_the_page_session_to_lms_app_lti_which_sends_the_web_view_to_the_tool()
		{
			var world = new LmsAppLaunchTests.AppWorld();
			var (tool, enrollment, lesson) = await _helpers.LtiAsync(world, passback: true);
			var issue = await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null);
			issue.Kind.Should().Be(LmsAppLaunchKinds.Lti13);
			var html = Html(await Web(world.Apps, out var http).Launch(issue.Token, CancellationToken.None));

			HeadersAreStrict(http);
			http.Response.Headers["Content-Security-Policy"].ToString().Should().Contain("form-action 'self' https:");
			Attribute(html, "data-mode").Should().Be("lti");
			Attribute(html, "data-kind").Should().Be("Lti13");
			Attribute(html, "data-autostart").Should().Be("true");
			html.Should().Contain("<form id=\"rg-lms-app-lti\" method=\"post\" rel=\"noopener noreferrer\" action=\"/lms-app/lti\">").And.NotContain("<iframe");
			html.Should().Contain("Std.App.LtiScoreHelp", "grade passback: the result comes back from the tool");
			var session = Attribute(html, "data-session");
			PageIsStrict(html, session);

			var redirect = (await Web(world.Apps, out var ltiHttp).Lti(session, CancellationToken.None)).Should().BeOfType<RedirectResult>().Subject;
			redirect.Url.Should().StartWith("https://tool.example.test/login?").And.Contain("lti_message_hint=").And.NotContain(session);
			ltiHttp.Response.Headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
			Html(await Web(world.Apps, out _).Lti("forged", CancellationToken.None), 410);
		}

		[Test]
		public async Task A_lesson_that_may_not_run_any_more_renders_a_refusal_that_posts_unavailable()
		{
			var world = new LmsAppLaunchTests.AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();
			var issue = await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null);
			world.W.Lms.SeatedMembers.Remove(LmsWorld.Member);

			var html = Html(await Web(world.Apps, out var http).Launch(issue.Token, CancellationToken.None), 403);

			HeadersAreStrict(http);
			Attribute(html, "data-mode").Should().Be("error");
			Attribute(html, "data-post-initial").Should().Be("true");
			var state = JObject.Parse(Attribute(html, "data-state"));
			((string)state["status"]).Should().Be("unavailable");
			((string)state["error"]).Should().Be("NotLicensed");
			((int)state["lessonId"]).Should().Be(lesson.LmsLessonId);
			html.Should().Contain("Std.Error.NotLicensed").And.NotContain("data-session");
			PageIsStrict(html, null);
		}

		[Test]
		public async Task The_state_endpoint_answers_the_page_sessions_message_same_origin_only()
		{
			var world = new LmsAppLaunchTests.AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();
			var issue = await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null);
			var html = Html(await Web(world.Apps, out _).Launch(issue.Token, CancellationToken.None));
			var session = Attribute(html, "data-session");
			var contentToken = LmsStandardsWorld.TokenOf(Attribute(Regex.Match(html, "<iframe[^>]*>").Value, "src"));
			await world.W.Runtime.CommitScormAsync(contentToken, new LmsScormCommit { Values = { ["cmi.core.lesson_status"] = "passed", ["cmi.core.score.raw"] = "88" }, Finish = true });

			var ok = (await Web(world.Apps, out var http).State(session)).Should().BeOfType<ContentResult>().Subject;
			ok.StatusCode.Should().Be(200);
			ok.ContentType.Should().Be("application/json");
			var message = JObject.Parse(ok.Content);
			((string)message["type"]).Should().Be("resgrid.lms.lesson");
			((string)message["status"]).Should().Be("passed");
			((bool)message["satisfied"]).Should().BeTrue();
			((decimal)message["score"]).Should().Be(88);
			http.Response.Headers.ContainsKey("Access-Control-Allow-Origin").Should().BeFalse("no other origin may read it");

			var unknown = (await Web(world.Apps, out _).State("forged")).Should().BeOfType<ContentResult>().Subject;
			unknown.StatusCode.Should().Be(401);
			((string)JObject.Parse(unknown.Content)["status"]).Should().Be("unavailable");
			((string)JObject.Parse(unknown.Content)["error"]).Should().Be("CodeExpired");
			(await Web(world.Apps, out _).State(contentToken)).Should().BeOfType<ContentResult>().Which.StatusCode.Should().Be(401, "a content token is not a page session");
		}

		[Test]
		public async Task The_launch_page_is_rate_limited_per_address()
		{
			var apps = new Mock<ILmsAppLaunchService>();
			apps.Setup(a => a.ExchangeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new LmsAppPage { Error = LmsErrors.CodeExpired });
			var now = DateTime.UtcNow;
			for (var i = 0; i < LmsAppController.LaunchesPerMinute; i++)
			{
				var controller = Web(apps.Object, out _);
				controller.Clock = () => now;
				Html(await controller.Launch("t" + i, CancellationToken.None), 410);
			}
			var limited = Web(apps.Object, out var http);
			limited.Clock = () => now;
			Html(await limited.Launch("t", CancellationToken.None), 429).Should().Contain("Std.App.Busy");
			HeadersAreStrict(http);
			apps.Verify(a => a.ExchangeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
				Times.Exactly(LmsAppController.LaunchesPerMinute), "a limited request never reaches the exchange");

			var later = Web(apps.Object, out _);
			later.Clock = () => now.AddMinutes(2);
			Html(await later.Launch("t", CancellationToken.None), 410);
		}

		[Test]
		public async Task The_lti_return_page_shows_its_lessons_state_only_for_a_valid_signed_link()
		{
			var world = new LmsAppLaunchTests.AppWorld();
			var (tool, enrollment, lesson) = await _helpers.LtiAsync(world);
			var issue = await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null);
			var page = await world.Apps.ExchangeAsync(issue.Token, Site, null, null);
			var appSession = world.AppSessionOf(page);
			var url = LmsAppLaunchRules.ReturnUrl(D, appSession.LmsRuntimeSessionId, DateTime.UtcNow.AddHours(1));
			var q = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);

			var html = Html(await Web(world.Apps, out var http).Return(q["d"], q["a"], q["e"], q["sig"]));
			HeadersAreStrict(http);
			Attribute(html, "data-mode").Should().Be("done");
			Attribute(html, "data-post-initial").Should().Be("true");
			((int)JObject.Parse(Attribute(html, "data-state"))["lessonId"]).Should().Be(lesson.LmsLessonId);
			html.Should().Contain("id=\"rg-lms-app-close\"").And.Contain("Std.App.Returned").And.NotContain("data-session");
			PageIsStrict(html, null);

			Html(await Web(world.Apps, out _).Return(q["d"], q["a"], q["e"], q["sig"] + "x"), 410);
			Html(await Web(world.Apps, out _).Return(q["d"], (appSession.LmsRuntimeSessionId + 1).ToString(), q["e"], q["sig"]), 410);
		}

		#endregion The launch page

		#region Guards

		[Test]
		public void The_launch_controller_is_anonymous_cookie_free_and_depends_on_the_app_launch_service_and_strings_only()
		{
			var type = typeof(LmsAppController);
			type.GetCustomAttributes<AllowAnonymousAttribute>().Should().NotBeEmpty();
			type.GetCustomAttributes<AuthorizeAttribute>().Should().BeEmpty();
			type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>()).Should().BeEmpty();
			type.GetConstructors().Single().GetParameters().Select(p => p.ParameterType)
				.Should().Equal(typeof(ILmsAppLaunchService), typeof(IStringLocalizer<Resgrid.Localization.Areas.User.LmsStandards.LmsStandards>));
			foreach (var post in new[] { nameof(LmsAppController.Item), nameof(LmsAppController.Lti), nameof(LmsAppController.State) })
				type.GetMethod(post).GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>().Should().NotBeEmpty(post + " is authenticated by the token in its body, never a cookie");

			var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "Controllers", "LmsAppController.cs"));
			source.Should().NotContain("SignInAsync").And.NotContain("Cookies.Append").And.NotContain("HttpContext.User").And.NotContain("Bootstrapper");
		}

		[Test]
		public void The_page_script_posts_only_to_the_native_bridge_and_the_configured_origins_and_keeps_the_token_out_of_urls()
		{
			var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "wwwroot", "js", "app", "internal", "lms", "resgrid.lms.app.js"));
			script.Should().Contain("window.ReactNativeWebView.postMessage(JSON.stringify(state))");
			script.Should().Contain("window.parent.postMessage(state, origins[i])");
			Regex.IsMatch(script, "postMessage\\([^)]*['\"]\\*['\"]").Should().BeFalse("never a wildcard target origin");
			script.Should().Contain("'resgrid.lms.lesson'");
			script.Should().Contain("xhr.send('session=' + encodeURIComponent(session))").And.NotContain("?session=").And.NotContain("&session=");
			script.Should().NotContain("const ").And.NotContain("let ").And.NotContain("=>", "the minifier's ES5 subset (NUglify)");
			foreach (var status in LmsAppLaunchRules.ResultStatuses)
				script.Should().Contain("'" + status + "'");
		}

		[Test]
		public void The_page_policy_allows_nothing_inline_and_frames_only_the_given_origins()
		{
			var policy = LmsAppPages.PolicyFor(new string[0]);
			policy.Should().Be("default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-src 'self'; " +
				"form-action 'self' https:; base-uri 'none'; object-src 'none'; frame-ancestors 'none'");
			LmsAppPages.PolicyFor(new[] { "https://a.example.test" }).Should().EndWith("frame-ancestors https://a.example.test");
			LmsContentController.ContentPolicyFor("https://web.example.test", new[] { "app://bundle" })
				.Should().EndWith("frame-ancestors 'self' https://web.example.test app://bundle");
			LmsContentController.ContentPolicyFor("https://web.example.test", new string[0]).Should().Be(LmsContentController.ContentPolicyFor("https://web.example.test"));
		}

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			return directory?.FullName ?? throw new InvalidOperationException("Resgrid.sln not found");
		}

		#endregion Guards

		#region The v4 API

		private static ApiLmsController Api(LmsAppLaunchTests.AppWorld world, string userId, bool moduleOn = true, bool flagOn = true)
		{
			var http = new DefaultHttpContext();
			http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, userId), new Claim(ClaimTypes.PrimaryGroupSid, D.ToString()) }, "test"));
			ApiClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			var gate = new Mock<ILmsGateService>();
			gate.Setup(g => g.GetGateAsync(It.IsAny<int>())).ReturnsAsync(() => world.W.Lms.Gate);
			var access = new Mock<ILmsAccessService>();
			access.Setup(a => a.IsTrainingModuleOnAsync(It.IsAny<int>())).ReturnsAsync(moduleOn);
			access.Setup(a => a.IsFlagEnabledAsync(It.IsAny<int>())).ReturnsAsync(flagOn);
			return new ApiLmsController(gate.Object, world.W.Lms.Learner, world.W.Lms.Assessment, world.W.Lms.MediaService, world.W.Lms.Crew, world.W.Lms.Academy, access.Object,
				Mock.Of<IAiKnowledgeService>(), world.Apps)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		[Test]
		public void GetLessonLaunch_needs_training_view()
		{
			var method = typeof(ApiLmsController).GetMethod(nameof(ApiLmsController.GetLessonLaunch));
			method.GetCustomAttributes<AuthorizeAttribute>().Should().ContainSingle(a => a.Policy == ResgridResources.Training_View);
			method.GetCustomAttributes<HttpGetAttribute>().Single().Template.Should().Be("GetLessonLaunch");
		}

		[Test]
		public async Task GetLessonLaunch_returns_the_kind_a_single_use_launch_url_its_expiry_and_the_completion_message()
		{
			var world = new LmsAppLaunchTests.AppWorld();
			var (enrollment, lesson) = await world.ScormAsync(v2004: true);
			var before = DateTime.UtcNow;

			var result = (LmsLessonLaunchResult)((OkObjectResult)(await Api(world, LmsWorld.Member).GetLessonLaunch(enrollment.LmsEnrollmentId, lesson.LmsLessonId)).Result).Value;

			result.Error.Should().BeNull();
			result.Data.Kind.Should().Be("Scorm2004");
			result.Data.LaunchUrl.Should().StartWith(Site + "/lms-app/launch?token=");
			result.Data.ExpiresOn.Should().BeOnOrBefore(before.AddMinutes(5).AddSeconds(5)).And.BeAfter(before);
			var message = result.Data.CompletionMessage;
			message.Type.Should().Be("resgrid.lms.lesson");
			message.Version.Should().Be(1);
			message.Origin.Should().Be(Site);
			message.EnrollmentId.Should().Be(enrollment.LmsEnrollmentId);
			message.LessonId.Should().Be(lesson.LmsLessonId);
			message.Statuses.Should().Equal(LmsAppLaunchRules.Statuses);
			message.ResultStatuses.Should().Equal("completed", "passed", "failed", "terminated", "unavailable");

			var page = await world.Apps.ExchangeAsync(TokenOf(result.Data.LaunchUrl), Site, null, null);
			page.Succeeded.Should().BeTrue(page.Error.ToString());
			page.Kind.Should().Be(LmsAppLaunchKinds.Scorm2004);
			(await world.Apps.ExchangeAsync(TokenOf(result.Data.LaunchUrl), Site, null, null)).Error.Should().Be(LmsErrors.CodeExpired);
		}

		[Test]
		public async Task GetLessonLaunch_refuses_with_the_players_reason_and_404s_what_is_not_the_members()
		{
			var world = new LmsAppLaunchTests.AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();

			(await Api(world, LmsWorld.Member, moduleOn: false).GetLessonLaunch(enrollment.LmsEnrollmentId, lesson.LmsLessonId)).Result.Should().BeOfType<NotFoundResult>();
			(await Api(world, LmsWorld.Member, flagOn: false).GetLessonLaunch(enrollment.LmsEnrollmentId, lesson.LmsLessonId)).Result.Should().BeOfType<NotFoundResult>();
			(await Api(world, LmsWorld.Member2).GetLessonLaunch(enrollment.LmsEnrollmentId, lesson.LmsLessonId)).Result.Should().BeOfType<NotFoundResult>("another member's enrollment");
			(await Api(world, LmsWorld.Member).GetLessonLaunch(enrollment.LmsEnrollmentId, lesson.LmsLessonId + 1000)).Result.Should().BeOfType<NotFoundResult>();

			world.W.Lms.SeatedMembers.Remove(LmsWorld.Member);
			var refused = (LmsLessonLaunchResult)((OkObjectResult)(await Api(world, LmsWorld.Member).GetLessonLaunch(enrollment.LmsEnrollmentId, lesson.LmsLessonId)).Result).Value;
			refused.Error.Should().Be("NotLicensed");
			refused.Data.Should().BeNull();
			world.W.Sessions.Rows.Should().NotContain(s => s.Kind == (int)LmsRuntimeKinds.AppLaunch, "no link without the right to run the lesson");
		}

		[Test]
		public async Task GetLessonLaunch_is_rate_limited_per_member()
		{
			var world = new LmsAppLaunchTests.AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();
			for (var i = 0; i < ApiLmsController.LessonLaunchesPerMinute; i++)
				(await Api(world, LmsWorld.Member).GetLessonLaunch(enrollment.LmsEnrollmentId, lesson.LmsLessonId)).Result.Should().BeOfType<OkObjectResult>();
			var limited = (await Api(world, LmsWorld.Member).GetLessonLaunch(enrollment.LmsEnrollmentId, lesson.LmsLessonId)).Result.Should().BeOfType<ObjectResult>().Subject;
			limited.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
			((LmsLessonLaunchResult)limited.Value).Error.Should().Be("RateLimited");
		}

		#endregion The v4 API
	}
}
