using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// T5 item 3 LTI 1.3 tool consumer: tool registration, OIDC third-party-initiated login, the RS256 id_token (verified here with the published
	/// key set), key rotation, the AGS token endpoint (client assertion signed with a test key) and score passback completing the lesson.
	/// </summary>
	[TestFixture]
	public class LmsLtiTests
	{
		private RSA _toolKey;
		private string _toolJwks;
		private const string ToolKid = "tool-key-1";

		[SetUp]
		public void SetUp()
		{
			_toolKey = RSA.Create(2048);
			var parameters = _toolKey.ExportParameters(false);
			_toolJwks = new JObject
			{
				["keys"] = new JArray(new JObject
				{
					["kty"] = "RSA", ["kid"] = ToolKid, ["use"] = "sig", ["alg"] = "RS256",
					["n"] = Base64UrlEncoder.Encode(parameters.Modulus), ["e"] = Base64UrlEncoder.Encode(parameters.Exponent)
				})
			}.ToString();
		}

		[TearDown]
		public void TearDown() => _toolKey?.Dispose();

		private LmsLtiToolInput ToolInput(bool passback = true, bool shareName = false) => new LmsLtiToolInput
		{
			Name = "CE Provider", OidcLoginUrl = "https://tool.example.test/login", LaunchUrl = "https://tool.example.test/launch",
			RedirectUris = "https://tool.example.test/launch", PublicJwksJson = _toolJwks, AllowGradePassback = passback, ShareName = shareName, IsActive = true,
			CustomParameters = "course_code=ACLS-1\nbad key=x"
		};

		private static Dictionary<string, string> Query(string url)
		{
			var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
			return query.AllKeys.ToDictionary(k => k, k => query[k]);
		}

		private async Task<(LmsStandardsWorld World, LmsLtiTool Tool, LmsEnrollment Enrollment, LmsLesson Lesson)> LtiCourseAsync(bool passback = true, int passMark = 80, bool shareName = false)
		{
			var world = new LmsStandardsWorld();
			var saved = await world.Lti.SaveToolAsync(world.Lms.AdminActor, ToolInput(passback, shareName));
			saved.Succeeded.Should().BeTrue(saved.Error.ToString());
			var tool = (LmsLtiTool)saved.Value;
			var (enrollment, lesson, _) = await world.CourseWithAsync(LmsLessonKinds.LtiLink, toolId: tool.LmsLtiToolId, passMark: passMark);
			return (world, tool, enrollment, lesson);
		}

		private static async Task<LmsLtiLaunchStep> LaunchAsync(LmsStandardsWorld world, LmsLtiTool tool, LmsEnrollment enrollment, LmsLesson lesson, string nonce = "n-1")
		{
			var begin = await world.Lti.BeginLaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId);
			begin.Succeeded.Should().BeTrue(begin.Error.ToString());
			var login = Query(begin.Url);
			return await world.Lti.AuthorizeAsync(LmsWorld.DepartmentId, LmsWorld.Member, new Dictionary<string, string>
			{
				["scope"] = "openid", ["response_type"] = "id_token", ["response_mode"] = "form_post", ["prompt"] = "none", ["client_id"] = tool.ClientId,
				["redirect_uri"] = "https://tool.example.test/launch", ["login_hint"] = login["login_hint"], ["lti_message_hint"] = login["lti_message_hint"],
				["nonce"] = nonce, ["state"] = "s-1"
			}, "en");
		}

		private static async Task<JsonWebToken> VerifyAsync(LmsStandardsWorld world, string idToken, string clientId)
		{
			var keys = new JsonWebKeySet(await world.Lti.GetKeySetJsonAsync(LmsWorld.DepartmentId, DateTime.UtcNow)).GetSigningKeys();
			var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
			{
				ValidIssuer = LmsLtiRules.Issuer(LmsWorld.DepartmentId),
				ValidAudience = clientId,
				IssuerSigningKeys = keys,
				ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }
			});
			result.IsValid.Should().BeTrue(result.Exception?.Message);
			return (JsonWebToken)result.SecurityToken;
		}

		[Test]
		public async Task A_tool_needs_https_endpoints_and_a_key_set_and_gets_its_own_client_and_deployment_ids()
		{
			var world = new LmsStandardsWorld();
			var input = ToolInput();
			input.LaunchUrl = "http://tool.example.test/launch";
			(await world.Lti.SaveToolAsync(world.Lms.AdminActor, input)).Error.Should().Be(LmsErrors.InvalidInput);
			input = ToolInput();
			input.PublicJwksJson = null;
			(await world.Lti.SaveToolAsync(world.Lms.AdminActor, input)).Error.Should().Be(LmsErrors.InvalidInput, "no key set at all");
			(await world.Lti.SaveToolAsync(world.Lms.Actor(LmsWorld.Member), ToolInput())).Error.Should().Be(LmsErrors.NotAuthorized);

			var saved = await world.Lti.SaveToolAsync(world.Lms.AdminActor, ToolInput());
			var tool = (LmsLtiTool)saved.Value;
			Guid.TryParse(tool.ClientId, out _).Should().BeTrue();
			tool.DeploymentId.Should().NotBeNullOrEmpty();
			world.Keys.Rows.Should().ContainSingle(k => k.Status == (int)LmsLtiKeyStatuses.Active, "the first tool creates the platform key");
			world.Keys.Rows.Single().PrivateKeyEncrypted.Should().StartWith("enc:", "the private key is stored encrypted");
			(await world.Lti.GetPlatformDetailsAsync(LmsWorld.DepartmentId)).ActiveKid.Should().Be(world.Keys.Rows.Single().Kid);
		}

		[Test]
		public async Task The_login_initiation_carries_the_issuer_hints_target_client_and_deployment()
		{
			var (world, tool, enrollment, lesson) = await LtiCourseAsync();
			var begin = await world.Lti.BeginLaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId);

			begin.Url.Should().StartWith("https://tool.example.test/login?");
			var query = Query(begin.Url);
			query["iss"].Should().Be(LmsLtiRules.Issuer(LmsWorld.DepartmentId));
			query["login_hint"].Should().Be(LmsLearnerIdentity.Subject(LmsWorld.DepartmentId, LmsWorld.Member));
			query["target_link_uri"].Should().Be("https://tool.example.test/launch");
			query["client_id"].Should().Be(tool.ClientId);
			query["lti_deployment_id"].Should().Be(tool.DeploymentId);
			world.Sessions.Rows.Single().TokenHash.Should().Be(LmsLearnerIdentity.HashToken(query["lti_message_hint"]));
		}

		[Test]
		public async Task The_id_token_is_signed_with_the_platform_key_and_carries_the_lti_claims()
		{
			var (world, tool, enrollment, lesson) = await LtiCourseAsync();
			var step = await LaunchAsync(world, tool, enrollment, lesson);

			step.Succeeded.Should().BeTrue(step.Error.ToString());
			step.Url.Should().Be("https://tool.example.test/launch");
			step.Form["state"].Should().Be("s-1");
			var token = await VerifyAsync(world, step.Form["id_token"], tool.ClientId);

			token.Kid.Should().Be(world.Keys.Rows.Single().Kid);
			token.Subject.Should().Be(LmsLearnerIdentity.Subject(LmsWorld.DepartmentId, LmsWorld.Member));
			token.GetClaim("nonce").Value.Should().Be("n-1");
			token.GetClaim(LmsLtiRules.ClaimMessageType).Value.Should().Be("LtiResourceLinkRequest");
			token.GetClaim(LmsLtiRules.ClaimVersion).Value.Should().Be("1.3.0");
			token.GetClaim(LmsLtiRules.ClaimDeploymentId).Value.Should().Be(tool.DeploymentId);
			var payload = JObject.Parse(Base64UrlEncoder.Decode(token.EncodedPayload));
			((string)payload[LmsLtiRules.ClaimResourceLink]["id"]).Should().Be("lesson-" + lesson.LmsLessonId);
			((string)payload[LmsLtiRules.ClaimRoles][0]).Should().Be(LmsLtiRules.RoleLearner);
			((string)payload[LmsLtiRules.ClaimAgsEndpoint]["lineitem"]).Should().Be(LmsLtiRules.LineItemUrl(LmsWorld.DepartmentId, lesson.LmsLessonId));
			((string)payload[LmsLtiRules.ClaimCustom]["course_code"]).Should().Be("ACLS-1");
			payload[LmsLtiRules.ClaimCustom]["bad key"].Should().BeNull();
			payload["name"].Should().BeNull("names are not shared unless switched on");
			payload["email"].Should().BeNull();
			payload.ToString().Should().NotContain(LmsWorld.Member, "the internal user id never leaves Resgrid");
		}

		[Test]
		public async Task Names_are_shared_only_when_the_tool_is_set_to()
		{
			var (world, tool, enrollment, lesson) = await LtiCourseAsync(shareName: true);
			var step = await LaunchAsync(world, tool, enrollment, lesson);
			var payload = JObject.Parse(Base64UrlEncoder.Decode((await VerifyAsync(world, step.Form["id_token"], tool.ClientId)).EncodedPayload));
			((string)payload["name"]).Should().Be("Pat Member");
			payload["email"].Should().BeNull();
		}

		[Test]
		public async Task The_authorization_request_is_refused_for_another_user_client_redirect_or_a_reused_hint()
		{
			var (world, tool, enrollment, lesson) = await LtiCourseAsync();
			var begin = await world.Lti.BeginLaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId);
			var login = Query(begin.Url);
			Dictionary<string, string> Request(string clientId = null, string redirect = null) => new Dictionary<string, string>
			{
				["scope"] = "openid", ["response_type"] = "id_token", ["response_mode"] = "form_post", ["client_id"] = clientId ?? tool.ClientId,
				["redirect_uri"] = redirect ?? "https://tool.example.test/launch", ["login_hint"] = login["login_hint"], ["lti_message_hint"] = login["lti_message_hint"], ["nonce"] = "n"
			};

			(await world.Lti.AuthorizeAsync(LmsWorld.DepartmentId, LmsWorld.Member2, Request(), "en")).Error.Should().Be(LmsErrors.NotAuthorized, "another signed-in member");
			(await world.Lti.AuthorizeAsync(LmsWorld.DepartmentId + 1, LmsWorld.Member, Request(), "en")).Error.Should().Be(LmsErrors.NotAuthorized, "signed in to another department");
			(await world.Lti.AuthorizeAsync(LmsWorld.DepartmentId, LmsWorld.Member, Request(clientId: Guid.NewGuid().ToString()), "en")).Error.Should().Be(LmsErrors.NotAuthorized);
			(await world.Lti.AuthorizeAsync(LmsWorld.DepartmentId, LmsWorld.Member, Request(redirect: "https://attacker.example.test/steal"), "en")).Error.Should().Be(LmsErrors.NotAuthorized);
			(await world.Lti.AuthorizeAsync(LmsWorld.DepartmentId, LmsWorld.Member, Request(), "en")).Succeeded.Should().BeTrue();
			(await world.Lti.AuthorizeAsync(LmsWorld.DepartmentId, LmsWorld.Member, Request(), "en")).Error.Should().Be(LmsErrors.CodeExpired, "the hint is single use");
		}

		[Test]
		public async Task Security_review_the_cross_site_authorize_works_with_the_hint_alone_and_fails_without_it_or_replayed()
		{
			// The member cookie is SameSite=Strict: the tool's redirect to /lti/authorize arrives with no member session (no department, no user).
			var (world, tool, enrollment, lesson) = await LtiCourseAsync();
			var begin = await world.Lti.BeginLaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId);
			var login = Query(begin.Url);
			Dictionary<string, string> Request(string hint) => new Dictionary<string, string>
			{
				["scope"] = "openid", ["response_type"] = "id_token", ["response_mode"] = "form_post", ["client_id"] = tool.ClientId,
				["redirect_uri"] = "https://tool.example.test/launch", ["login_hint"] = login["login_hint"], ["lti_message_hint"] = hint, ["nonce"] = "n-x", ["state"] = "s-x"
			};

			(await world.Lti.AuthorizeAsync(0, null, Request(null), "en")).Succeeded.Should().BeFalse("no hint, no launch");
			(await world.Lti.AuthorizeAsync(0, null, Request(LmsLearnerIdentity.NewToken()), "en")).Error.Should().Be(LmsErrors.CodeExpired, "a guessed hint names no launch");

			var step = await world.Lti.AuthorizeAsync(0, null, Request(login["lti_message_hint"]), "en");
			step.Succeeded.Should().BeTrue(step.Error.ToString());
			var token = await VerifyAsync(world, step.Form["id_token"], tool.ClientId);
			token.Subject.Should().Be(LmsLearnerIdentity.Subject(LmsWorld.DepartmentId, LmsWorld.Member), "the hint names the member who started the launch");

			(await world.Lti.AuthorizeAsync(0, null, Request(login["lti_message_hint"]), "en")).Error.Should().Be(LmsErrors.CodeExpired, "replayed");

			// A hint is bound to its tool: another registered tool cannot redeem it.
			var other = (LmsLtiTool)(await world.Lti.SaveToolAsync(world.Lms.AdminActor, ToolInput())).Value;
			var second = Query((await world.Lti.BeginLaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId)).Url);
			var forOther = Request(second["lti_message_hint"]);
			forOther["client_id"] = other.ClientId;
			(await world.Lti.AuthorizeAsync(0, null, forOther, "en")).Error.Should().Be(LmsErrors.NotAuthorized);

			// An expired hint is refused.
			var third = Query((await world.Lti.BeginLaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId)).Url);
			world.Sessions.Rows.Single(s => s.TokenHash == LmsLearnerIdentity.HashToken(third["lti_message_hint"])).ExpiresOn = DateTime.UtcNow.AddMinutes(-1);
			(await world.Lti.AuthorizeAsync(0, null, Request(third["lti_message_hint"]), "en")).Error.Should().Be(LmsErrors.CodeExpired, "the hint lasts minutes");
		}

		[Test]
		public async Task Security_review_the_authorize_endpoint_takes_no_member_cookie_and_passes_no_identity_when_none_came()
		{
			typeof(Resgrid.Web.Controllers.LmsLtiController).GetMethod(nameof(Resgrid.Web.Controllers.LmsLtiController.Authorize))
				.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false).Should().NotBeEmpty("the cross-site redirect has no member cookie");
			typeof(Resgrid.Web.Controllers.LmsLtiController).GetMethod(nameof(Resgrid.Web.Controllers.LmsLtiController.Authorize))
				.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false).Should().BeEmpty();

			var lti = new Moq.Mock<Resgrid.Model.Services.ILmsLtiService>();
			lti.Setup(l => l.AuthorizeAsync(0, null, Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<string>(), Moq.It.IsAny<System.Threading.CancellationToken>()))
				.ReturnsAsync(new LmsLtiLaunchStep { Url = "https://tool.example.test/launch", Form = { ["id_token"] = "t" } });
			var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
			http.Request.Method = "GET";
			http.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?lti_message_hint=abc&client_id=c");
			var controller = new Resgrid.Web.Controllers.LmsLtiController(lti.Object)
			{
				ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = http }
			};

			var result = await controller.Authorize(System.Threading.CancellationToken.None);

			result.Should().BeOfType<Microsoft.AspNetCore.Mvc.ContentResult>().Which.StatusCode.Should().Be(200);
			lti.Verify(l => l.AuthorizeAsync(0, null, Moq.It.Is<IDictionary<string, string>>(r => r["lti_message_hint"] == "abc"), Moq.It.IsAny<string>(),
				Moq.It.IsAny<System.Threading.CancellationToken>()), Moq.Times.Once);
		}

		[Test]
		public async Task Rotating_the_key_signs_with_the_new_one_and_keeps_the_old_one_published_for_a_while()
		{
			var (world, tool, enrollment, lesson) = await LtiCourseAsync();
			var oldKid = world.Keys.Rows.Single().Kid;
			(await world.Lti.RotateKeyAsync(world.Lms.AdminActor)).Succeeded.Should().BeTrue();

			var keySet = JObject.Parse(await world.Lti.GetKeySetJsonAsync(LmsWorld.DepartmentId, DateTime.UtcNow));
			keySet["keys"].Select(k => (string)k["kid"]).Should().HaveCount(2).And.Contain(oldKid);
			keySet.ToString().Should().NotContain("\"d\"", "only public key material is published");
			var step = await LaunchAsync(world, tool, enrollment, lesson);
			(await VerifyAsync(world, step.Form["id_token"], tool.ClientId)).Kid.Should().NotBe(oldKid);

			var later = JObject.Parse(await world.Lti.GetKeySetJsonAsync(LmsWorld.DepartmentId, DateTime.UtcNow.AddDays(8)));
			later["keys"].Select(k => (string)k["kid"]).Should().NotContain(oldKid);
		}

		private string Assertion(LmsLtiTool tool, string audience = null, string jti = null, RSA key = null)
		{
			var handler = new JsonWebTokenHandler();
			return handler.CreateToken(new SecurityTokenDescriptor
			{
				Issuer = tool.ClientId,
				Audience = audience ?? LmsLtiRules.TokenUrl(LmsWorld.DepartmentId),
				Expires = DateTime.UtcNow.AddMinutes(5),
				Claims = new Dictionary<string, object> { ["sub"] = tool.ClientId, ["jti"] = jti ?? Guid.NewGuid().ToString() },
				SigningCredentials = new SigningCredentials(new RsaSecurityKey(key ?? _toolKey) { KeyId = ToolKid }, SecurityAlgorithms.RsaSha256)
			});
		}

		private static Dictionary<string, string> TokenForm(string assertion, string scope = LmsLtiRules.ScopeScore + " " + LmsLtiRules.ScopeLineItemReadOnly) => new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials", ["client_assertion_type"] = LmsLtiRules.ClientAssertionType, ["client_assertion"] = assertion, ["scope"] = scope
		};

		[Test]
		public async Task The_token_endpoint_verifies_the_tools_assertion_and_refuses_a_replay()
		{
			var (world, tool, _, _) = await LtiCourseAsync();
			var assertion = Assertion(tool, jti: "jti-1");

			var issued = await world.Lti.IssueTokenAsync(LmsWorld.DepartmentId, TokenForm(assertion));
			issued.Succeeded.Should().BeTrue(issued.OAuthError);
			issued.Scope.Split(' ').Should().BeEquivalentTo(LmsLtiRules.ScopeScore, LmsLtiRules.ScopeLineItemReadOnly);
			world.Tokens.Rows.Single().TokenHash.Should().Be(LmsLearnerIdentity.HashToken(issued.AccessToken));

			(await world.Lti.IssueTokenAsync(LmsWorld.DepartmentId, TokenForm(assertion))).OAuthError.Should().Be("invalid_grant", "the jti was used");
			(await world.Lti.IssueTokenAsync(LmsWorld.DepartmentId, TokenForm(Assertion(tool, audience: "https://elsewhere.test/token")))).OAuthError.Should().Be("invalid_client");
			using var otherKey = RSA.Create(2048);
			(await world.Lti.IssueTokenAsync(LmsWorld.DepartmentId, TokenForm(Assertion(tool, key: otherKey)))).OAuthError.Should().Be("invalid_client", "signed with a key the tool did not register");
			(await world.Lti.IssueTokenAsync(LmsWorld.DepartmentId, TokenForm(Assertion(tool), "https://purl.imsglobal.org/spec/lti-nrps/scope/contextmembership.readonly"))).OAuthError
				.Should().Be("invalid_scope", "Names and Roles is not offered");
			(await world.Lti.IssueTokenAsync(99, TokenForm(Assertion(tool)))).OAuthError.Should().Be("invalid_client", "another department's platform");
		}

		[Test]
		public async Task A_fully_graded_score_at_the_pass_mark_completes_the_lesson_and_an_older_one_is_ignored()
		{
			var (world, tool, enrollment, lesson) = await LtiCourseAsync(passMark: 80);
			(await LaunchAsync(world, tool, enrollment, lesson)).Succeeded.Should().BeTrue();
			var bearer = (await world.Lti.IssueTokenAsync(LmsWorld.DepartmentId, TokenForm(Assertion(tool)))).AccessToken;
			var subject = LmsLearnerIdentity.Subject(LmsWorld.DepartmentId, LmsWorld.Member);
			string Score(decimal given, string grading, DateTime at) => new JObject
			{
				["userId"] = subject, ["scoreGiven"] = given, ["scoreMaximum"] = 100, ["activityProgress"] = "Completed", ["gradingProgress"] = grading,
				["timestamp"] = at.ToString("o")
			}.ToString();

			var t0 = DateTime.UtcNow;
			(await world.Lti.PostScoreAsync(LmsWorld.DepartmentId, lesson.LmsLessonId, bearer, Score(70, "FullyGraded", t0))).Status.Should().Be(204);
			world.Registrations.Rows.Single().Satisfied.Should().BeFalse("70 is below the pass mark");
			world.Registrations.Rows.Single().SuccessStatus.Should().Be("failed");

			var passed = await world.Lti.PostScoreAsync(LmsWorld.DepartmentId, lesson.LmsLessonId, bearer, Score(88, "FullyGraded", t0.AddMinutes(1)));
			passed.LessonSatisfied.Should().BeTrue();
			passed.CourseCompleted.Should().BeTrue();
			world.Lms.Progress.Rows.Single(p => p.LmsLessonId == lesson.LmsLessonId).Status.Should().Be((int)LmsLessonProgressStatuses.Completed);
			world.Registrations.Rows.Single().ScorePercent.Should().Be(88);

			(await world.Lti.PostScoreAsync(LmsWorld.DepartmentId, lesson.LmsLessonId, bearer, Score(10, "FullyGraded", t0.AddSeconds(30)))).Status.Should().Be(204);
			world.Registrations.Rows.Single().ScorePercent.Should().Be(88, "an older score never replaces a newer one");
		}

		[Test]
		public async Task Score_passback_needs_the_score_scope_the_lessons_own_tool_and_a_known_learner()
		{
			var (world, tool, enrollment, lesson) = await LtiCourseAsync();
			(await LaunchAsync(world, tool, enrollment, lesson)).Succeeded.Should().BeTrue();
			var readOnly = (await world.Lti.IssueTokenAsync(LmsWorld.DepartmentId, TokenForm(Assertion(tool), LmsLtiRules.ScopeLineItemReadOnly))).AccessToken;
			var full = (await world.Lti.IssueTokenAsync(LmsWorld.DepartmentId, TokenForm(Assertion(tool)))).AccessToken;
			var score = new JObject { ["userId"] = LmsLearnerIdentity.Subject(LmsWorld.DepartmentId, LmsWorld.Member), ["activityProgress"] = "Completed", ["gradingProgress"] = "FullyGraded" };

			(await world.Lti.PostScoreAsync(LmsWorld.DepartmentId, lesson.LmsLessonId, readOnly, score.ToString())).Status.Should().Be(401);
			(await world.Lti.PostScoreAsync(LmsWorld.DepartmentId, lesson.LmsLessonId, "forged", score.ToString())).Status.Should().Be(401);
			(await world.Lti.PostScoreAsync(LmsWorld.DepartmentId, lesson.LmsLessonId + 1000, full, score.ToString())).Status.Should().Be(404);
			var stranger = (JObject)score.DeepClone();
			stranger["userId"] = "someone-else";
			(await world.Lti.PostScoreAsync(LmsWorld.DepartmentId, lesson.LmsLessonId, full, stranger.ToString())).Status.Should().Be(404);
			(await world.Lti.PostScoreAsync(LmsWorld.DepartmentId, lesson.LmsLessonId, full, "{\"userId\":\"x\"}")).Status.Should().Be(400);
			(await world.Lti.GetLineItemAsync(LmsWorld.DepartmentId, lesson.LmsLessonId, readOnly)).Status.Should().Be(200);
		}

		[Test]
		public async Task Without_grade_passback_opening_the_tool_completes_the_lesson()
		{
			var (world, tool, enrollment, lesson) = await LtiCourseAsync(passback: false, passMark: 0);
			(await world.Validator.ApplyAsync(world.Lms.AdminActor, new LmsLesson(), new LmsLessonInput { Kind = (int)LmsLessonKinds.LtiLink, LmsLtiToolId = tool.LmsLtiToolId, PassMark = 80 }))
				.Should().Be(LmsErrors.None);
			(await LaunchAsync(world, tool, enrollment, lesson)).Succeeded.Should().BeTrue();
			world.Lms.Progress.Rows.Single(p => p.LmsLessonId == lesson.LmsLessonId).Status.Should().Be((int)LmsLessonProgressStatuses.Completed);
			(await world.Lti.IssueTokenAsync(LmsWorld.DepartmentId, TokenForm(Assertion(tool)))).OAuthError.Should().Be("invalid_client", "no passback, no token");
		}

		[Test]
		public async Task An_lti_launch_fails_closed_while_the_lms_is_dark()
		{
			var (world, _, enrollment, lesson) = await LtiCourseAsync();
			world.Lms.Gate = new LmsDepartmentGate { DepartmentId = LmsWorld.DepartmentId, ModuleOn = true, FlagOn = true, AddonActive = null };
			(await world.Lti.BeginLaunchAsync(LmsWorld.DepartmentId, LmsWorld.Member, enrollment.LmsEnrollmentId, lesson.LmsLessonId)).Succeeded.Should().BeFalse();
			world.Sessions.Rows.Should().BeEmpty();
		}

		[Test]
		public void The_score_rule_needs_completed_fully_graded_and_the_pass_mark()
		{
			LmsLtiRules.Evaluate(new LmsLtiScoreState { ScoreGiven = 9, ScoreMaximum = 10, ActivityProgress = "Completed", GradingProgress = "FullyGraded" }, 80).Should().Be((true, 90m));
			LmsLtiRules.Evaluate(new LmsLtiScoreState { ScoreGiven = 9, ScoreMaximum = 10, ActivityProgress = "Completed", GradingProgress = "Pending" }, 80).Satisfied.Should().BeFalse();
			LmsLtiRules.Evaluate(new LmsLtiScoreState { ActivityProgress = "Completed", GradingProgress = "FullyGraded" }, 0).Satisfied.Should().BeTrue("a pass mark of 0 needs no score");
			LmsLtiRules.Evaluate(new LmsLtiScoreState { ActivityProgress = "Completed", GradingProgress = "FullyGraded" }, 50).Satisfied.Should().BeFalse();
			LmsLtiRules.ParseScore(JObject.Parse("{\"userId\":\"u\",\"scoreGiven\":5,\"activityProgress\":\"Completed\",\"gradingProgress\":\"FullyGraded\"}")).Score.Should().BeNull("a score needs its maximum");
		}
	}
}
