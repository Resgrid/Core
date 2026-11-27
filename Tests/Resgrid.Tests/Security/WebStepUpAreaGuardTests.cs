using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Tests.Security.Live;
using Resgrid.Web.Attributes;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Round 10 A5: the Web guard with a step-up area follows the department's rule. Optional areas pass, a fresh second factor or an
	/// allowed password / SSO reentry passes and is handed to the signature, and otherwise the submission is held and the member goes to
	/// the verification they can actually complete — never stranded, and never waved through on a policy fault.
	/// </summary>
	[TestFixture]
	public class WebStepUpAreaGuardTests
	{
		private const string UserId = "user-1";
		private const string SessionId = "session-9";
		private const int DepartmentId = 42;

		private InMemoryCacheProvider _cache;
		private Mock<IStepUpPolicyService> _policy;
		private StepUpCapabilities _capabilities;
		private StepUpEvaluation _evaluation;

		[SetUp]
		public void SetUp()
		{
			_cache = new InMemoryCacheProvider();
			_capabilities = new StepUpCapabilities { TotpEnrolled = true };
			_evaluation = new StepUpEvaluation { Rule = StepUpTestPolicies.Rule(StepUpArea.Signatures) };
			_policy = StepUpTestPolicies.Of(null);
			_policy.Setup(p => p.EvaluateAsync(It.IsAny<StepUpSubject>(), It.IsAny<StepUpArea>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _evaluation);
		}

		private HttpContext Http(bool script = false, bool withPolicy = true)
		{
			var user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };
			var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			users.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
			users.Setup(m => m.GetTwoFactorEnabledAsync(user)).ReturnsAsync(true);
			var services = new ServiceCollection()
				.AddSingleton(users.Object)
				.AddSingleton(Mock.Of<IMfaEvidenceService>())
				.AddSingleton<ICacheProvider>(_cache)
				.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider())
				.AddSingleton(StepUpTestPolicies.Capabilities(_capabilities, _policy.Object));
			if (withPolicy)
				services.AddSingleton(_policy.Object);

			var http = new DefaultHttpContext
			{
				RequestServices = services.BuildServiceProvider(),
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.NameIdentifier, UserId), new Claim(SessionClaimTypes.SessionId, SessionId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			http.Request.Method = "POST";
			http.Request.Scheme = "https";
			http.Request.Host = new HostString("app.resgrid.test");
			http.Request.Path = "/User/Protocols/Approve";
			http.Request.ContentType = "application/x-www-form-urlencoded";
			http.Request.ContentLength = 40;
			http.Request.Headers.Referer = "https://app.resgrid.test/User/Protocols/View/7";
			if (script)
				http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
			http.Features.Set<IFormFeature>(new FormFeature(new FormCollection(new Dictionary<string, StringValues> { ["id"] = "7", ["comment"] = "ok" })));
			return http;
		}

		private static async Task<(ActionExecutingContext Context, bool Passed)> Guard(HttpContext http, StepUpArea area = StepUpArea.Signatures)
		{
			var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>(),
				new Dictionary<string, object>(), controller: null);
			var passed = false;
			await new RequiresRecentTwoFactorAttribute { RequireForOperation = true, StepUpArea = area }
				.OnActionExecutionAsync(context, () => { passed = true; return Task.FromResult<ActionExecutedContext>(null); });
			return (context, passed);
		}

		[Test]
		public async Task An_optional_area_passes_without_a_prompt()
		{
			_evaluation = new StepUpEvaluation { Rule = StepUpTestPolicies.Rule(StepUpArea.Signatures, required: false), NotRequired = true };
			var (_, passed) = await Guard(Http());
			passed.Should().BeTrue();
		}

		[Test]
		public async Task An_allowed_password_reentry_passes_and_is_handed_to_the_signature_as_reauthentication()
		{
			var at = DateTime.UtcNow.AddMinutes(-1);
			_evaluation = new StepUpEvaluation
			{
				Rule = StepUpTestPolicies.Rule(StepUpArea.Signatures, methods: StepUpMethodClasses.Password), Satisfied = true,
				MethodClass = StepUpMethodClasses.Password, Method = MfaEvidenceMethod.Password, VerifiedOnUtc = at
			};
			var http = Http();

			var (_, passed) = await Guard(http);

			passed.Should().BeTrue();
			var proof = WebStepUpProof.Apply(SignatureProof.SessionOnly(UserId), http);
			proof.StepUpVerifiedOnUtc.Should().BeNull("a password is never offered as a second factor");
			proof.ReauthenticatedOnUtc.Should().Be(at);
			proof.ReauthenticationMethod.Should().Be((int)MfaEvidenceMethod.Password);
		}

		[Test]
		public async Task A_fresh_second_factor_is_handed_to_the_signature_with_its_method()
		{
			var at = DateTime.UtcNow.AddMinutes(-1);
			_evaluation = new StepUpEvaluation
			{
				Rule = StepUpTestPolicies.Rule(StepUpArea.Approvals), Satisfied = true, MethodClass = StepUpMethodClasses.Mfa, Method = MfaEvidenceMethod.Passkey, VerifiedOnUtc = at
			};
			var http = Http();
			(await Guard(http, StepUpArea.Approvals)).Passed.Should().BeTrue();
			var proof = WebStepUpProof.Apply(SignatureProof.SessionOnly(UserId), http);
			proof.StepUpVerifiedOnUtc.Should().Be(at);
			proof.StepUpMethod.Should().Be((int)MfaEvidenceMethod.Passkey);
			proof.ReauthenticatedOnUtc.Should().BeNull();
		}

		[Test]
		public async Task Without_proof_a_member_with_an_authenticator_is_sent_to_Verify2FA_with_the_password_page_offered_where_allowed()
		{
			_policy = StepUpTestPolicies.Of(a => StepUpTestPolicies.Rule(a, methods: StepUpMethodClasses.Password));
			_policy.Setup(p => p.EvaluateAsync(It.IsAny<StepUpSubject>(), It.IsAny<StepUpArea>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _evaluation);
			_capabilities = new StepUpCapabilities { TotpEnrolled = true, PasswordAvailable = true };

			var (context, passed) = await Guard(Http());

			passed.Should().BeFalse();
			var location = context.Result.Should().BeOfType<RedirectResult>().Subject.Url;
			location.Should().StartWith("/User/TwoFactor/Verify2FA?returnUrl=").And.Contain("stepUpArea=signatures").And.Contain("reauth=1");
			location.Should().Contain(Uri.EscapeDataString(StepUpFormReplay.ResumePath), "the submission is held and replayed after verifying");
		}

		[Test]
		public async Task A_member_with_only_the_password_goes_straight_to_the_password_page()
		{
			_policy = StepUpTestPolicies.Of(a => StepUpTestPolicies.Rule(a, methods: StepUpMethodClasses.Password));
			_policy.Setup(p => p.EvaluateAsync(It.IsAny<StepUpSubject>(), It.IsAny<StepUpArea>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _evaluation);
			_capabilities = new StepUpCapabilities { PasswordAvailable = true };

			var (context, _) = await Guard(Http());

			context.Result.Should().BeOfType<RedirectResult>().Which.Url.Should().StartWith("/User/AccountSecurity/Reauthenticate?returnUrl=")
				.And.Contain("stepUpArea=signatures");
		}

		[Test]
		public async Task A_member_with_nothing_allowed_usable_is_sent_to_enrollment()
		{
			_capabilities = new StepUpCapabilities { PasswordAvailable = true };
			var (context, passed) = await Guard(Http());
			passed.Should().BeFalse();
			context.Result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/User/TwoFactor/Enable2FA?enforced=1",
				"the password is not allowed here, so only a second factor would do");
		}

		[Test]
		public async Task A_script_call_gets_403_with_the_area_and_where_to_verify()
		{
			var http = Http(script: true);
			var (context, _) = await Guard(http);
			var json = context.Result.Should().BeOfType<JsonResult>().Subject;
			json.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			Newtonsoft.Json.JsonConvert.SerializeObject(json.Value).Should().Contain("\"error\":\"step_up_required\"").And.Contain("\"area\":\"signatures\"");
			http.Response.Headers[RequiresRecentTwoFactorAttribute.StepUpRedirectHeader].ToString().Should().Contain("/User/TwoFactor/Verify2FA");
		}

		[Test]
		public async Task Without_the_policy_service_the_guard_fails_closed()
		{
			var (context, passed) = await Guard(Http(withPolicy: false));
			passed.Should().BeFalse();
			context.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
		}

		[Test]
		public async Task A_policy_fault_fails_closed()
		{
			_policy.Setup(p => p.EvaluateAsync(It.IsAny<StepUpSubject>(), It.IsAny<StepUpArea>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
			var (context, passed) = await Guard(Http());
			passed.Should().BeFalse();
			context.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
		}
	}
}
