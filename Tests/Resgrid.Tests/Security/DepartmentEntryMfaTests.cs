using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Web department entry (passkey plan section 7.6 row 5): a session that has not completed an actual second factor
	/// cannot switch into a RequireMfa department; it is sent to verify first.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class DepartmentEntryMfaTests
	{
		private const string UserId = "user-1";
		private const int Target = 7;

		private IHttpContextAccessor _previousAccessor;
		private Mock<IMfaEvidenceService> _evidence;
		private Mock<IMfaPolicyService> _policy;
		private Mock<IDepartmentsService> _departments;
		private ProfileController _controller;
		private Mock<ISsoBrokerService> _broker;
		private Mock<ISsoReturnTargetRegistry> _returnTargets;
		private Mock<IDepartmentSsoService> _sso;
		private Mock<IExternalIdentityLinkService> _links;

		[SetUp]
		public void SetUp()
		{
			_previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.NameIdentifier, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, "3"), new Claim(SessionClaimTypes.SessionId, "session-9")
				}, "Test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var user = new IdentityUser { Id = UserId, AuthenticationGeneration = 4 };
			var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			users.Setup(m => m.FindByIdAsync(UserId)).ReturnsAsync(user);
			users.Setup(m => m.GetTwoFactorEnabledAsync(user)).ReturnsAsync(true);

			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.IsMemberOfDepartmentAsync(Target, UserId)).ReturnsAsync(true);
			var sso = _sso = new Mock<IDepartmentSsoService>();
			var links = _links = new Mock<IExternalIdentityLinkService>();
			_broker = new Mock<ISsoBrokerService>();
			_returnTargets = new Mock<ISsoReturnTargetRegistry>();
			_sessions = new Mock<IUserSessionService>();
			_sessions.Setup(s => s.GetActiveForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<UserSessionSummary>());
			_urls = new List<UrlActionContext>();
			(_apiGate, _webGate) = (Resgrid.Config.TwoFactorConfig.LoginMfaTransactionEnabled, Resgrid.Config.TwoFactorConfig.WebLoginMfaTransactionEnabled);
			links.Setup(l => l.IsLocalLoginAllowedAsync(UserId, Target, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_policy = new Mock<IMfaPolicyService>();
			_policy.Setup(p => p.IsRequireMfaEnforcedAsync(Target, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_evidence = new Mock<IMfaEvidenceService>();

			var url = new Mock<IUrlHelper>();
			url.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Callback((UrlActionContext c) => _urls.Add(c))
				.Returns((UrlActionContext c) => $"/User/{c.Controller}/{c.Action}");

			_controller = new ProfileController(
				departmentsService: _departments.Object, usersService: Mock.Of<IUsersService>(), authorizationService: null,
				userProfileService: null, scheduledTasksService: null, certificationService: null,
				customStateService: null, imageService: null, appOptionsAccessor: null,
				emailService: null, userManager: users.Object, signInManager: null,
				departmentSsoService: sso.Object, secLocalizer: null, deleteService: null,
				externalIdentityLinkService: links.Object, userSessionService: _sessions.Object, systemAuditsService: null,
				departmentGroupsService: null, departmentSettingsService: null, passwordRecoveryService: null,
				eventAggregator: null, protectedReadService: null, businessOperationsAccess: Mock.Of<IBusinessOperationsAccessService>(),
				limitsService: null, profileLocalizer: null, mfaPolicyService: _policy.Object, mfaEvidenceService: _evidence.Object, ssoBroker: _broker.Object,
				ssoReturnTargets: _returnTargets.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				Url = url.Object
			};
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;
			Resgrid.Config.TwoFactorConfig.LoginMfaTransactionEnabled = _apiGate;
			Resgrid.Config.TwoFactorConfig.WebLoginMfaTransactionEnabled = _webGate;
		}

		private Mock<IUserSessionService> _sessions;
		private List<UrlActionContext> _urls;
		private bool _apiGate, _webGate;

		private static object Value(UrlActionContext context, string name) => context.Values?.GetType().GetProperty(name)?.GetValue(context.Values);

		[Test]
		public async Task Switching_into_a_require_mfa_department_without_a_verified_second_factor_asks_for_one()
		{
			var result = await _controller.SetActiveDepartment(new Resgrid.Web.Areas.User.Models.Personnel.ChangeActiveDepartmentModel { DepartmentId = Target },
				CancellationToken.None);

			var refused = result.Should().BeOfType<ObjectResult>().Subject;
			refused.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			var body = JObject.FromObject(refused.Value);
			body.Value<string>("error").Should().Be("step_up_required");
			body.Value<string>("redirectUrl").Should().Be("/User/TwoFactor/Verify2FA");
			_departments.Verify(d => d.SetActiveDepartmentForUserAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IdentityUser>(),
				It.IsAny<CancellationToken>()), Times.Never, "nothing changes until the user verifies");
		}

		[Test]
		public async Task Verifying_to_enter_names_the_department_so_its_own_methods_are_offered()
		{
			await _controller.SetActiveDepartment(new Resgrid.Web.Areas.User.Models.Personnel.ChangeActiveDepartmentModel { DepartmentId = Target },
				CancellationToken.None);

			var verify = _urls.Single(c => c.Action == "Verify2FA");
			Value(verify, "entry").Should().Be(Target, "Verify2FA offers what the department being entered accepts");
		}

		[Test]
		public async Task A_department_that_requires_its_own_sso_is_entered_through_its_provider_where_the_web_offers_it()
		{
			_links.Setup(l => l.IsLocalLoginAllowedAsync(UserId, Target, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			var model = new Resgrid.Web.Areas.User.Models.Personnel.ChangeActiveDepartmentModel { DepartmentId = Target };

			_departments.Setup(d => d.GetDepartmentByIdAsync(Target, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Target, Code = "TGT" });
			Resgrid.Config.TwoFactorConfig.LoginMfaTransactionEnabled = true;
			Resgrid.Config.TwoFactorConfig.WebLoginMfaTransactionEnabled = true;
			(await _controller.SetActiveDepartment(model, CancellationToken.None)).Should().BeOfType<ForbidResult>(
				"without the broker and the Web's return address there is nowhere to send the user");

			_broker.SetupGet(b => b.IsEnabled).Returns(true);
			_returnTargets.Setup(r => r.IsAllowed(UserSessionClientApplication.Web, Resgrid.Web.Helpers.WebSsoRoundTrip.ReturnTarget)).Returns(true);
			Resgrid.Config.TwoFactorConfig.WebLoginMfaTransactionEnabled = false;
			(await _controller.SetActiveDepartment(model, CancellationToken.None)).Should().BeOfType<ForbidResult>(
				"Web SSO sign-in continues on the Web login transaction");
			Resgrid.Config.TwoFactorConfig.WebLoginMfaTransactionEnabled = true;

			var refused = (await _controller.SetActiveDepartment(model, CancellationToken.None)).Should().BeOfType<ObjectResult>().Subject;
			refused.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			var body = JObject.FromObject(refused.Value);
			body.Value<string>("error").Should().Be("sso_required");
			body.Value<string>("redirectUrl").Should().Be("/User/Account/SsoLogOn");
			Value(_urls.Last(c => c.Action == "SsoLogOn"), "departmentCode").Should().Be("TGT");

			(await _controller.SetDefaultDepartment(Target, CancellationToken.None)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/User/Account/SsoLogOn");
			_departments.Verify(d => d.SetActiveDepartmentForUserAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IdentityUser>(),
				It.IsAny<CancellationToken>()), Times.Never, "nothing changes until the provider signs the user in");
		}

		[Test]
		public async Task Making_it_the_default_department_is_sent_to_verify_too()
		{
			var result = await _controller.SetDefaultDepartment(Target, CancellationToken.None);

			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/User/TwoFactor/Verify2FA");
		}
	}
}
