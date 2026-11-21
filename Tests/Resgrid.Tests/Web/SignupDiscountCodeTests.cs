using System;
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
using Resgrid.Model.Billing.Api;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Services;
using Resgrid.Tests.Security.Live;
using Resgrid.Web.Helpers;
using Resgrid.Web.Models.AccountViewModels;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using WebAccountController = Resgrid.Web.Controllers.AccountController;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// A discount code typed on the signup form used to be dropped unless the plan picker came next. Now, whenever no plan picker
	/// follows, it is checked and a valid one is held for checkout exactly like one entered on the Subscription page. No
	/// SignInManager is given, so every registration here is made but not signed in (the path that ends on the sign-in page).
	/// </summary>
	[TestFixture]
	public class SignupDiscountCodeTests
	{
		private const int NewDepartmentId = 42;
		private Mock<ISubscriptionsService> _subscriptions;
		private Mock<IAffiliateService> _affiliates;
		private Mock<IDepartmentsService> _departments;
		private Mock<IEventAggregator> _events;
		private Department _department;
		private string _billingUrl, _backendKey;
		private bool _requirePlanSelection;

		[SetUp]
		public void SetUp()
		{
			(_billingUrl, _backendKey, _requirePlanSelection) =
				(SystemBehaviorConfig.BillingApiBaseUrl, ApiConfig.BackendInternalApikey, SystemBehaviorConfig.RequirePlanSelectionDuringSignup);
			SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.test";
			ApiConfig.BackendInternalApikey = "test-key";
			SystemBehaviorConfig.RequirePlanSelectionDuringSignup = false;

			_department = new Department { DepartmentId = NewDepartmentId, Name = "Station 9" };
			_subscriptions = new Mock<ISubscriptionsService>();
			_affiliates = new Mock<IAffiliateService>();
			_events = new Mock<IEventAggregator>();
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.CreateDepartmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(_department);
		}

		[TearDown]
		public void TearDown() =>
			(SystemBehaviorConfig.BillingApiBaseUrl, ApiConfig.BackendInternalApikey, SystemBehaviorConfig.RequirePlanSelectionDuringSignup) =
				(_billingUrl, _backendKey, _requirePlanSelection);

		private WebAccountController Controller()
		{
			var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			users.Setup(u => u.CreateAsync(It.IsAny<IdentityUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);

			var http = new DefaultHttpContext();
			return new WebAccountController(userManager: users.Object, signInManager: null, departmentsService: _departments.Object,
				usersService: new Mock<IUsersService>().Object, emailService: new Mock<IEmailService>().Object, invitesService: null,
				userProfileService: new Mock<IUserProfileService>().Object, subscriptionsService: _subscriptions.Object, affiliateService: _affiliates.Object,
				eventAggregator: _events.Object, systemAuditsService: null, serviceProvider: null, departmentSsoService: null, secLocalizer: null,
				userSessionService: null, externalIdentityLinkService: null, passwordRecoveryService: null, limitsService: null,
				mfaEvidenceService: null, securityNotices: null, mfaActivity: null, loginTransactions: null, passkeys: null, approvals: null,
				mfaPolicy: null, twoFactorLocalizer: null, ssoBroker: null, ssoReturnTargets: null, dataProtection: null, adpStepUp: null, mfaState: null,
				userStore: null, recoveries: null, passkeyRows: null, challenges: null, approvalRows: null, sharedSessions: null, importActivation: null,
				cacheProvider: new InMemoryCacheProvider(), loginThrottle: null, formLimits: new PublicFormRateLimiter(new InMemoryCacheProvider()))
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
			};
		}

		private static RegisterViewModel Registration(string discountCode = null, string partnerCode = null) => new RegisterViewModel
		{
			FirstName = "Pat", LastName = "Doe", Email = "pat@example.test", Username = "pdoe" + Guid.NewGuid().ToString("N"), DepartmentName = "Station 9",
			DepartmentType = "Career Fire", Password = "Passw0rdOk", ConfirmPassword = "Passw0rdOk", DiscountCode = discountCode, AffiliateCode = partnerCode
		};

		private void BillingSays(DiscountCodeOutcomes outcome) =>
			_subscriptions.Setup(s => s.ApplyDiscountCodeToDepartmentAsync(NewDepartmentId, "SPRING25")).ReturnsAsync(new ApplyDiscountCodeData { Outcome = outcome });

		[Test]
		public async Task A_valid_signup_discount_code_is_held_for_checkout_when_no_plan_picker_follows()
		{
			BillingSays(DiscountCodeOutcomes.NoActiveSubscription);
			var controller = Controller();

			var result = await controller.Register(Registration(discountCode: " SPRING25 "), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
			_subscriptions.Verify(s => s.ApplyDiscountCodeToDepartmentAsync(NewDepartmentId, "SPRING25"), Times.Once);
			controller.TempData[DiscountCodeEntry.PendingCodeKey].Should().Be("SPRING25");
			controller.TempData[DiscountCodeEntry.MessageKey].Should().Be("DiscountPending");
			_events.Verify(e => e.SendMessage(It.Is<AuditEvent>(a => a.DepartmentId == NewDepartmentId && a.Successful && a.After.Contains("signup"))), Times.Once);
		}

		[Test]
		public async Task Signup_that_does_not_reach_the_plan_picker_still_holds_the_code_when_plan_selection_is_required()
		{
			// Sign-in did not finish, so the picker that would have taken the code never shows.
			SystemBehaviorConfig.RequirePlanSelectionDuringSignup = true;
			BillingSays(DiscountCodeOutcomes.NoActiveSubscription);
			var controller = Controller();

			await controller.Register(Registration(discountCode: "SPRING25"), CancellationToken.None);

			controller.TempData[DiscountCodeEntry.PendingCodeKey].Should().Be("SPRING25");
		}

		[Test]
		public async Task An_invalid_signup_discount_code_is_reported_and_not_held()
		{
			BillingSays(DiscountCodeOutcomes.InvalidCode);
			var controller = Controller();

			await controller.Register(Registration(discountCode: "SPRING25"), CancellationToken.None);

			controller.TempData.ContainsKey(DiscountCodeEntry.PendingCodeKey).Should().BeFalse();
			controller.TempData[DiscountCodeEntry.MessageKey].Should().Be("DiscountInvalid");
		}

		[Test]
		public async Task Signup_without_a_discount_code_never_asks_billing()
		{
			var controller = Controller();

			await controller.Register(Registration(), CancellationToken.None);

			_subscriptions.Verify(s => s.ApplyDiscountCodeToDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
			controller.TempData.ContainsKey(DiscountCodeEntry.MessageKey).Should().BeFalse();
		}

		[Test]
		public async Task A_billing_failure_never_undoes_the_registration()
		{
			_subscriptions.Setup(s => s.ApplyDiscountCodeToDepartmentAsync(It.IsAny<int>(), It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("billing down"));
			var controller = Controller();

			var result = await controller.Register(Registration(discountCode: "SPRING25"), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
			controller.TempData["LoginMfaMessage"].Should().Be("Your account was created. Sign in with the username and password you just chose.");
			controller.TempData.ContainsKey(DiscountCodeEntry.PendingCodeKey).Should().BeFalse();
		}

		[Test]
		public async Task A_partner_account_code_is_trimmed_before_it_is_looked_up()
		{
			_affiliates.Setup(a => a.GetActiveAffiliateByCodeAsync("ACME")).ReturnsAsync(new Affiliate { AffiliateCode = "Acme" });

			await Controller().Register(Registration(partnerCode: "  ACME "), CancellationToken.None);

			_department.AffiliateCode.Should().Be("Acme", "the partner's own spelling of the code is stored");
			_departments.Verify(d => d.SaveDepartmentAsync(_department, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_billing_reply_without_a_result_reads_as_unavailable()
		{
			var tempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>());
			_subscriptions.Setup(s => s.ApplyDiscountCodeToDepartmentAsync(1, "X")).ReturnsAsync((ApplyDiscountCodeData)null);

			(await DiscountCodeEntry.EnterAsync(_subscriptions.Object, tempData, 1, "X")).Should().Be(DiscountCodeOutcomes.Unavailable);
			tempData[DiscountCodeEntry.MessageKey].Should().Be("DiscountUnavailable");
		}

		[Test]
		public async Task An_overlong_code_is_refused_without_asking_billing()
		{
			var tempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>());

			(await DiscountCodeEntry.EnterAsync(_subscriptions.Object, tempData, 1, new string('X', DiscountCodeEntry.MaxLength + 1)))
				.Should().Be(DiscountCodeOutcomes.InvalidCode);
			_subscriptions.Verify(s => s.ApplyDiscountCodeToDepartmentAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}
	}
}
