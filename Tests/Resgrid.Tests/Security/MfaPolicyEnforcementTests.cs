using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Attributes;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Mfa;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 5a: department RequireMfa behind its gate, the shared method choice and preference, the
	/// Web step-up guard on server-side evidence, the API step-up endpoints, and the 5-minute guards on security changes.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class MfaPolicyEnforcementTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private const string SessionId = "session-9";

		private bool _gate;
		private int _window;
		private IHttpContextAccessor _previousAccessor;

		[SetUp]
		public void SetUp()
		{
			_gate = TwoFactorConfig.RequireMfaEnforcementEnabled;
			_window = TwoFactorConfig.SensitiveOperationWindowMinutes;
			_previousAccessor = ApiClaims._httpContextAccessor;
			TwoFactorConfig.SensitiveOperationWindowMinutes = 5;
		}

		[TearDown]
		public void TearDown()
		{
			TwoFactorConfig.RequireMfaEnforcementEnabled = _gate;
			TwoFactorConfig.SensitiveOperationWindowMinutes = _window;
			ApiClaims._httpContextAccessor = _previousAccessor;
		}

		private static Mock<UserManager<IdentityUser>> UserManager(IdentityUser user, bool enrolled = true)
		{
			var manager = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			manager.Setup(m => m.FindByIdAsync(user.Id)).ReturnsAsync(user);
			manager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
			manager.Setup(m => m.GetTwoFactorEnabledAsync(user)).ReturnsAsync(enrolled);
			manager.Setup(m => m.IsLockedOutAsync(user)).ReturnsAsync(false);
			return manager;
		}

		private static IdentityUser User() => new() { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };

		private static MfaEvidence Evidence(DateTime verifiedOn) => new()
		{
			UserId = UserId, SessionKey = "sid:" + SessionId, Kind = (int)MfaEvidenceKind.SecondFactor,
			Method = (int)MfaEvidenceMethod.Totp, VerifiedOnUtc = verifiedOn, AuthenticationGeneration = 4
		};

		// ---- Policy service ------------------------------------------------------------------------------------------

		private static MfaPolicyService Policy(DepartmentSecurityPolicy policy, InMemoryUserMfaStateRepository state = null)
		{
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(policy);
			return new MfaPolicyService(sso.Object, state ?? new InMemoryUserMfaStateRepository(), Mock.Of<IPasskeyFeatureGates>());
		}

		[Test]
		public async Task Require_mfa_is_enforced_on_the_new_paths_only_behind_its_gate()
		{
			var service = Policy(new DepartmentSecurityPolicy { DepartmentId = DepartmentId, RequireMfa = true });

			TwoFactorConfig.RequireMfaEnforcementEnabled = false;
			(await service.IsRequireMfaEnforcedAsync(DepartmentId)).Should().BeFalse();
			(await service.DepartmentRequiresMfaAsync(DepartmentId)).Should().BeTrue("the SSO exchange has always enforced it");

			TwoFactorConfig.RequireMfaEnforcementEnabled = true;
			(await service.IsRequireMfaEnforcedAsync(DepartmentId)).Should().BeTrue();
		}

		[Test]
		public async Task A_department_without_a_policy_row_or_the_flag_is_unaffected()
		{
			TwoFactorConfig.RequireMfaEnforcementEnabled = true;

			(await Policy(null).IsRequireMfaEnforcedAsync(DepartmentId)).Should().BeFalse();
			(await Policy(new DepartmentSecurityPolicy { RequireMfa = false }).IsRequireMfaEnforcedAsync(DepartmentId)).Should().BeFalse();
			(await Policy(null).IsRequireMfaEnforcedAsync(null)).Should().BeFalse();
		}

		[Test]
		public async Task A_policy_lookup_failure_is_thrown_never_read_as_not_required()
		{
			TwoFactorConfig.RequireMfaEnforcementEnabled = true;
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

			var check = () => new MfaPolicyService(sso.Object, new InMemoryUserMfaStateRepository(), Mock.Of<IPasskeyFeatureGates>()).IsRequireMfaEnforcedAsync(DepartmentId);

			await check.Should().ThrowAsync<TimeoutException>();
		}

		[Test]
		public async Task A_totp_user_is_offered_totp_and_an_unenrolled_user_nothing()
		{
			var enrolled = await Policy(null).GetMethodChoiceAsync(UserId, totpEnrolled: true, DepartmentId, MfaMethodScope.Login);
			enrolled.EnrolledMethods.Should().Equal(MfaMethodNames.Totp);
			enrolled.Preferred.Should().Be(MfaMethodNames.Totp);
			enrolled.CanVerify.Should().BeTrue();

			var unenrolled = await Policy(null).GetMethodChoiceAsync(UserId, totpEnrolled: false, DepartmentId, MfaMethodScope.Login);
			unenrolled.EnrolledMethods.Should().BeEmpty();
			unenrolled.AllowedMethods.Should().Equal(MfaMethodNames.Totp);
			unenrolled.CanVerify.Should().BeFalse();
		}

		// ---- Preference ----------------------------------------------------------------------------------------------

		private sealed class FixedClock : TimeProvider
		{
			public override DateTimeOffset GetUtcNow() => new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
		}

		[Test]
		public async Task Every_successful_second_factor_becomes_the_preference_and_nothing_else_does()
		{
			var state = new InMemoryUserMfaStateRepository();
			var service = new MfaEvidenceService(Mock.Of<IUserSessionMfaEvidenceRepository>(), state, Mock.Of<IUserPasskeyRepository>(),
				Mock.Of<IUserSessionsRepository>(), new InMemoryMfaActivityRepository(), new FixedClock());

			await service.RecordAsync(UserId, "sid:s", UserSessionClientApplication.Web, MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Password,
				MfaEvidencePurpose.Login, DateTime.UtcNow, 4);
			await service.RecordAsync(UserId, "sid:s", UserSessionClientApplication.Web, MfaEvidenceKind.Recovery, MfaEvidenceMethod.RecoveryCode,
				MfaEvidencePurpose.Login, DateTime.UtcNow, 4);
			state.Preferences.Should().BeEmpty("a password or a recovery code is not a second-factor preference");

			await service.RecordAsync(UserId, "sid:s", UserSessionClientApplication.Web, MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Totp,
				MfaEvidencePurpose.StepUp, DateTime.UtcNow, 4);
			state.Preferences[UserId].Should().Be((int)MfaEvidenceMethod.Totp);
		}

		[Test]
		public async Task A_preference_failure_never_fails_the_verification()
		{
			var state = new Mock<IUserMfaStateRepository>();
			state.Setup(s => s.SetPreferredMethodAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new TimeoutException());
			var evidence = new Mock<IUserSessionMfaEvidenceRepository>();
			var service = new MfaEvidenceService(evidence.Object, state.Object, Mock.Of<IUserPasskeyRepository>(), Mock.Of<IUserSessionsRepository>(),
				new InMemoryMfaActivityRepository(), new FixedClock());

			await service.RecordAsync(UserId, "sid:s", UserSessionClientApplication.Web, MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Totp,
				MfaEvidencePurpose.StepUp, DateTime.UtcNow, 4);

			evidence.Verify(e => e.InsertAsync(It.IsAny<MfaEvidence>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		// ---- Web step-up on server evidence --------------------------------------------------------------------------

		[Test]
		public void Evidence_a_few_seconds_ahead_is_now_and_further_ahead_is_nothing()
		{
			var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

			StepUpEvidence.Normalize(now.AddMinutes(-2), now).Should().Be(now.AddMinutes(-2));
			StepUpEvidence.Normalize(now.AddSeconds(20), now).Should().Be(now);
			StepUpEvidence.Normalize(now.AddMinutes(2), now).Should().BeNull();
			StepUpEvidence.Normalize(null, now).Should().BeNull();
		}

		[TestCase("GET", "/User/Security/SecurityPolicy", null, "/User/Security/SecurityPolicy")]
		[TestCase("POST", "/User/Security/SsoEdit", "https://app.resgrid.test/User/Security/SsoEdit?id=7", "/User/Security/SsoEdit?id=7")]
		[TestCase("POST", "/User/Security/SsoEdit", "https://evil.example/phish", "/User/Security/SsoEdit")]
		[TestCase("POST", "/User/Security/SsoEdit", null, "/User/Security/SsoEdit")]
		public void A_form_post_returns_to_the_local_page_that_submitted_it(string method, string path, string referer, string expected)
		{
			var context = new DefaultHttpContext();
			context.Request.Method = method;
			context.Request.Host = new HostString("app.resgrid.test");
			context.Request.Path = path;
			if (referer != null)
				context.Request.Headers.Referer = referer;

			RequiresRecentTwoFactorAttribute.ReturnUrlFor(context.Request).Should().Be(expected);
		}

		private static async Task<(ActionExecutingContext Context, bool Passed)> RunGuard(MfaEvidence latest, int windowMinutes = 5)
		{
			var user = User();
			var evidence = new Mock<IMfaEvidenceService>();
			evidence.Setup(e => e.GetLatestSecondFactorAsync(UserId, "sid:" + SessionId, 4, It.IsAny<CancellationToken>())).ReturnsAsync(latest);
			var services = new ServiceCollection()
				.AddSingleton(UserManager(user).Object)
				.AddSingleton(evidence.Object)
				.BuildServiceProvider();
			var http = new DefaultHttpContext
			{
				RequestServices = services,
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.NameIdentifier, UserId), new Claim(SessionClaimTypes.SessionId, SessionId)
				}, "test"))
			};
			http.Request.Method = "GET";
			http.Request.Path = "/User/Security/SecurityPolicy";

			var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
				new List<IFilterMetadata>(), new Dictionary<string, object>(), controller: null);
			var passed = false;
			await new RequiresRecentTwoFactorAttribute { RequireForOperation = true, VerificationWindowMinutes = windowMinutes }
				.OnActionExecutionAsync(context, () => { passed = true; return Task.FromResult<ActionExecutedContext>(null); });
			return (context, passed);
		}

		[Test]
		public async Task The_web_guard_accepts_recent_second_factor_evidence_for_this_session()
		{
			var (_, passed) = await RunGuard(Evidence(DateTime.UtcNow.AddMinutes(-2)));

			passed.Should().BeTrue();
		}

		[Test]
		public async Task The_web_guard_sends_the_user_to_verify_without_recent_evidence()
		{
			var (none, nonePassed) = await RunGuard(null);
			nonePassed.Should().BeFalse();
			none.Result.Should().BeOfType<RedirectToRouteResult>().Which.RouteValues["action"].Should().Be("Verify2FA");
			((RedirectToRouteResult)none.Result).RouteValues["scope"].Should().Be(nameof(MfaMethodScope.Login),
				"Verify2FA offers a passkey only where this action's scope accepts one");

			var (stale, stalePassed) = await RunGuard(Evidence(DateTime.UtcNow.AddMinutes(-6)));
			stalePassed.Should().BeFalse("a security change needs MFA within five minutes");
			stale.Result.Should().BeOfType<RedirectToRouteResult>();
		}

		// ---- API step-up ---------------------------------------------------------------------------------------------

		private static DefaultHttpContext ApiContext(bool withSession = true)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			if (withSession)
				http.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
				{
					SessionId = SessionId, ClientApplication = (int)UserSessionClientApplication.Responder, AuthenticationGeneration = 4
				};
			var accessor = new Mock<IHttpContextAccessor>();
			accessor.Setup(a => a.HttpContext).Returns(http);
			ApiClaims._httpContextAccessor = accessor.Object;
			return http;
		}

		private static (MfaController Controller, Mock<UserManager<IdentityUser>> Users, Mock<IMfaEvidenceService> Evidence) Mfa(
			bool codeValid = true, bool enrolled = true, bool withSession = true, int attempts = 1)
		{
			var user = User();
			var users = UserManager(user, enrolled);
			users.Setup(m => m.VerifyTwoFactorTokenAsync(user, It.IsAny<string>(), "123456")).ReturnsAsync(codeValid);
			var evidence = new Mock<IMfaEvidenceService>();
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(attempts);
			var controller = new MfaController(users.Object, Policy(null), evidence.Object, cache.Object, Mock.Of<ISystemAuditsService>(),
				Mock.Of<IPasskeyService>(), Mock.Of<IDepartmentSsoService>(), Mock.Of<ISsoBrokerService>(), Mock.Of<IMfaApprovalService>(), Mock.Of<IMfaActivityService>())
			{
				ControllerContext = new ControllerContext { HttpContext = ApiContext(withSession) }
			};
			return (controller, users, evidence);
		}

		private static VerifyStepUpInput Input(string code = "123456", string operation = MfaStepUpOperations.SecurityChange) =>
			new() { Operation = operation, Method = MfaMethodNames.Totp, Code = code };

		private static string ProblemType(IConvertToActionResult result) =>
			((ProblemDetails)((ObjectResult)result.Convert()).Value).Type;

		[Test]
		public async Task A_verified_step_up_becomes_evidence_on_this_session_and_returns_no_token()
		{
			var (controller, users, evidence) = Mfa();

			var result = await controller.VerifyStepUp(Input(), CancellationToken.None);

			var data = result.Value.Data;
			(DateTime.Parse(data.ExpiresAt).ToUniversalTime() - DateTime.Parse(data.VerifiedAt).ToUniversalTime()).Should().Be(TimeSpan.FromMinutes(5));
			evidence.Verify(e => e.RecordAsync(UserId, "sid:" + SessionId, UserSessionClientApplication.Responder, MfaEvidenceKind.SecondFactor,
				MfaEvidenceMethod.Totp, MfaEvidencePurpose.StepUp, It.IsAny<DateTime>(), 4, DepartmentId, null, It.IsAny<CancellationToken>()), Times.Once);
			users.Verify(m => m.ResetAccessFailedCountAsync(It.IsAny<IdentityUser>()), Times.Once);
		}

		[Test]
		public async Task A_wrong_code_counts_toward_the_account_lockout_and_records_nothing()
		{
			var (controller, users, evidence) = Mfa(codeValid: false);

			ProblemType(await controller.VerifyStepUp(Input(), CancellationToken.None)).Should().Be("invalid_totp");
			users.Verify(m => m.AccessFailedAsync(It.IsAny<IdentityUser>()), Times.Once);
			evidence.Verify(e => e.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(), It.IsAny<MfaEvidenceKind>(),
				It.IsAny<MfaEvidenceMethod>(), It.IsAny<MfaEvidencePurpose>(), It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<int?>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_locked_out_account_is_refused_before_the_code_is_checked()
		{
			var (controller, users, _) = Mfa();
			users.Setup(m => m.IsLockedOutAsync(It.IsAny<IdentityUser>())).ReturnsAsync(true);

			ProblemType(await controller.VerifyStepUp(Input(), CancellationToken.None)).Should().Be("too_many_attempts");
			users.Verify(m => m.VerifyTwoFactorTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Step_up_refusals_are_specific()
		{
			ProblemType(await Mfa(withSession: false).Controller.VerifyStepUp(Input(), CancellationToken.None)).Should().Be("session_required");
			ProblemType(await Mfa(enrolled: false).Controller.VerifyStepUp(Input(), CancellationToken.None)).Should().Be("mfa_enrollment_required");
			ProblemType(await Mfa().Controller.VerifyStepUp(Input(operation: "export_everything"), CancellationToken.None)).Should().Be("invalid_request");
			ProblemType(await Mfa(attempts: 6).Controller.VerifyStepUp(Input(), CancellationToken.None)).Should().Be("too_many_attempts");
			var passkey = Input();
			passkey.Method = MfaMethodNames.Passkey;
			ProblemType(await Mfa().Controller.VerifyStepUp(passkey, CancellationToken.None)).Should().Be("mfa_method_not_allowed");
		}

		[Test]
		public async Task Unrecorded_evidence_is_reported_rather_than_a_silent_success()
		{
			var (controller, _, evidence) = Mfa();
			evidence.Setup(e => e.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(), It.IsAny<MfaEvidenceKind>(),
				It.IsAny<MfaEvidenceMethod>(), It.IsAny<MfaEvidencePurpose>(), It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<int?>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

			ProblemType(await controller.VerifyStepUp(Input(), CancellationToken.None)).Should().Be("service_unavailable");
		}

		[Test]
		public async Task Step_up_options_list_the_usable_methods_and_the_default()
		{
			var enrolled = (await Mfa().Controller.StepUpOptions(MfaStepUpOperations.AdpManagement, CancellationToken.None)).Value.Data;
			enrolled.Methods.Should().Equal(MfaMethodNames.Totp);
			enrolled.Preferred.Should().Be(MfaMethodNames.Totp);
			enrolled.EnrollmentRequired.Should().BeFalse();
			enrolled.WindowMinutes.Should().Be(5);

			var unenrolled = (await Mfa(enrolled: false).Controller.StepUpOptions(MfaStepUpOperations.AdpManagement, CancellationToken.None)).Value.Data;
			unenrolled.Methods.Should().BeEmpty();
			unenrolled.EnrollmentRequired.Should().BeTrue();
		}

		[Test]
		public async Task Api_step_up_evidence_must_be_this_sessions_current_generation_and_recent()
		{
			var http = ApiContext();
			var evidence = new Mock<IMfaEvidenceService>();
			evidence.Setup(e => e.GetLatestSecondFactorAsync(UserId, "sid:" + SessionId, 4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(Evidence(DateTime.UtcNow.AddMinutes(-2)));

			var policy = Policy(null);
			(await ApiStepUpEvidence.HasRecentSecondFactorAsync(evidence.Object, policy, UserId, http, DepartmentId, MfaMethodScope.SecurityChange, TimeSpan.FromMinutes(5))).Should().BeTrue();
			(await ApiStepUpEvidence.HasRecentSecondFactorAsync(evidence.Object, policy, UserId, http, DepartmentId, MfaMethodScope.SecurityChange, TimeSpan.FromMinutes(1))).Should().BeFalse();
			(await ApiStepUpEvidence.HasRecentSecondFactorAsync(evidence.Object, policy, UserId, ApiContext(withSession: false), DepartmentId, MfaMethodScope.SecurityChange, TimeSpan.FromMinutes(5)))
				.Should().BeFalse("without a validated session there is no evidence");
		}

		// ---- 5-minute guard on API security changes ------------------------------------------------------------------

		private static (SsoAdminController Controller, Mock<IDepartmentSsoService> Sso) SsoAdmin(MfaEvidence latest)
		{
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = UserId });
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.SaveSecurityPolicyAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DepartmentSecurityPolicy p, string _, CancellationToken _) => p);
			var evidence = new Mock<IMfaEvidenceService>();
			evidence.Setup(e => e.GetLatestSecondFactorAsync(UserId, "sid:" + SessionId, 4, It.IsAny<CancellationToken>())).ReturnsAsync(latest);
			var controller = new SsoAdminController(sso.Object, departments.Object, Mock.Of<IPermissionsService>(),
				Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(), evidence.Object, Policy(null), Mock.Of<ISsoBrokerService>(),
				Mock.Of<ISystemAuditsService>(), Mock.Of<IPasskeyFeatureGates>())
			{
				ControllerContext = new ControllerContext { HttpContext = ApiContext() }
			};
			return (controller, sso);
		}

		[Test]
		public async Task A_security_policy_change_needs_step_up_within_five_minutes()
		{
			var (stale, staleSso) = SsoAdmin(Evidence(DateTime.UtcNow.AddMinutes(-6)));
			var refused = await stale.SaveSecurityPolicy(new Resgrid.Web.Services.Models.v4.Sso.SaveSecurityPolicyInput { RequireMfa = true }, CancellationToken.None);
			((ProblemDetails)((ObjectResult)((IConvertToActionResult)refused).Convert()).Value).Type.Should().Be("step_up_required");
			staleSso.Verify(s => s.SaveSecurityPolicyAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			var (fresh, freshSso) = SsoAdmin(Evidence(DateTime.UtcNow.AddMinutes(-1)));
			await fresh.SaveSecurityPolicy(new Resgrid.Web.Services.Models.v4.Sso.SaveSecurityPolicyInput { RequireMfa = true }, CancellationToken.None);
			freshSso.Verify(s => s.SaveSecurityPolicyAsync(It.Is<DepartmentSecurityPolicy>(p => p.RequireMfa), UserId, It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
