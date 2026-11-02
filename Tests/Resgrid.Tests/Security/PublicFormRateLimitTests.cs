using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Security.Live;
using Resgrid.Web.Models;
using Resgrid.Web.Models.AccountViewModels;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using WebAccountController = Resgrid.Web.Controllers.AccountController;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Per-address caps on the signed-out account forms: registration (posts, and departments created per day), forgot password, the
	/// emailed reset link and the invitation. A refused form answers 429 with a message saying how long to wait, and does nothing else:
	/// no UserManager is given to the controller, so reaching account creation would throw.
	/// </summary>
	[TestFixture]
	public class PublicFormRateLimitTests
	{
		private const string Address = "203.0.113.7";
		private const int Dept = 7;
		private readonly Guid _code = Guid.NewGuid();
		private PublicFormRateLimiter _limits;
		private Mock<IPasswordRecoveryService> _recovery;
		private Mock<UserManager<IdentityUser>> _users;
		private string _billingUrl, _backendKey;

		[SetUp]
		public void SetUp()
		{
			(_billingUrl, _backendKey) = (SystemBehaviorConfig.BillingApiBaseUrl, ApiConfig.BackendInternalApikey);
			SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.test";
			ApiConfig.BackendInternalApikey = "test-key";
			_limits = new PublicFormRateLimiter(new InMemoryCacheProvider());
			_recovery = new Mock<IPasswordRecoveryService>();
			_users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
		}

		[TearDown]
		public void TearDown() => (SystemBehaviorConfig.BillingApiBaseUrl, ApiConfig.BackendInternalApikey) = (_billingUrl, _backendKey);

		private WebAccountController Controller(string address = Address)
		{
			var invites = new Mock<IInvitesService>();
			invites.Setup(i => i.GetInviteByCodeAsync(_code)).ReturnsAsync(new Invite { InviteId = 3, DepartmentId = Dept, Code = _code, EmailAddress = "invitee@example.test" });
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Station 12" });
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(x => x.GetEffectiveMinPasswordLengthAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(8);

			var http = new DefaultHttpContext();
			http.Connection.RemoteIpAddress = IPAddress.Parse(address);
			return new WebAccountController(userManager: _users.Object, signInManager: null, departmentsService: departments.Object,
				usersService: new Mock<IUsersService>().Object, emailService: null, invitesService: invites.Object, userProfileService: null,
				subscriptionsService: null, affiliateService: null, eventAggregator: null,
				systemAuditsService: null, serviceProvider: null, departmentSsoService: sso.Object, secLocalizer: null,
				userSessionService: null, externalIdentityLinkService: null, passwordRecoveryService: _recovery.Object, limitsService: new Mock<ILimitsService>().Object,
				mfaEvidenceService: null, securityNotices: null, mfaActivity: null, loginTransactions: null, passkeys: null, approvals: null,
				mfaPolicy: null, twoFactorLocalizer: null, ssoBroker: null, ssoReturnTargets: null, dataProtection: null, adpStepUp: null, mfaState: null,
				userStore: null, recoveries: null, passkeyRows: null, challenges: null, approvalRows: null, sharedSessions: null, importActivation: null,
				cacheProvider: new InMemoryCacheProvider(), loginThrottle: null, formLimits: _limits)
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
			};
		}

		private async Task Use(PublicFormLimit form, int times, string address = Address)
		{
			for (var i = 0; i < times; i++)
				await _limits.RecordAsync(form, address);
		}

		private static void Refused(WebAccountController controller, IActionResult result, string message)
		{
			result.Should().BeOfType<ViewResult>();
			controller.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
			controller.ModelState.Values.SelectMany(v => v.Errors).Should().ContainSingle().Which.ErrorMessage.Should().Contain(message);
		}

		// ---- The limiter ------------------------------------------------------------------------------------------------

		[Test]
		public async Task A_form_is_refused_after_its_cap_and_only_for_that_address_and_that_form()
		{
			for (var i = 0; i < SessionSecurityConfig.InviteIpLimitPerHour; i++)
				(await _limits.TryAcquireAsync(PublicFormLimit.Invite, Address)).Should().BeTrue($"use {i + 1} is within the cap");

			(await _limits.TryAcquireAsync(PublicFormLimit.Invite, Address)).Should().BeFalse();
			(await _limits.TryAcquireAsync(PublicFormLimit.Invite, "198.51.100.4")).Should().BeTrue("another network is untouched");
			(await _limits.TryAcquireAsync(PublicFormLimit.Register, Address)).Should().BeTrue("each form counts on its own");
		}

		[Test]
		public async Task Uses_past_the_cap_are_refused_even_when_none_of_them_has_seen_the_refusal_marker()
		{
			// A burst of concurrent posts all read "no marker" before any of them counts; the marker read is pinned empty here.
			var counter = 0L;
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.GetStringAsync(It.IsAny<string>())).ReturnsAsync((string)null);
			cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(() => Interlocked.Increment(ref counter));
			var limits = new PublicFormRateLimiter(cache.Object);

			var results = new List<bool>();
			for (var i = 0; i < SessionSecurityConfig.InviteIpLimitPerHour + 3; i++)
				results.Add(await limits.TryAcquireAsync(PublicFormLimit.Invite, Address));

			results.Count(r => r).Should().Be(SessionSecurityConfig.InviteIpLimitPerHour);
			results.Skip(SessionSecurityConfig.InviteIpLimitPerHour).Should().OnlyContain(r => !r);
		}

		[Test]
		public async Task A_cap_of_zero_turns_the_form_limit_off()
		{
			var saved = SessionSecurityConfig.FactorRecoveryIpLimitPerHour;
			try
			{
				SessionSecurityConfig.FactorRecoveryIpLimitPerHour = 0;
				for (var i = 0; i < 50; i++)
					(await _limits.TryAcquireAsync(PublicFormLimit.FactorRecovery, Address)).Should().BeTrue();
			}
			finally
			{
				SessionSecurityConfig.FactorRecoveryIpLimitPerHour = saved;
			}
		}

		[Test]
		public async Task A_cache_that_fails_never_refuses_a_form_and_addresses_are_stored_only_hashed()
		{
			var broken = new Mock<ICacheProvider>();
			broken.Setup(c => c.GetStringAsync(It.IsAny<string>())).ThrowsAsync(new TimeoutException());
			broken.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ThrowsAsync(new TimeoutException());
			(await new PublicFormRateLimiter(broken.Object).TryAcquireAsync(PublicFormLimit.Register, Address)).Should().BeTrue();

			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);
			await new PublicFormRateLimiter(cache.Object).TryAcquireAsync(PublicFormLimit.Register, Address);
			cache.Verify(c => c.IncrementAsync(It.Is<string>(k => k.Contains(Address)), It.IsAny<TimeSpan>()), Times.Never);
			cache.Verify(c => c.GetStringAsync(It.Is<string>(k => k.Contains(Address))), Times.Never);
		}

		// ---- Registration ----------------------------------------------------------------------------------------------

		private static RegisterViewModel Registration() => new RegisterViewModel
		{
			FirstName = "Pat", LastName = "Doe", Email = "pat@example.test", Username = "pdoe", DepartmentName = "Station 9",
			Password = "Passw0rdOk", ConfirmPassword = "Passw0rdOk"
		};

		[Test]
		public async Task Registration_posts_over_the_hourly_cap_are_refused_whatever_they_contain()
		{
			await Use(PublicFormLimit.Register, SessionSecurityConfig.RegisterIpLimitPerHour);
			var controller = Controller();
			controller.ModelState.AddModelError("GoogleReCaptchaResponse", "Please complete the check");

			Refused(controller, await controller.Register(Registration(), CancellationToken.None), "Too many registration attempts have come from your network");
			controller.ModelState.Values.SelectMany(v => v.Errors).Single().ErrorMessage.Should().Contain("wait up to an hour");
		}

		[Test]
		public async Task New_departments_over_the_daily_cap_are_refused_before_any_account_is_made()
		{
			await Use(PublicFormLimit.RegisterCreated, SessionSecurityConfig.RegisterIpAccountsPerDay);
			var controller = Controller();

			Refused(controller, await controller.Register(Registration(), CancellationToken.None), "Too many new departments have been registered from your network today");
			_users.Verify(u => u.CreateAsync(It.IsAny<IdentityUser>(), It.IsAny<string>()), Times.Never);
		}

		// ---- Forgot password -------------------------------------------------------------------------------------------

		[Test]
		public async Task A_forgot_password_request_over_the_address_limit_says_so_instead_of_a_silent_confirmation()
		{
			_recovery.Setup(r => r.IssueAsync(null, "nobody@example.test", Address, 0, null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PasswordRecoveryIssueResult { RateLimited = true, IpRateLimited = true });
			var controller = Controller();

			Refused(controller, await controller.ForgotPassword(new ForgotPasswordViewModel { Email = "nobody@example.test" }, CancellationToken.None),
				"Too many password reset requests have come from your network");
		}

		[Test]
		public async Task A_forgot_password_request_over_only_the_emails_limit_still_gets_the_usual_confirmation()
		{
			// Saying so would tell anyone that someone recently asked about that email.
			_recovery.Setup(r => r.IssueAsync(null, "nobody@example.test", Address, 0, null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PasswordRecoveryIssueResult { RateLimited = true });

			(await Controller().ForgotPassword(new ForgotPasswordViewModel { Email = "nobody@example.test" }, CancellationToken.None))
				.Should().BeOfType<ViewResult>().Which.ViewName.Should().Be("ForgotPasswordConfirmation");
		}

		[Test]
		public async Task Password_recovery_reports_its_address_limit_apart_from_the_emails()
		{
			var service = new PasswordRecoveryService(new InMemoryCacheProvider());
			for (var i = 0; i < SessionSecurityConfig.PublicResetIpLimitPerHour; i++)
				(await service.IssueAsync(null, $"user{i}@example.test", Address, 0, null)).RateLimited.Should().BeFalse();

			var overAddress = await service.IssueAsync(null, "another@example.test", Address, 0, null);
			overAddress.IpRateLimited.Should().BeTrue();

			for (var i = 0; i < SessionSecurityConfig.PublicResetAccountLimitPerHour; i++)
				await service.IssueAsync(null, "same@example.test", "198.51.100." + i, 0, null);
			var overEmail = await service.IssueAsync(null, "same@example.test", "198.51.100.200", 0, null);
			overEmail.RateLimited.Should().BeTrue();
			overEmail.IpRateLimited.Should().BeFalse();
		}

		// ---- The emailed reset link ------------------------------------------------------------------------------------

		[Test]
		public async Task Reset_link_posts_over_the_hourly_cap_show_the_wait_instead_of_the_form()
		{
			await Use(PublicFormLimit.PasswordReset, SessionSecurityConfig.PasswordResetIpLimitPerHour);
			var controller = Controller();

			var result = await controller.BeginPasswordReset(new string('a', 43), CancellationToken.None);

			var model = result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<ResetPasswordViewModel>().Subject;
			model.RateLimited.Should().BeTrue();
			model.RateLimitedMessage.Should().Contain("open the link from your email again");
			controller.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
			_recovery.Verify(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never, "no grant is looked up");
		}

		// ---- The invitation ------------------------------------------------------------------------------------------------

		[Test]
		public async Task Codes_that_do_not_exist_count_and_then_even_a_real_invitation_is_not_looked_up()
		{
			for (var i = 0; i < SessionSecurityConfig.InviteIpLimitPerHour; i++)
				(await Controller().CompleteInvite(Guid.NewGuid().ToString())).Should().BeOfType<RedirectToActionResult>()
					.Which.ActionName.Should().Be("MissingInvite");

			var controller = Controller();
			var result = await controller.CompleteInvite(_code.ToString());

			result.Should().BeOfType<ViewResult>().Which.ViewName.Should().Be("MissingInvite");
			controller.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
			((string)controller.ViewData["LimitMessage"]).Should().Contain("Too many invitation attempts");
			(await Controller("198.51.100.4").CompleteInvite(_code.ToString())).Should().BeOfType<ViewResult>()
				.Which.Model.Should().BeOfType<CompleteInviteModel>("another network opens the invitation as usual");
		}

		[Test]
		public async Task Invitation_posts_over_the_hourly_cap_are_refused()
		{
			await Use(PublicFormLimit.Invite, SessionSecurityConfig.InviteIpLimitPerHour);
			var controller = Controller();

			var result = await controller.CompleteInvite(new CompleteInviteModel { Code = _code.ToString(), UserName = "pinvitee", Password = "Passw0rdOk" },
				CancellationToken.None);

			result.Should().BeOfType<ViewResult>().Which.ViewName.Should().Be("MissingInvite");
			controller.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
		}
	}
}
