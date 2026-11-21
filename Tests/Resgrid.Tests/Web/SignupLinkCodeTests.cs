using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Web.Models.AccountViewModels;
using WebAccountController = Resgrid.Web.Controllers.AccountController;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// A signup link can carry a partner/account code (from an external partner or a Resgrid account executive) and a discount code;
	/// both prefill the registration form and stay editable there. Links made before the rename used affiliateCode or aid.
	/// </summary>
	[TestFixture]
	public class SignupLinkCodeTests
	{
		private string _billingUrl, _backendKey;

		[SetUp]
		public void SetUp()
		{
			(_billingUrl, _backendKey) = (SystemBehaviorConfig.BillingApiBaseUrl, ApiConfig.BackendInternalApikey);
			SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.test";
			ApiConfig.BackendInternalApikey = "test-key";
		}

		[TearDown]
		public void TearDown() => (SystemBehaviorConfig.BillingApiBaseUrl, ApiConfig.BackendInternalApikey) = (_billingUrl, _backendKey);

		private static WebAccountController Controller() =>
			new WebAccountController(userManager: null, signInManager: null, departmentsService: null,
				usersService: null, emailService: null, invitesService: null, userProfileService: null,
				subscriptionsService: null, affiliateService: null, eventAggregator: null,
				systemAuditsService: null, serviceProvider: null, departmentSsoService: null, secLocalizer: null,
				userSessionService: null, externalIdentityLinkService: null, passwordRecoveryService: null, limitsService: null,
				mfaEvidenceService: null, securityNotices: null, mfaActivity: null, loginTransactions: null, passkeys: null, approvals: null,
				mfaPolicy: null, twoFactorLocalizer: null, ssoBroker: null, ssoReturnTargets: null, dataProtection: null, adpStepUp: null, mfaState: null,
				userStore: null, recoveries: null, passkeyRows: null, challenges: null, approvalRows: null, sharedSessions: null, importActivation: null,
				loginThrottle: null, formLimits: null, cacheProvider: null)
			{
				ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
			};

		private static RegisterViewModel Form(IActionResult result) =>
			result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<RegisterViewModel>().Subject;

		[Test]
		public void A_signup_link_prefills_both_the_partner_account_code_and_the_discount_code()
		{
			var form = Form(Controller().Register(partnerCode: "AE-SMITH", discountCode: "SPRING25"));

			form.AffiliateCode.Should().Be("AE-SMITH");
			form.DiscountCode.Should().Be("SPRING25");
		}

		[TestCase("accountCode")]
		[TestCase("affiliateCode")]
		[TestCase("aid")]
		public void Every_accepted_partner_account_parameter_name_prefills_the_code(string parameter)
		{
			var controller = Controller();
			var result = parameter switch
			{
				"accountCode" => controller.Register(accountCode: "ACME"),
				"affiliateCode" => controller.Register(affiliateCode: "ACME"),
				_ => controller.Register(aid: "ACME")
			};

			Form(result).AffiliateCode.Should().Be("ACME");
		}

		[Test]
		public void The_short_discount_parameter_prefills_the_discount_code()
		{
			Form(Controller().Register(discount: "SPRING25")).DiscountCode.Should().Be("SPRING25");
		}

		[Test]
		public void The_current_parameter_name_wins_over_a_legacy_one()
		{
			var form = Form(Controller().Register(partnerCode: "NEW", affiliateCode: "OLD", aid: "OLDER", discountCode: "FULL", discount: "SHORT"));

			form.AffiliateCode.Should().Be("NEW");
			form.DiscountCode.Should().Be("FULL");
		}

		[Test]
		public void A_blank_parameter_falls_through_to_the_next_name()
		{
			Form(Controller().Register(partnerCode: "  ", affiliateCode: "LEGACY")).AffiliateCode.Should().Be("LEGACY");
		}

		[Test]
		public void A_link_without_codes_leaves_both_fields_empty()
		{
			var form = Form(Controller().Register());

			form.AffiliateCode.Should().BeNull();
			form.DiscountCode.Should().BeNull();
		}

		[Test]
		public void Codes_are_trimmed_and_an_overlong_value_is_dropped_rather_than_cut()
		{
			WebAccountController.SignupLinkCode("  ACME  ").Should().Be("ACME");
			WebAccountController.SignupLinkCode(new string('X', WebAccountController.SignupLinkCodeMaxLength)).Should().HaveLength(WebAccountController.SignupLinkCodeMaxLength);
			WebAccountController.SignupLinkCode(new string('X', WebAccountController.SignupLinkCodeMaxLength + 1), "FALLBACK").Should().BeNull(
				"the first supplied name is the one the link meant, so a mangled value is not swapped for another");
			WebAccountController.SignupLinkCode(null, "", " ").Should().BeNull();
			WebAccountController.SignupLinkCode(null).Should().BeNull();
		}

		[Test]
		public void Without_billing_the_signup_page_is_not_offered_and_the_link_goes_to_sign_in()
		{
			SystemBehaviorConfig.BillingApiBaseUrl = null;

			Controller().Register(partnerCode: "ACME").Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LogOn");
		}
	}
}
