using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Departments;
using Resgrid.Web.Areas.User.Models.Subscription;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Department → Dedicated Number: department admins only, every write re-checks that the user may modify the department, a number
	/// is only ever changed by the department that holds it, and the legacy Text Messaging provisioning no longer buys numbers.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class DedicatedNumberControllerTests
	{
		private const int DepartmentId = 12;
		private const string UserId = "admin";

		private Mock<IDepartmentPhoneNumbersService> _numbers;
		private Mock<IDedicatedNumberAccessService> _access;
		private Mock<Resgrid.Model.Services.IAuthorizationService> _authorization;
		private DefaultHttpContext _httpContext;

		[SetUp]
		public void SetUp()
		{
			_numbers = new Mock<IDepartmentPhoneNumbersService>();
			_numbers.Setup(n => n.GetAllForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentPhoneNumber>());
			_access = new Mock<IDedicatedNumberAccessService>();
			_access.Setup(a => a.GetAccessAsync(DepartmentId)).ReturnsAsync(new DedicatedNumberAccess
				{ HasBillingRelationship = true, IsOffered = true, HasActiveAddon = true, BillingProvider = "Stripe", AllowedCountries = new[] { "US", "CA" } });
			_authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			_authorization.Setup(a => a.CanUserModifyDepartmentAsync(UserId, DepartmentId)).ReturnsAsync(true);

			_httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _httpContext };
		}

		[TearDown]
		public void TearDown() => Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;

		private static IStringLocalizer<T> Strings<T>()
		{
			var localizer = new Mock<IStringLocalizer<T>>();
			localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			return localizer.Object;
		}

		private DedicatedNumberController Controller(string managingUserId = UserId, Mock<IDedicatedNumberBillingService> billing = null)
		{
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Fixture", TimeZone = "Eastern Standard Time", ManagingUserId = managingUserId });
			return new DedicatedNumberController(_numbers.Object, _access.Object, Mock.Of<IEnhancedAiAccessService>(), _authorization.Object, departments.Object,
				Strings<Resgrid.Localization.Areas.User.Department.DedicatedNumber>(), (billing ?? new Mock<IDedicatedNumberBillingService>()).Object)
			{
				ControllerContext = new ControllerContext { HttpContext = _httpContext },
				TempData = new TempDataDictionary(_httpContext, Mock.Of<ITempDataProvider>())
			};
		}

		private DedicatedNumberBillingController BillingController(Mock<IDedicatedNumberBillingService> billing)
		{
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.IntakeDedicatedNumber, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = true });
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = UserId });
			departments.Setup(d => d.GetDepartmentMemberAsync(UserId, DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = UserId });

			return new DedicatedNumberBillingController(billing.Object, _access.Object, _numbers.Object, departments.Object, _authorization.Object, flags.Object,
				Strings<Resgrid.Localization.Areas.User.Addons.Addons>(), Strings<Resgrid.Localization.Areas.User.Department.DedicatedNumber>())
			{
				ControllerContext = new ControllerContext { HttpContext = _httpContext },
				Url = Mock.Of<IUrlHelper>()
			};
		}

		[Test]
		public void Every_action_is_for_department_administrators()
		{
			var actions = typeof(DedicatedNumberController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
				.Where(m => typeof(Task<IActionResult>).IsAssignableFrom(m.ReturnType)).ToList();

			actions.Select(a => a.Name).Should().BeEquivalentTo(new[] { "Index", "Provision", "UpdateIntake", "Release", "Reactivate" });
			actions.Should().OnlyContain(m => m.GetCustomAttribute<AuthorizeAttribute>() != null &&
				m.GetCustomAttribute<AuthorizeAttribute>()!.Policy == ResgridResources.Department_Update);
			actions.Where(m => m.Name != "Index").Should().OnlyContain(m => m.GetCustomAttribute<HttpPostAttribute>() != null &&
				m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null);
		}

		[Test]
		public async Task A_user_who_may_not_modify_the_department_cannot_buy_release_or_change_a_number()
		{
			_authorization.Setup(a => a.CanUserModifyDepartmentAsync(UserId, DepartmentId)).ReturnsAsync(false);
			var controller = Controller();

			(await controller.Provision(Number, CancellationToken.None)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			(await controller.Release("any", true, CancellationToken.None)).Should().BeOfType<RedirectResult>();
			(await controller.UpdateIntake(true, true, CancellationToken.None)).Should().BeOfType<RedirectResult>();
			(await controller.Reactivate(CancellationToken.None)).Should().BeOfType<RedirectResult>();

			_numbers.Verify(n => n.ProvisionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_numbers.Verify(n => n.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		private const string Number = "+17755550100";

		[Test]
		public async Task Another_departments_number_cannot_be_released()
		{
			_numbers.Setup(n => n.GetAllForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentPhoneNumber>
				{ new DepartmentPhoneNumber { DepartmentPhoneNumberId = "mine", DepartmentId = DepartmentId, Status = (int)DepartmentPhoneNumberStatuses.Active } });

			(await Controller().Release("theirs", true, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
			_numbers.Verify(n => n.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			(await Controller().Release("mine", false, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>("an unconfirmed release changes nothing");
			_numbers.Verify(n => n.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			_numbers.Setup(n => n.ReleaseAsync("mine", It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var controller = Controller();
			(await controller.Release("mine", true, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();
			controller.TempData["DedicatedNumberMessage"].Should().Be("Released");
		}

		[Test]
		public async Task A_refused_purchase_is_shown_to_the_admin_as_its_reason()
		{
			_numbers.Setup(n => n.ProvisionAsync(DepartmentId, Number, UserId, It.IsAny<CancellationToken>()))
				.ThrowsAsync(new DedicatedNumberException(DedicatedNumberErrors.AddonRequired));
			var controller = Controller();

			(await controller.Provision(Number, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");

			controller.TempData["DedicatedNumberError"].Should().Be("Error.AddonRequired");
		}

		[Test]
		public async Task A_carrier_failure_is_reported_and_a_purchase_is_confirmed()
		{
			_numbers.Setup(n => n.ProvisionAsync(DepartmentId, Number, UserId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentPhoneNumber { PhoneNumber = Number, Status = (int)DepartmentPhoneNumberStatuses.Failed });
			var controller = Controller();
			await controller.Provision(Number, CancellationToken.None);
			controller.TempData["DedicatedNumberError"].Should().Be("ProvisionFailed");

			_numbers.Setup(n => n.ProvisionAsync(DepartmentId, Number, UserId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentPhoneNumber { PhoneNumber = Number, Status = (int)DepartmentPhoneNumberStatuses.Active });
			controller = Controller();
			await controller.Provision(Number, CancellationToken.None);
			controller.TempData["DedicatedNumberMessage"].Should().Be("Provisioned");
		}

		[Test]
		public async Task The_page_searches_only_while_the_department_can_buy_a_number()
		{
			_numbers.Setup(n => n.SearchAvailableAsync(DepartmentId, It.Is<DedicatedNumberSearch>(s => s.CountryCode == "US" && s.AreaCode == "775"), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<AvailablePhoneNumber> { new AvailablePhoneNumber { PhoneNumber = Number } });

			var view = (await Controller().Index("US", "775")).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<DedicatedNumberView>().Subject;
			view.CanSearch.Should().BeTrue();
			view.Results.Should().ContainSingle();

			_numbers.Setup(n => n.GetAllForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentPhoneNumber>
				{ new DepartmentPhoneNumber { DepartmentPhoneNumberId = "mine", DepartmentId = DepartmentId, PhoneNumber = Number, Status = (int)DepartmentPhoneNumberStatuses.Active } });
			view = (DedicatedNumberView)((ViewResult)await Controller().Index("US", "775")).Model;
			view.CanSearch.Should().BeFalse();
			view.Active.Should().NotBeNull();
			_numbers.Verify(n => n.SearchAvailableAsync(It.IsAny<int>(), It.IsAny<DedicatedNumberSearch>(), It.IsAny<CancellationToken>()), Times.Once);
			_numbers.Verify(n => n.SearchAvailableAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Every_search_filter_on_the_page_reaches_the_service()
		{
			DedicatedNumberSearch asked = null;
			_numbers.Setup(n => n.SearchAvailableAsync(DepartmentId, It.IsAny<DedicatedNumberSearch>(), It.IsAny<CancellationToken>()))
				.Callback((int d, DedicatedNumberSearch s, CancellationToken _) => asked = s)
				.ReturnsAsync(new List<AvailablePhoneNumber> { new AvailablePhoneNumber { PhoneNumber = Number, NumberType = DedicatedNumberTypes.Mobile } });

			var view = (DedicatedNumberView)((ViewResult)await Controller().Index("CA", "613", "55", "ON", "Ottawa", "K1A 0B1", true, 50)).Model;

			asked.CountryCode.Should().Be("CA");
			asked.AreaCode.Should().Be("613");
			asked.Contains.Should().Be("55");
			asked.Region.Should().Be("ON");
			asked.Locality.Should().Be("Ottawa");
			asked.PostalCode.Should().Be("K1A 0B1");
			asked.NearDepartment.Should().BeTrue();
			asked.DistanceMiles.Should().Be(50);
			view.Region.Should().Be("ON");
			view.City.Should().Be("Ottawa");
			view.Near.Should().BeTrue();
			view.Distance.Should().Be(50);
			view.NanpCountries.Should().Equal("US", "CA");

			// A refusal (here the department's location is unknown) is shown as its reason.
			_numbers.Setup(n => n.SearchAvailableAsync(DepartmentId, It.IsAny<DedicatedNumberSearch>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new DedicatedNumberException(DedicatedNumberErrors.DepartmentLocationUnknown));
			view = (DedicatedNumberView)((ViewResult)await Controller().Index("US", near: true)).Model;
			view.Error.Should().Be("Error.DepartmentLocationUnknown");
			view.Results.Should().BeEmpty();
		}

		[Test]
		public async Task A_number_whose_department_stopped_paying_shows_its_stop_and_release_dates_and_a_paying_one_shows_nothing_new()
		{
			var t0 = new DateTime(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc);
			_numbers.Setup(n => n.GetAllForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentPhoneNumber>
			{
				new DepartmentPhoneNumber
				{
					DepartmentPhoneNumberId = "mine", DepartmentId = DepartmentId, PhoneNumber = Number, Status = (int)DepartmentPhoneNumberStatuses.Active,
					AddonInactiveSince = t0, ReleaseWarningsSent = (int)DedicatedNumberReleaseWarnings.Stopped, LastReleaseWarningOn = t0
				}
			});

			var view = (DedicatedNumberView)((ViewResult)await Controller().Index()).Model;

			view.ActiveSchedule.Should().NotBeNull();
			view.ActiveSchedule.IntakeStopsOn.Should().Be(t0.AddHours(72));
			view.ActiveSchedule.ReleaseOn.Should().Be(t0.AddDays(Resgrid.Config.DedicatedNumberAddonConfig.ReleaseGraceDays));
			view.ActiveSchedule.WarningsSent.Should().Be(DedicatedNumberReleaseWarnings.Stopped);
			view.Department.Should().NotBeNull("dates show in the department's time zone");

			_numbers.Setup(n => n.GetAllForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentPhoneNumber>
				{ new DepartmentPhoneNumber { DepartmentPhoneNumberId = "mine", DepartmentId = DepartmentId, PhoneNumber = Number, Status = (int)DepartmentPhoneNumberStatuses.Active } });
			view = (DedicatedNumberView)((ViewResult)await Controller().Index()).Model;
			view.ActiveSchedule.Should().BeNull();
			view.LapsedSchedule.Should().BeNull();
		}

		[Test]
		public async Task Cancelling_the_renewal_tells_the_admins_with_the_paid_through_date_and_never_fails_the_cancellation()
		{
			var paidThrough = new DateTime(2026, 12, 20, 0, 0, 0, DateTimeKind.Utc);
			var billing = new Mock<IDedicatedNumberBillingService>();
			billing.Setup(b => b.CancelRenewalAsync(DepartmentId)).ReturnsAsync(true);
			billing.Setup(b => b.GetAsync(DepartmentId)).ReturnsAsync(new DedicatedNumberBillingStatus { Active = true, Cancelled = true, PaidThroughUtc = paidThrough });

			(await BillingController(billing).CancelRenewal()).Should().BeOfType<RedirectToActionResult>()
				.Which.RouteValues["result"].Should().Be(AddonBillingResults.RenewalCancelled);
			_numbers.Verify(n => n.NotifyCancellationScheduledAsync(DepartmentId, DedicatedNumberCancellations.AddonRenewal, paidThrough, It.IsAny<CancellationToken>()), Times.Once);

			// The notice failing leaves the cancellation reported as done.
			_numbers.Setup(n => n.NotifyCancellationScheduledAsync(It.IsAny<int>(), It.IsAny<DedicatedNumberCancellations>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("email down"));
			(await BillingController(billing).CancelRenewal()).Should().BeOfType<RedirectToActionResult>()
				.Which.RouteValues["result"].Should().Be(AddonBillingResults.RenewalCancelled);

			// A refused cancellation tells nobody.
			_numbers.Invocations.Clear();
			billing.Setup(b => b.CancelRenewalAsync(DepartmentId)).ReturnsAsync(false);
			(await BillingController(billing).CancelRenewal()).Should().BeOfType<RedirectToActionResult>()
				.Which.RouteValues["result"].Should().Be(AddonBillingResults.RenewalFailed);
			_numbers.Verify(n => n.NotifyCancellationScheduledAsync(It.IsAny<int>(), It.IsAny<DedicatedNumberCancellations>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task The_billing_page_explains_that_an_entity_plan_is_required()
		{
			_access.Setup(a => a.GetAccessAsync(DepartmentId)).ReturnsAsync(new DedicatedNumberAccess { IsOffered = true, AllowedCountries = Array.Empty<string>() });
			var billing = new Mock<IDedicatedNumberBillingService>();
			billing.Setup(b => b.GetAsync(DepartmentId)).ReturnsAsync(new DedicatedNumberBillingStatus { Provider = "Stripe", Currency = "USD", MonthlyAmount = 15m, CheckoutAvailable = false });

			var controller = BillingController(billing);

			var view = (await controller.Index()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<AddonBillingView>().Subject;

			view.Notices.Should().Contain("RequiresEntityPlan");
			view.CanBuy.Should().BeFalse();
			view.Title.Should().Be("DedicatedNumberTitle");
			view.Features.Should().HaveCount(AddonCatalog.Get(PlanAddonTypes.DedicatedNumber).FeatureCount);
			view.Links.Should().Contain(l => l.External && l.Url.EndsWith("/features/dedicated-number"));
		}

		private void Buyable() => _access.Setup(a => a.GetAccessAsync(DepartmentId)).ReturnsAsync(new DedicatedNumberAccess
			{ HasBillingRelationship = true, IsOffered = true, HasActiveAddon = false, BillingProvider = "Stripe", AllowedCountries = new[] { "US", "CA" } });

		private static Mock<IDedicatedNumberBillingService> Offer(bool checkoutAvailable = true)
		{
			var billing = new Mock<IDedicatedNumberBillingService>();
			billing.Setup(b => b.GetAsync(DepartmentId)).ReturnsAsync(new DedicatedNumberBillingStatus
				{ Provider = "Stripe", Currency = "USD", MonthlyAmount = 15m, CheckoutAvailable = checkoutAvailable, HasEntityPlan = true });
			return billing;
		}

		[Test]
		public async Task The_managing_member_can_buy_the_addon_on_the_number_page()
		{
			Buyable();
			var billing = Offer();

			var view = (await Controller(billing: billing).Index()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<DedicatedNumberView>().Subject;

			view.IsOwner.Should().BeTrue();
			view.CanBuyHere.Should().BeTrue();
			view.Billing.MonthlyAmount.Should().Be(15m);
			view.Billing.Currency.Should().Be("USD");
			view.CanSearch.Should().BeFalse("a number is chosen only once the add-on is held");

			// Checkout closed at the provider: no Buy button here (the page links to the billing page, which says why).
			(await Controller(billing: Offer(checkoutAvailable: false)).Index()).Should().BeOfType<ViewResult>().Which.Model.As<DedicatedNumberView>().CanBuyHere.Should().BeFalse();
		}

		[Test]
		public async Task Only_the_managing_member_is_offered_checkout_and_nobody_asks_billing_when_the_addon_cannot_be_bought()
		{
			Buyable();
			var billing = Offer();

			var other = (await Controller("someone-else", billing).Index()).Should().BeOfType<ViewResult>().Which.Model.As<DedicatedNumberView>();
			other.IsOwner.Should().BeFalse();
			other.CanBuyHere.Should().BeFalse();
			billing.Verify(b => b.GetAsync(It.IsAny<int>()), Times.Never);

			// Held already, no entity plan, or not offered: the page never asks the Billing API.
			foreach (var access in new[]
			{
				new DedicatedNumberAccess { HasBillingRelationship = true, IsOffered = true, HasActiveAddon = true, AllowedCountries = new[] { "US" } },
				new DedicatedNumberAccess { HasBillingRelationship = false, IsOffered = true, AllowedCountries = Array.Empty<string>() },
				new DedicatedNumberAccess { HasBillingRelationship = true, IsOffered = false, AllowedCountries = Array.Empty<string>() }
			})
			{
				_access.Setup(a => a.GetAccessAsync(DepartmentId)).ReturnsAsync(access);
				var view = (await Controller(billing: billing).Index()).Should().BeOfType<ViewResult>().Which.Model.As<DedicatedNumberView>();
				view.CanBuyHere.Should().BeFalse();
			}
			billing.Verify(b => b.GetAsync(It.IsAny<int>()), Times.Never);

			// A billing failure leaves the page working, with no inline checkout.
			Buyable();
			billing.Setup(b => b.GetAsync(DepartmentId)).ThrowsAsync(new InvalidOperationException("billing down"));
			var failed = (await Controller(billing: billing).Index()).Should().BeOfType<ViewResult>().Which.Model.As<DedicatedNumberView>();
			failed.IsOwner.Should().BeTrue();
			failed.CanBuyHere.Should().BeFalse();
		}

		[Test]
		public async Task A_checkout_that_came_back_paid_waits_for_activation_instead_of_offering_another()
		{
			Buyable();

			var waiting = (await Controller(billing: Offer()).Index(result: AddonBillingResults.Complete, attempt: 500)).Should().BeOfType<ViewResult>().Which.Model.As<DedicatedNumberView>();
			waiting.AwaitingActivation.Should().BeTrue();
			waiting.CanBuyHere.Should().BeFalse();
			waiting.CheckoutAttempt.Should().Be(100);

			var cancelled = (await Controller(billing: Offer()).Index(result: AddonBillingResults.Cancelled)).Should().BeOfType<ViewResult>().Which.Model.As<DedicatedNumberView>();
			cancelled.CheckoutResult.Should().Be(AddonBillingResults.Cancelled);
			cancelled.CanBuyHere.Should().BeTrue();

			(await Controller(billing: Offer()).Index(result: "<script>")).Should().BeOfType<ViewResult>().Which.Model.As<DedicatedNumberView>().CheckoutResult.Should().BeNull();

			// Once the payment activates the add-on, the same return shows it held.
			_access.Setup(a => a.GetAccessAsync(DepartmentId)).ReturnsAsync(new DedicatedNumberAccess { HasBillingRelationship = true, IsOffered = true, HasActiveAddon = true, AllowedCountries = new[] { "US" } });
			var active = (await Controller(billing: Offer()).Index(result: AddonBillingResults.Complete)).Should().BeOfType<ViewResult>().Which.Model.As<DedicatedNumberView>();
			active.AwaitingActivation.Should().BeFalse();
			active.CanSearch.Should().BeTrue();
		}

		[Test]
		public async Task Checkout_started_on_the_number_page_returns_there_and_the_billing_page_keeps_its_own_return()
		{
			var billing = new Mock<IDedicatedNumberBillingService>();
			billing.Setup(b => b.BeginCheckoutAsync(DepartmentId, It.IsAny<DedicatedNumberCheckoutReturns>()))
				.ReturnsAsync(new DedicatedNumberCheckout { Provider = "Stripe", Url = "https://checkout.stripe.com/c/pay/cs_test", TransactionId = "cs_test" });

			_httpContext.Request.ContentType = "application/x-www-form-urlencoded";
			_httpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["returnTo"] = DedicatedNumberBillingController.ReturnToNumberPage });
			(await BillingController(billing).Checkout()).Should().BeOfType<JsonResult>();
			billing.Verify(b => b.BeginCheckoutAsync(DepartmentId, DedicatedNumberCheckoutReturns.NumberPage), Times.Once);

			foreach (var form in new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>[] { new(), new() { ["returnTo"] = "https://evil.example" }, new() { ["returnTo"] = "Number" } })
			{
				_httpContext.Request.Form = new FormCollection(form);
				(await BillingController(billing).Checkout()).Should().BeOfType<JsonResult>();
			}
			billing.Verify(b => b.BeginCheckoutAsync(DepartmentId, DedicatedNumberCheckoutReturns.BillingPage), Times.Exactly(3));
		}

		[Test]
		public void Legacy_text_messaging_provisioning_sends_the_admin_to_the_dedicated_number_page_and_buys_nothing()
		{
			var numbersService = new Mock<INumbersService>();
			var limits = new Mock<ILimitsService>();
			limits.Setup(l => l.CanDepartmentProvisionNumberAsync(It.IsAny<int>())).ReturnsAsync(true);
			var controller = new DepartmentController(Mock.Of<IDepartmentsService>(), Mock.Of<IUsersService>(), Mock.Of<IActionLogsService>(), Mock.Of<IEmailService>(),
				Mock.Of<IDepartmentGroupsService>(), Mock.Of<IUserProfileService>(), Mock.Of<IDeleteService>(), Mock.Of<IInvitesService>(), _authorization.Object,
				Mock.Of<IAddressService>(), Mock.Of<ISubscriptionsService>(), limits.Object, Mock.Of<ICallsService>(), Mock.Of<IDepartmentSettingsService>(), Mock.Of<IUnitsService>(),
				Mock.Of<ICertificationService>(), numbersService.Object, Mock.Of<IScheduledTasksService>(), Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IEventAggregator>(), Mock.Of<ICustomStateService>(), Mock.Of<ICqrsProvider>(), Mock.Of<IPrinterProvider>(), Mock.Of<IQueueService>(),
				Mock.Of<IDocumentsService>(), Mock.Of<INotesService>(), Mock.Of<IContactsService>(), Mock.Of<ICheckInTimerService>(), Mock.Of<ISecurityPinService>(),
				Mock.Of<IRunCardsService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<IDepartmentProfileMediaService>(),
				Strings<Resgrid.Localization.Areas.User.Department.Department>(), Mock.Of<Resgrid.Model.AiDispatch.IAiDispatchEnrichmentService>(),
				Mock.Of<IProtectedReadService>(), Mock.Of<ICallNumberingService>(), Mock.Of<IDocumentNumberingService>(), Mock.Of<IOfflineMapPackService>(),
				Mock.Of<IStaticMapService>(), Mock.Of<IMapStyleService>(), Mock.Of<IRoutingService>(), Mock.Of<IDepartmentMapProvidersService>())
			{
				ControllerContext = new ControllerContext { HttpContext = _httpContext }
			};

			foreach (var result in new[] { controller.ProvisionNumber(Number, "US", CancellationToken.None), controller.ProvisionDefaultNumberAsync("US", "775", CancellationToken.None) })
			{
				var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
				redirect.ControllerName.Should().Be("DedicatedNumber");
				redirect.ActionName.Should().Be("Index");
			}

			numbersService.Verify(n => n.ProvisionNumberAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			numbersService.Verify(n => n.GetAvailableNumbers(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

			foreach (var name in new[] { "ProvisionNumber", "ProvisionDefaultNumberAsync" })
			{
				var method = typeof(DepartmentController).GetMethod(name);
				method.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
				method.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(ResgridResources.Department_Update);
			}
		}
	}
}
