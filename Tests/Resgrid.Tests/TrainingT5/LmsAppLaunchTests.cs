using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// Round 10 A8: SCORM, cmi5 and LTI lessons in the app's web view. The launch link (single use, minutes long, only its hash stored, bound to
	/// the member, enrollment and lesson), its exchange for a page session that reaches that lesson only, each kind's runtime, the completion
	/// message, the seat and lapse refusals (at issue, at exchange and on every runtime call of an app session), tokens never accepted across
	/// their kinds, and the LTI return link. Over the real learner, runtime and LTI services (LmsStandardsWorld).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class LmsAppLaunchTests
	{
		private const int D = LmsWorld.DepartmentId;
		private string _previousPassphrase, _previousSite, _previousOrigins;
		private int _previousMinutes;
		private RSA _toolKey;

		[SetUp]
		public void SetUp()
		{
			_previousPassphrase = SystemBehaviorConfig.ExternalLinkUrlParamPassphrase;
			_previousSite = SystemBehaviorConfig.ResgridBaseUrl;
			_previousOrigins = LmsStandardsConfig.AppFrameOrigins;
			_previousMinutes = LmsStandardsConfig.AppLaunchMinutes;
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = "round-10-a8-test-passphrase";
			SystemBehaviorConfig.ResgridBaseUrl = "https://web.example.test";
			LmsStandardsConfig.AppFrameOrigins = "";
			LmsStandardsConfig.AppLaunchMinutes = 5;
			LmsAppLaunchService.ClearCache();
			_toolKey = RSA.Create(2048);
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = _previousPassphrase;
			SystemBehaviorConfig.ResgridBaseUrl = _previousSite;
			LmsStandardsConfig.AppFrameOrigins = _previousOrigins;
			LmsStandardsConfig.AppLaunchMinutes = _previousMinutes;
			LmsAppLaunchService.ClearCache();
			LmsStandardsRuntimeService.ClearCache();
			_toolKey?.Dispose();
		}

		#region World

		/// <summary>The app launch service over a standards world, with the audit events it sends.</summary>
		internal sealed class AppWorld
		{
			public readonly LmsStandardsWorld W = new LmsStandardsWorld();
			public readonly LmsAppLaunchService Apps;
			public readonly List<AuditEvent> Audits = new List<AuditEvent>();

			public AppWorld()
			{
				LmsAppLaunchService.ClearCache();
				W.Events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback<AuditEvent>(a => Audits.Add(a));
				Apps = new LmsAppLaunchService(W.Lms.Learner, W.Runtime, W.Lti, W.Sessions, W.Registrations, W.Packages, W.Tools, W.Lms.Store.Object, W.Events.Object);
			}

			public async Task<(LmsEnrollment Enrollment, LmsLesson Lesson)> ScormAsync(bool v2004 = false, bool readingFirst = false)
			{
				var package = await W.ReadyPackageAsync(v2004 ? LmsStandardsWorld.Scorm2004Zip() : LmsStandardsWorld.Scorm12Zip());
				var (enrollment, lesson, _) = await W.CourseWithAsync(LmsLessonKinds.Scorm, package.LmsContentPackageId, readingFirst: readingFirst);
				return (enrollment, lesson);
			}

			public async Task<(LmsEnrollment Enrollment, LmsLesson Lesson)> Cmi5Async(string moveOn = null)
			{
				var zip = moveOn == null
					? LmsStandardsWorld.Cmi5Zip()
					: LmsStandardsWorld.Zip(("cmi5.xml", LmsStandardsWorld.Cmi5Manifest.Replace("moveOn=\"CompletedAndPassed\"", "moveOn=\"" + moveOn + "\"")), ("au1/index.html", "<html><head></head><body>au</body></html>"));
				var package = await W.ReadyPackageAsync(zip);
				var (enrollment, lesson, _) = await W.CourseWithAsync(LmsLessonKinds.Cmi5, package.LmsContentPackageId);
				return (enrollment, lesson);
			}

			public async Task<LmsAppPage> OpenAsync(LmsEnrollment enrollment, LmsLesson lesson, string user = LmsWorld.Member, string item = null)
			{
				var issue = await Apps.IssueAsync(D, user, enrollment.LmsEnrollmentId, lesson.LmsLessonId, item);
				issue.Succeeded.Should().BeTrue(issue.Error.ToString());
				var page = await Apps.ExchangeAsync(issue.Token, "https://web.example.test", "203.0.113.9", "test-agent");
				page.Succeeded.Should().BeTrue(page.Error.ToString());
				return page;
			}

			public LmsRuntimeSession AppSessionOf(LmsAppPage page) =>
				W.Sessions.Rows.Single(s => s.Kind == (int)LmsRuntimeKinds.AppSession && s.TokenHash == LmsLearnerIdentity.HashToken(page.SessionToken));
		}

		private string Jwks()
		{
			var parameters = _toolKey.ExportParameters(false);
			return new JObject
			{
				["keys"] = new JArray(new JObject
				{
					["kty"] = "RSA", ["kid"] = "tool-key-1", ["use"] = "sig", ["alg"] = "RS256",
					["n"] = Base64UrlEncoder.Encode(parameters.Modulus), ["e"] = Base64UrlEncoder.Encode(parameters.Exponent)
				})
			}.ToString();
		}

		internal async Task<(LmsLtiTool Tool, LmsEnrollment Enrollment, LmsLesson Lesson)> LtiAsync(AppWorld world, bool passback = false, bool active = true)
		{
			var saved = await world.W.Lti.SaveToolAsync(world.W.Lms.AdminActor, new LmsLtiToolInput
			{
				Name = "CE Provider", OidcLoginUrl = "https://tool.example.test/login", LaunchUrl = "https://tool.example.test/launch",
				RedirectUris = "https://tool.example.test/launch", PublicJwksJson = Jwks(), AllowGradePassback = passback, IsActive = active
			});
			saved.Succeeded.Should().BeTrue(saved.Error.ToString());
			var tool = (LmsLtiTool)saved.Value;
			var (enrollment, lesson, _) = await world.W.CourseWithAsync(LmsLessonKinds.LtiLink, toolId: tool.LmsLtiToolId);
			return (tool, enrollment, lesson);
		}

		private static Dictionary<string, string> Query(string url)
		{
			var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
			return query.AllKeys.ToDictionary(k => k, k => query[k]);
		}

		#endregion World

		#region The launch link

		[Test]
		public async Task A_launch_link_is_an_opaque_single_use_token_of_minutes_whose_hash_alone_is_stored()
		{
			var world = new AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();
			var before = DateTime.UtcNow;

			var issue = await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null);

			issue.Succeeded.Should().BeTrue(issue.Error.ToString());
			issue.Kind.Should().Be(LmsAppLaunchKinds.Scorm12);
			issue.Token.Should().MatchRegex("^[A-Za-z0-9_-]{43}$", "256 random bits, base64url");
			issue.ExpiresOn.Should().BeOnOrBefore(before.AddMinutes(5).AddSeconds(5)).And.BeAfter(before);
			var row = world.W.Sessions.Rows.Single();
			row.Kind.Should().Be((int)LmsRuntimeKinds.AppLaunch);
			row.TokenHash.Should().Be(LmsLearnerIdentity.HashToken(issue.Token)).And.NotBe(issue.Token, "only the hash is stored");
			row.UserId.Should().Be(LmsWorld.Member);
			row.LmsEnrollmentId.Should().Be(enrollment.LmsEnrollmentId);
			row.LmsLessonId.Should().Be(lesson.LmsLessonId);

			var page = await world.Apps.ExchangeAsync(issue.Token, "https://web.example.test", "203.0.113.9", "agent");
			page.Succeeded.Should().BeTrue(page.Error.ToString());
			world.W.Sessions.Rows.Single(s => s.Kind == (int)LmsRuntimeKinds.AppLaunch).UsedOn.Should().NotBeNull("the link is consumed");
			(await world.Apps.ExchangeAsync(issue.Token, "https://web.example.test", "203.0.113.9", "agent")).Error.Should().Be(LmsErrors.CodeExpired, "single use");

			var audit = world.Audits.Where(a => a.Type == AuditLogTypes.LmsAppLaunchOpened).Should().ContainSingle().Subject;
			audit.Type.Should().Be(AuditLogTypes.LmsAppLaunchOpened);
			audit.UserId.Should().Be(LmsWorld.Member);
			audit.DepartmentId.Should().Be(D);
			audit.IpAddress.Should().Be("203.0.113.9");
			audit.After.Should().Contain("\"LessonId\":" + lesson.LmsLessonId).And.NotContain(issue.Token).And.NotContain(page.SessionToken, "no token is audited");
		}

		[Test]
		public async Task An_expired_tampered_or_unknown_link_is_refused_and_the_lifetime_never_exceeds_five_minutes()
		{
			var world = new AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();
			LmsStandardsConfig.AppLaunchMinutes = 60;
			var issue = await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null);
			issue.ExpiresOn.Should().BeOnOrBefore(DateTime.UtcNow.AddMinutes(5).AddSeconds(5), "clamped to five minutes");

			var tampered = (issue.Token[0] == 'A' ? "B" : "A") + issue.Token.Substring(1);
			(await world.Apps.ExchangeAsync(tampered, "https://s", null, null)).Error.Should().Be(LmsErrors.CodeExpired);
			(await world.Apps.ExchangeAsync(issue.Token + "x", "https://s", null, null)).Error.Should().Be(LmsErrors.CodeExpired);
			(await world.Apps.ExchangeAsync(null, "https://s", null, null)).Error.Should().Be(LmsErrors.CodeExpired);
			(await world.Apps.ExchangeAsync(new string('a', 500), "https://s", null, null)).Error.Should().Be(LmsErrors.CodeExpired);

			world.Apps.Clock = () => DateTime.UtcNow.AddMinutes(6);
			(await world.Apps.ExchangeAsync(issue.Token, "https://s", null, null)).Error.Should().Be(LmsErrors.CodeExpired, "expired");
			world.W.Sessions.Rows.Single(s => s.Kind == (int)LmsRuntimeKinds.AppLaunch).UsedOn.Should().BeNull("an expired link is not even consumed");
			world.Audits.Should().NotContain(a => a.Type == AuditLogTypes.LmsAppLaunchOpened);
		}

		[Test]
		public async Task A_link_is_issued_only_for_the_members_own_enrollment_and_an_open_standards_lesson_that_can_play()
		{
			var world = new AppWorld();
			var (enrollment, lesson) = await world.ScormAsync(readingFirst: true);

			(await world.Apps.IssueAsync(D, LmsWorld.Member2, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null)).Error.Should().Be(LmsErrors.NotFound, "another member's enrollment");
			(await world.Apps.IssueAsync(D + 1, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null)).Error.Should().Be(LmsErrors.NotFound, "another department");
			(await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null)).Error.Should().Be(LmsErrors.LessonLocked, "the reading first");
			var reading = world.W.Lms.Lessons.Rows.Single(l => l.Kind == (int)LmsLessonKinds.Blocks && l.LmsCourseVersionId == lesson.LmsCourseVersionId);
			(await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, reading.LmsLessonId, null)).Error.Should().Be(LmsErrors.NotFound, "not a standards lesson");

			var (otherEnrollment, otherLesson) = await world.ScormAsync();
			(await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, otherLesson.LmsLessonId, null)).Error.Should().Be(LmsErrors.NotFound, "a lesson of another course");
			(await world.Apps.IssueAsync(D, LmsWorld.Member, otherEnrollment.LmsEnrollmentId, otherLesson.LmsLessonId, null)).Succeeded.Should().BeTrue();

			world.W.Packages.Rows.Single(p => p.LmsContentPackageId == otherLesson.LmsContentPackageId).Status = (int)LmsContentPackageStatuses.Processing;
			(await world.Apps.IssueAsync(D, LmsWorld.Member, otherEnrollment.LmsEnrollmentId, otherLesson.LmsLessonId, null)).Error.Should().Be(LmsErrors.MediaUnavailable, "not ready");
			world.W.Packages.Rows.Single(p => p.LmsContentPackageId == otherLesson.LmsContentPackageId).Status = (int)LmsContentPackageStatuses.Ready;
			world.W.Lms.Store.SetupGet(s => s.IsConfigured).Returns(false);
			(await world.Apps.IssueAsync(D, LmsWorld.Member, otherEnrollment.LmsEnrollmentId, otherLesson.LmsLessonId, null)).Error.Should().Be(LmsErrors.MediaUnavailable, "no store");
			world.W.Lms.Store.SetupGet(s => s.IsConfigured).Returns(true);

			var (tool, ltiEnrollment, ltiLesson) = await LtiAsync(world, active: false);
			(await world.Apps.IssueAsync(D, LmsWorld.Member, ltiEnrollment.LmsEnrollmentId, ltiLesson.LmsLessonId, null)).Error.Should().Be(LmsErrors.MediaUnavailable, "the tool is off");
		}

		[Test]
		public async Task Without_an_lms_seat_or_after_the_lapse_window_no_link_is_issued_and_an_issued_one_is_refused_at_exchange()
		{
			var world = new AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();
			var issue = await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null);
			issue.Succeeded.Should().BeTrue();

			world.W.Lms.SeatedMembers.Remove(LmsWorld.Member);
			(await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null)).Error.Should().Be(LmsErrors.NotLicensed);
			var refused = await world.Apps.ExchangeAsync(issue.Token, "https://s", null, null);
			refused.Error.Should().Be(LmsErrors.NotLicensed, "the rules run again at exchange");
			refused.State.Status.Should().Be(LmsAppLaunchRules.StatusUnavailable);
			refused.State.Error.Should().Be("NotLicensed");
			refused.State.Kind.Should().BeNull("refused before the lesson's content was looked at: no guessed kind");
			refused.SessionToken.Should().BeNull("no page session without the right to run the lesson");
			world.W.Sessions.Rows.Should().NotContain(s => s.Kind == (int)LmsRuntimeKinds.AppSession);
			world.W.Lms.SeatedMembers.Add(LmsWorld.Member);
			(await world.Apps.ExchangeAsync(issue.Token, "https://s", null, null)).Error.Should().Be(LmsErrors.CodeExpired, "the refused link was consumed");

			var stored = world.W.Lms.Enrollments.Rows.Single(e => e.LmsEnrollmentId == enrollment.LmsEnrollmentId);
			stored.AssignedOn = DateTime.UtcNow.AddDays(-60);
			world.W.Lms.Gate = new LmsDepartmentGate { DepartmentId = D, ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = DateTime.UtcNow.AddDays(-40) };
			(await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null)).Error.Should().Be(LmsErrors.LapseWindowEnded);
		}

		#endregion The launch link

		#region Each kind's runtime

		[Test]
		public async Task A_scorm_launch_runs_the_sandboxed_content_bound_to_the_page_session()
		{
			var world = new AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();
			var page = await world.OpenAsync(enrollment, lesson);

			page.Kind.Should().Be(LmsAppLaunchKinds.Scorm12);
			page.EnrollmentId.Should().Be(enrollment.LmsEnrollmentId);
			page.LessonId.Should().Be(lesson.LmsLessonId);
			page.SessionToken.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
			page.Launch.Url.Should().StartWith("/lms-content/").And.EndWith("/index.html");
			page.PostInitial.Should().BeFalse();
			page.State.Status.Should().Be(LmsAppLaunchRules.StatusNotAttempted);

			var appSession = world.AppSessionOf(page);
			appSession.UserId.Should().Be(LmsWorld.Member);
			appSession.ExpiresOn.Should().BeCloseTo(DateTime.UtcNow.AddHours(LmsStandardsConfig.LaunchTokenHours), TimeSpan.FromMinutes(1));
			var content = world.W.Sessions.Rows.Single(s => s.TokenHash == LmsLearnerIdentity.HashToken(LmsStandardsWorld.TokenOf(page.Launch.Url)));
			content.Kind.Should().Be((int)LmsRuntimeKinds.Scorm12);
			content.AppSessionId.Should().Be(appSession.LmsRuntimeSessionId);
			content.ExpiresOn.Should().BeOnOrBefore(appSession.ExpiresOn, "the content session ends no later than the page session");
			(await world.W.Runtime.GetScormConfigAsync(LmsStandardsWorld.TokenOf(page.Launch.Url), "/commit")).Version.Should().Be("1.2");
		}

		[Test]
		public async Task A_scorm_2004_package_launches_as_scorm_2004_and_the_page_relaunches_its_own_items_only()
		{
			var world = new AppWorld();
			var (enrollment, lesson) = await world.ScormAsync(v2004: true);
			var page = await world.OpenAsync(enrollment, lesson);
			page.Kind.Should().Be(LmsAppLaunchKinds.Scorm2004);
			page.Launch.Item.Identifier.Should().Be("i1");
			page.Launch.Items.Select(i => i.Identifier).Should().Equal("i1", "i2");

			var second = await world.Apps.OpenItemAsync(page.SessionToken, "i2", "https://web.example.test");
			second.Succeeded.Should().BeTrue(second.Error.ToString());
			second.Launch.Item.Identifier.Should().Be("i2");
			second.Launch.Url.Should().Contain("/part2/index.html");
			second.SessionToken.Should().Be(page.SessionToken, "the same page session");
			var foreign = await world.Apps.OpenItemAsync(page.SessionToken, "https://elsewhere/item", "https://web.example.test");
			foreign.Launch.Item.Identifier.Should().Be("i1", "an identifier the package does not have falls back to its next unsatisfied item");
			world.W.Sessions.Rows.Where(s => s.Kind == (int)LmsRuntimeKinds.Scorm2004).Should().OnlyContain(s => s.AppSessionId == world.AppSessionOf(page).LmsRuntimeSessionId && s.LmsLessonId == lesson.LmsLessonId);

			(await world.Apps.OpenItemAsync(LmsStandardsWorld.TokenOf(page.Launch.Url), "i2", "https://s")).Error.Should().Be(LmsErrors.CodeExpired, "a content token is not a page session");
			(await world.Apps.OpenItemAsync("unknown", "i2", "https://s")).Error.Should().Be(LmsErrors.CodeExpired);
		}

		[Test]
		public async Task A_cmi5_launch_carries_the_fetch_url_and_an_au_satisfied_by_its_launch_posts_at_once()
		{
			var world = new AppWorld();
			var (enrollment, lesson) = await world.Cmi5Async();
			var page = await world.OpenAsync(enrollment, lesson);
			page.Kind.Should().Be(LmsAppLaunchKinds.Cmi5);
			var query = System.Web.HttpUtility.ParseQueryString(page.Launch.Url.Substring(page.Launch.Url.IndexOf('?')));
			query["endpoint"].Should().Be("https://web.example.test/lms-content/xapi/");
			query["fetch"].Should().StartWith("https://web.example.test/lms-content/cmi5/fetch/");
			page.PostInitial.Should().BeFalse("CompletedAndPassed needs the AU's results");
			var fetch = query["fetch"].Substring(query["fetch"].LastIndexOf('/') + 1);
			(await world.W.Runtime.FetchCmi5TokenAsync(fetch)).Succeeded.Should().BeTrue();

			var other = new AppWorld();
			var (e2, l2) = await other.Cmi5Async("NotApplicable");
			var satisfied = await other.OpenAsync(e2, l2);
			satisfied.State.Status.Should().Be(LmsAppLaunchRules.StatusCompleted);
			satisfied.State.Satisfied.Should().BeTrue();
			satisfied.PostInitial.Should().BeTrue("the launch itself recorded the result");
		}

		[Test]
		public async Task An_lti_launch_opens_the_tool_from_the_page_session_and_the_id_token_names_a_signed_return_url()
		{
			var world = new AppWorld();
			var (tool, enrollment, lesson) = await LtiAsync(world);
			var page = await world.OpenAsync(enrollment, lesson);
			page.Kind.Should().Be(LmsAppLaunchKinds.Lti13);
			page.ToolName.Should().Be("CE Provider");
			page.Launch.Should().BeNull("no content launch for a tool");

			var step = await world.Apps.BeginLtiAsync(page.SessionToken);
			step.Succeeded.Should().BeTrue(step.Error.ToString());
			step.Url.Should().StartWith("https://tool.example.test/login?");
			var login = Query(step.Url);
			var hint = world.W.Sessions.Rows.Single(s => s.Kind == (int)LmsRuntimeKinds.Lti && s.TokenHash == LmsLearnerIdentity.HashToken(login["lti_message_hint"]));
			hint.AppSessionId.Should().Be(world.AppSessionOf(page).LmsRuntimeSessionId);

			var authorized = await world.W.Lti.AuthorizeAsync(0, null, new Dictionary<string, string>
			{
				["scope"] = "openid", ["response_type"] = "id_token", ["response_mode"] = "form_post", ["client_id"] = tool.ClientId,
				["redirect_uri"] = "https://tool.example.test/launch", ["login_hint"] = login["login_hint"], ["lti_message_hint"] = login["lti_message_hint"], ["nonce"] = "n"
			}, "en");
			authorized.Succeeded.Should().BeTrue(authorized.Error.ToString());
			var payload = JObject.Parse(Base64UrlEncoder.Decode(authorized.Form["id_token"].Split('.')[1]));
			var returnUrl = (string)payload[LmsLtiRules.ClaimLaunchPresentation]["return_url"];
			returnUrl.Should().StartWith("https://web.example.test/lms-app/return?");
			var r = Query(returnUrl);
			LmsAppLaunchRules.TryValidateReturn(r["d"], r["a"], r["e"], r["sig"], DateTime.UtcNow, out var d, out var a).Should().BeTrue();
			d.Should().Be(D);
			a.Should().Be(world.AppSessionOf(page).LmsRuntimeSessionId);
			returnUrl.Should().NotContain(page.SessionToken, "the tool never gets the page session");

			var state = await world.Apps.GetReturnStateAsync(d, a);
			state.Status.Should().Be(LmsAppLaunchRules.StatusCompleted, "without grade passback the launch completes the lesson");
			state.Satisfied.Should().BeTrue();
			(await world.Apps.GetReturnStateAsync(d + 1, a)).Should().BeNull("another department");
			(await world.Apps.GetReturnStateAsync(d, hint.LmsRuntimeSessionId)).Should().BeNull("not an app session");

			// A web launch of the same lesson gets no return URL.
			var web = await world.W.Lti.BeginLaunchAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId);
			var webLogin = Query(web.Url);
			var webStep = await world.W.Lti.AuthorizeAsync(0, null, new Dictionary<string, string>
			{
				["scope"] = "openid", ["response_type"] = "id_token", ["response_mode"] = "form_post", ["client_id"] = tool.ClientId,
				["redirect_uri"] = "https://tool.example.test/launch", ["login_hint"] = webLogin["login_hint"], ["lti_message_hint"] = webLogin["lti_message_hint"], ["nonce"] = "n2"
			}, "en");
			JObject.Parse(Base64UrlEncoder.Decode(webStep.Form["id_token"].Split('.')[1]))[LmsLtiRules.ClaimLaunchPresentation]["return_url"].Should().BeNull();

			(await world.Apps.BeginLtiAsync(LmsStandardsWorld.TokenOf("/lms-content/x/y"))).Error.Should().Be(LmsErrors.CodeExpired);
		}

		[Test]
		public async Task The_page_session_of_a_scorm_lesson_cannot_start_an_lti_launch_and_tokens_never_cross_kinds()
		{
			var world = new AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();
			var issue = await world.Apps.IssueAsync(D, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId, null);
			var page = await world.Apps.ExchangeAsync(issue.Token, "https://web.example.test", null, null);
			var content = LmsStandardsWorld.TokenOf(page.Launch.Url);

			(await world.Apps.BeginLtiAsync(page.SessionToken)).Error.Should().Be(LmsErrors.NotFound);
			// The page session is not a content token, the content token is not a page session, the used link is neither.
			(await world.W.Runtime.ResolveContentAsync(page.SessionToken)).Should().BeNull();
			(await world.W.Runtime.GetScormConfigAsync(page.SessionToken, "/c")).Should().BeNull();
			(await world.W.Runtime.CommitScormAsync(page.SessionToken, new LmsScormCommit { Values = { ["cmi.core.lesson_status"] = "passed" } })).Status.Should().Be(401);
			(await world.W.Runtime.ResolveContentAsync(issue.Token)).Should().BeNull();
			(await world.W.Runtime.CommitScormAsync(issue.Token, new LmsScormCommit { Values = { ["cmi.core.lesson_status"] = "passed" } })).Status.Should().Be(401);
			(await world.Apps.GetStateAsync(content)).Should().BeNull();
			(await world.Apps.GetStateAsync(issue.Token)).Should().BeNull();
			(await world.Apps.ExchangeAsync(page.SessionToken, "https://s", null, null)).Error.Should().Be(LmsErrors.CodeExpired);
			(await world.Apps.ExchangeAsync(content, "https://s", null, null)).Error.Should().Be(LmsErrors.CodeExpired);
			(await world.W.Lti.AuthorizeAsync(0, null, new Dictionary<string, string>
			{
				["scope"] = "openid", ["response_type"] = "id_token", ["response_mode"] = "form_post", ["lti_message_hint"] = page.SessionToken, ["nonce"] = "n"
			}, "en")).Succeeded.Should().BeFalse("a page session is not an LTI hint");
		}

		#endregion Each kind's runtime

		#region State and the completion message

		[Test]
		public async Task The_state_follows_the_runtime_and_the_message_has_the_documented_shape()
		{
			var world = new AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();
			var page = await world.OpenAsync(enrollment, lesson);
			var content = LmsStandardsWorld.TokenOf(page.Launch.Url);

			(await world.Apps.GetStateAsync(page.SessionToken)).Status.Should().Be(LmsAppLaunchRules.StatusNotAttempted);
			(await world.W.Runtime.CommitScormAsync(content, new LmsScormCommit { Values = { ["cmi.core.lesson_status"] = "incomplete" } })).Succeeded.Should().BeTrue();
			(await world.Apps.GetStateAsync(page.SessionToken)).Status.Should().Be(LmsAppLaunchRules.StatusIncomplete);

			(await world.W.Runtime.CommitScormAsync(content, new LmsScormCommit
			{
				Values = { ["cmi.core.lesson_status"] = "passed", ["cmi.core.score.raw"] = "92" },
				Finish = true
			})).LessonSatisfied.Should().BeTrue();
			var state = await world.Apps.GetStateAsync(page.SessionToken);
			state.Status.Should().Be(LmsAppLaunchRules.StatusPassed);
			state.Satisfied.Should().BeTrue();
			state.Score.Should().Be(92);

			var json = JObject.Parse(state.ToJson());
			json.Properties().Select(p => p.Name).Should().BeEquivalentTo("type", "version", "enrollmentId", "lessonId", "kind", "status", "satisfied", "score");
			((string)json["type"]).Should().Be("resgrid.lms.lesson");
			((int)json["version"]).Should().Be(1);
			((int)json["enrollmentId"]).Should().Be(enrollment.LmsEnrollmentId);
			((int)json["lessonId"]).Should().Be(lesson.LmsLessonId);
			((string)json["kind"]).Should().Be("Scorm12");
			world.W.Lms.Progress.Rows.Single(p => p.LmsLessonId == lesson.LmsLessonId).Status.Should().Be((int)LmsLessonProgressStatuses.Completed, "GetCourse shows it");
		}

		[Test]
		public async Task A_sco_that_finishes_without_a_result_reports_terminated()
		{
			var world = new AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();
			var page = await world.OpenAsync(enrollment, lesson);
			(await world.W.Runtime.CommitScormAsync(LmsStandardsWorld.TokenOf(page.Launch.Url), new LmsScormCommit
			{
				Values = { ["cmi.core.lesson_status"] = "incomplete", ["cmi.core.exit"] = "suspend" },
				Finish = true
			})).Succeeded.Should().BeTrue();

			var state = await world.Apps.GetStateAsync(page.SessionToken);
			state.Status.Should().Be(LmsAppLaunchRules.StatusTerminated);
			state.Satisfied.Should().BeFalse();
			state.Score.Should().BeNull();
			JObject.Parse(state.ToJson())["score"].Should().BeNull("score only when reported");
		}

		[Test]
		public async Task An_app_session_stops_once_the_seat_is_removed_its_runtime_calls_are_refused_and_the_state_says_why()
		{
			var world = new AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();
			var page = await world.OpenAsync(enrollment, lesson);
			var content = LmsStandardsWorld.TokenOf(page.Launch.Url);
			var start = DateTime.UtcNow;
			world.W.Runtime.Clock = () => start;
			world.Apps.Clock = () => start;
			(await world.W.Runtime.CommitScormAsync(content, new LmsScormCommit { Values = { ["cmi.core.lesson_location"] = "p1" } })).Succeeded.Should().BeTrue();
			(await world.Apps.GetStateAsync(page.SessionToken)).Status.Should().NotBe(LmsAppLaunchRules.StatusUnavailable);

			world.W.Lms.SeatedMembers.Remove(LmsWorld.Member);
			world.W.Runtime.Clock = () => start.AddMinutes(2);
			world.Apps.Clock = () => start.AddMinutes(2);

			var refused = await world.W.Runtime.CommitScormAsync(content, new LmsScormCommit { Values = { ["cmi.core.lesson_status"] = "passed", ["cmi.core.lesson_location"] = "p9" } });
			refused.Status.Should().Be(403, "every runtime call of an app session re-applies the player's rules");
			LmsScormState.Parse(world.W.Registrations.Rows.Single().StateJson).Scos.Single().Value.Values["cmi.core.lesson_location"].Should().Be("p1", "nothing was written");
			(await world.W.Runtime.GetScormConfigAsync(content, "/c")).Should().BeNull();
			(await world.W.Runtime.ResolveContentAsync(content)).Should().BeNull();
			var state = await world.Apps.GetStateAsync(page.SessionToken);
			state.Status.Should().Be(LmsAppLaunchRules.StatusUnavailable);
			state.Error.Should().Be("NotLicensed");
			(await world.Apps.OpenItemAsync(page.SessionToken, null, "https://s")).Error.Should().Be(LmsErrors.NotLicensed);

			world.W.Lms.SeatedMembers.Add(LmsWorld.Member);
			(await world.Apps.GetStateAsync(page.SessionToken)).Status.Should().NotBe(LmsAppLaunchRules.StatusUnavailable, "a refusal is never cached");
		}

		[Test]
		public async Task A_cmi5_app_session_refuses_its_fetch_url_and_lrs_once_the_lapse_window_ends()
		{
			var world = new AppWorld();
			var (enrollment, lesson) = await world.Cmi5Async();
			var page = await world.OpenAsync(enrollment, lesson);
			var query = System.Web.HttpUtility.ParseQueryString(page.Launch.Url.Substring(page.Launch.Url.IndexOf('?')));
			var fetch = query["fetch"].Substring(query["fetch"].LastIndexOf('/') + 1);

			var stored = world.W.Lms.Enrollments.Rows.Single(e => e.LmsEnrollmentId == enrollment.LmsEnrollmentId);
			stored.AssignedOn = DateTime.UtcNow.AddDays(-60);
			world.W.Lms.Gate = new LmsDepartmentGate { DepartmentId = D, ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = DateTime.UtcNow.AddDays(-40) };

			var outcome = await world.W.Runtime.FetchCmi5TokenAsync(fetch);
			outcome.Status.Should().Be(403);
			((string)((JObject)outcome.Value)["error-code"]).Should().Be("3");
			world.W.Sessions.Rows.Single(s => s.Kind == (int)LmsRuntimeKinds.Cmi5).FetchedOn.Should().BeNull("the fetch URL was not used up");
			(await world.Apps.GetStateAsync(page.SessionToken)).Error.Should().Be("LapseWindowEnded");
		}

		[Test]
		public async Task An_expired_page_session_reads_nothing()
		{
			var world = new AppWorld();
			var (enrollment, lesson) = await world.ScormAsync();
			var page = await world.OpenAsync(enrollment, lesson);
			world.Apps.Clock = () => DateTime.UtcNow.AddHours(LmsStandardsConfig.LaunchTokenHours + 1);
			(await world.Apps.GetStateAsync(page.SessionToken)).Should().BeNull();
			(await world.Apps.OpenItemAsync(page.SessionToken, null, "https://s")).Error.Should().Be(LmsErrors.CodeExpired);
			(await world.Apps.BeginLtiAsync(page.SessionToken)).Error.Should().Be(LmsErrors.CodeExpired);
			(await world.Apps.GetStateAsync(null)).Should().BeNull();
			(await world.Apps.GetStateAsync(new string('x', 200))).Should().BeNull();
		}

		#endregion State and the completion message

		#region Rules

		[TestCase(null, LmsAppLaunchRules.StatusNotAttempted)]
		[TestCase("not attempted|unknown|False", LmsAppLaunchRules.StatusNotAttempted)]
		[TestCase("incomplete|unknown|False", LmsAppLaunchRules.StatusIncomplete)]
		[TestCase("completed|unknown|True", LmsAppLaunchRules.StatusCompleted)]
		[TestCase("incomplete|unknown|True", LmsAppLaunchRules.StatusCompleted)]
		[TestCase("completed|passed|True", LmsAppLaunchRules.StatusPassed)]
		[TestCase("completed|failed|False", LmsAppLaunchRules.StatusFailed)]
		public void The_status_comes_from_the_registration(string registration, string expected)
		{
			LmsRuntimeRegistration reg = null;
			if (registration != null)
			{
				var parts = registration.Split('|');
				reg = new LmsRuntimeRegistration { CompletionStatus = parts[0], SuccessStatus = parts[1], Satisfied = bool.Parse(parts[2]) };
			}
			LmsAppLaunchRules.Status(reg, null).Should().Be(expected);
			LmsAppLaunchRules.Status(reg, new LmsRuntimeSession { EndedOn = DateTime.UtcNow })
				.Should().Be(expected == LmsAppLaunchRules.StatusNotAttempted || expected == LmsAppLaunchRules.StatusIncomplete ? LmsAppLaunchRules.StatusTerminated : expected,
					"an ended session without a result is terminated");
		}

		[Test]
		public void Only_well_formed_app_origins_are_kept()
		{
			LmsAppLaunchRules.ParseOrigins("https://Responder.Resgrid.com/ app://bundle, http://localhost:8081;http://responder.resgrid.local https://responder.resgrid.com")
				.Should().Equal("https://responder.resgrid.com", "app://bundle", "http://localhost:8081", "http://responder.resgrid.local");
			LmsAppLaunchRules.ParseOrigins("http://example.com https://a.example.com/path * https://*.example.com javascript://x data://x file://x 'self' " +
				"https://a.example.com\" https://user@a.example.com https://a.example.com:99999 https://a.example.com?x=1 https://a.example.com#x").Should().BeEmpty();
			// A semicolon only separates entries: "x;script-src *" can never become a directive.
			LmsAppLaunchRules.ParseOrigins("https://a.example.com;script-src *").Should().Equal("https://a.example.com");
			LmsAppLaunchRules.ParseOrigins(string.Join(" ", Enumerable.Range(1, 20).Select(i => "https://h" + i + ".example.com"))).Should().HaveCount(LmsAppLaunchRules.MaxFrameOrigins);
			LmsAppLaunchRules.ParseOrigins(null).Should().BeEmpty();
			LmsAppLaunchRules.FrameAncestors(new string[0]).Should().Be("'none'");
			LmsAppLaunchRules.FrameAncestors(new[] { "https://a.example.com", "app://bundle" }).Should().Be("https://a.example.com app://bundle");
		}

		[Test]
		public void A_return_link_validates_only_untampered_unexpired_and_with_the_passphrase()
		{
			var now = DateTime.UtcNow;
			var url = LmsAppLaunchRules.ReturnUrl(D, 77, now.AddHours(1));
			var q = Query(url);
			LmsAppLaunchRules.TryValidateReturn(q["d"], q["a"], q["e"], q["sig"], now, out var d, out var a).Should().BeTrue();
			(d, a).Should().Be((D, 77));
			LmsAppLaunchRules.TryValidateReturn(q["d"], "78", q["e"], q["sig"], now, out _, out _).Should().BeFalse("another session");
			LmsAppLaunchRules.TryValidateReturn((D + 1).ToString(), q["a"], q["e"], q["sig"], now, out _, out _).Should().BeFalse("another department");
			LmsAppLaunchRules.TryValidateReturn(q["d"], q["a"], (long.Parse(q["e"]) + 60).ToString(), q["sig"], now, out _, out _).Should().BeFalse("a moved expiry");
			LmsAppLaunchRules.TryValidateReturn(q["d"], q["a"], q["e"], q["sig"], now.AddHours(2), out _, out _).Should().BeFalse("expired");
			var far = Query(LmsAppLaunchRules.ReturnUrl(D, 77, now.AddDays(30)));
			LmsAppLaunchRules.TryValidateReturn(far["d"], far["a"], far["e"], far["sig"], now, out _, out _).Should().BeFalse("further out than a session lasts");

			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = "another";
			LmsAppLaunchRules.TryValidateReturn(q["d"], q["a"], q["e"], q["sig"], now, out _, out _).Should().BeFalse("another key");
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = "";
			LmsAppLaunchRules.ReturnUrl(D, 77, now.AddHours(1)).Should().BeNull("no key, no link");
		}

		[Test]
		public void The_same_platform_key_signs_any_number_of_launches_in_one_process()
		{
			// Regression (round 10 A8): the signature provider cache held the disposed RSA of the first launch; the second one threw.
			var (kid, _, privateKey) = LmsLtiCrypto.NewKey();
			var claims = new Dictionary<string, object> { ["sub"] = "s", ["nonce"] = "n" };
			for (var i = 0; i < 3; i++)
				LmsLtiCrypto.Sign(privateKey, kid, "https://issuer.example.test", "client", claims, DateTime.UtcNow, TimeSpan.FromMinutes(5)).Split('.').Should().HaveCount(3);
		}

		[Test]
		public void The_limiter_counts_a_sliding_minute_per_key()
		{
			var limiter = new LmsAppLaunchLimiter();
			var now = DateTime.UtcNow;
			Enumerable.Range(0, 3).All(_ => limiter.Allow("a", 3, now)).Should().BeTrue();
			limiter.Allow("a", 3, now).Should().BeFalse();
			limiter.Allow("b", 3, now).Should().BeTrue("per key");
			limiter.Allow("a", 3, now.AddSeconds(61)).Should().BeTrue("the window slides");
		}

		#endregion Rules
	}
}
