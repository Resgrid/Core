using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Models;
using Resgrid.Web.Models.AccountViewModels;
using WebAccountController = Resgrid.Web.Controllers.AccountController;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// A second post of the registration or invitation form (a double click, a resend) while the first is under way, or just after it
	/// created the account, is answered as a repeat: never a second account, and never the "username is already taken" or spent
	/// reCAPTCHA error that read as if the first had failed. No UserManager is given, so reaching account creation would throw.
	/// </summary>
	[TestFixture]
	public class AccountSubmissionClaimTests
	{
		private const int Dept = 7;
		private readonly Guid _code = Guid.NewGuid();
		private Mock<ICacheProvider> _cache;
		private string _billingUrl, _backendKey;

		[SetUp]
		public void SetUp()
		{
			(_billingUrl, _backendKey) = (SystemBehaviorConfig.BillingApiBaseUrl, ApiConfig.BackendInternalApikey);
			SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.test";
			ApiConfig.BackendInternalApikey = "test-key";
			_cache = new Mock<ICacheProvider>();
		}

		[TearDown]
		public void TearDown() => (SystemBehaviorConfig.BillingApiBaseUrl, ApiConfig.BackendInternalApikey) = (_billingUrl, _backendKey);

		private WebAccountController Controller()
		{
			var invites = new Mock<IInvitesService>();
			invites.Setup(i => i.GetInviteByCodeAsync(_code)).ReturnsAsync(new Invite { InviteId = 3, DepartmentId = Dept, Code = _code, EmailAddress = "invitee@example.test" });
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Station 12" });
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(x => x.GetEffectiveMinPasswordLengthAsync(Dept, It.IsAny<CancellationToken>())).ReturnsAsync(8);

			var http = new DefaultHttpContext();
			return new WebAccountController(userManager: null, signInManager: null, departmentsService: departments.Object,
				usersService: new Mock<IUsersService>().Object, emailService: null, invitesService: invites.Object, userProfileService: null,
				subscriptionsService: null, affiliateService: null, eventAggregator: null,
				systemAuditsService: null, serviceProvider: null, departmentSsoService: sso.Object, secLocalizer: null,
				userSessionService: null, externalIdentityLinkService: null, passwordRecoveryService: null, limitsService: new Mock<ILimitsService>().Object,
				mfaEvidenceService: null, securityNotices: null, mfaActivity: null, loginTransactions: null, passkeys: null, approvals: null,
				mfaPolicy: null, twoFactorLocalizer: null, ssoBroker: null, ssoReturnTargets: null, dataProtection: null, adpStepUp: null, mfaState: null,
				userStore: null, recoveries: null, passkeyRows: null, challenges: null, approvalRows: null, sharedSessions: null, importActivation: null,
				loginThrottle: null, formLimits: new Resgrid.Services.PublicFormRateLimiter(new Resgrid.Tests.Security.Live.InMemoryCacheProvider()), cacheProvider: _cache.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
			};
		}

		private void HeldBy(string holder) =>
			_cache.Setup(c => c.GetOrAddStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(holder);

		private static RegisterViewModel Registration() => new RegisterViewModel
		{
			FirstName = "Pat", LastName = "Doe", Email = "pat@example.test", Username = "pdoe", DepartmentName = "Station 9",
			DepartmentType = "Fire", Password = "Passw0rdOk", ConfirmPassword = "Passw0rdOk"
		};

		[Test]
		public async Task A_registration_sent_again_while_the_first_is_processing_says_so_instead_of_failing()
		{
			HeldBy("another-post");
			var controller = Controller();
			// The repeat's own reCAPTCHA token was already spent by the first post.
			controller.ModelState.AddModelError("GoogleReCaptchaResponse", "Please complete the check");

			var result = await controller.Register(Registration(), CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			var errors = controller.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
			errors.Should().ContainSingle().Which.Should().Contain("sent more than once");
		}

		[Test]
		public async Task A_registration_sent_again_after_the_first_created_the_account_goes_to_sign_in()
		{
			HeldBy("created");
			var controller = Controller();

			var result = await controller.Register(Registration(), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
			controller.TempData["LoginMfaMessage"].Should().Be("Your account was created. Sign in with the username and password you just chose.");
		}

		[Test]
		public async Task A_registration_that_fails_validation_gives_its_claim_back_so_the_corrected_form_can_be_sent()
		{
			HeldBy(null);
			var controller = Controller();
			controller.ModelState.AddModelError("Email", "Invalid");

			(await controller.Register(Registration(), CancellationToken.None)).Should().BeOfType<ViewResult>();

			_cache.Verify(c => c.RemoveAsync(It.Is<string>(k => k.StartsWith("AccountSubmission_Register_"))), Times.Once);
			_cache.Verify(c => c.GetOrAddStringAsync(It.Is<string>(k => !k.Contains("pdoe", StringComparison.OrdinalIgnoreCase)),
				It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Once, "the key is a hash, not the username");
		}

		[Test]
		public async Task An_invitation_sent_again_while_the_first_is_processing_says_so()
		{
			HeldBy("another-post");
			var controller = Controller();
			var model = new CompleteInviteModel { Code = _code.ToString(), FirstName = "Pat", LastName = "Invitee", UserName = "pinvitee", Password = "Passw0rdOk", ConfirmPassword = "Passw0rdOk" };

			var result = await controller.CompleteInvite(model, CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			controller.ModelState.Values.SelectMany(v => v.Errors).Should().ContainSingle()
				.Which.ErrorMessage.Should().Contain("sent more than once");
		}

		[Test]
		public async Task An_invitation_sent_again_after_the_first_completed_it_shows_the_completed_page()
		{
			HeldBy("created");

			(await Controller().CompleteInvite(new CompleteInviteModel { Code = _code.ToString() }, CancellationToken.None))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("CompletedInvite");
		}
	}
}
