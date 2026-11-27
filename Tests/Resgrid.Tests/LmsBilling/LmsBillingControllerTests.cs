using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Subscription;

namespace Resgrid.Tests.LmsBilling
{
	/// <summary>
	/// The LMS card on the shared add-on billing page (§17.3 billing, R-L10): offered only with the operator toggle and sold only
	/// with a paid base plan; a pack quantity with the price shown before any change; adding packs goes to the Billing API at once,
	/// removing them is scheduled and never below the LMS-enabled members; only the managing member may change anything.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class LmsBillingControllerTests
	{
		private const int DepartmentId = 81;
		private const string OwnerId = "owner";

		private Mock<ILmsBillingService> _billing;
		private Mock<ILmsSeatService> _seats;
		private Mock<IEventAggregator> _events;
		private Mock<IFeatureToggleService> _flags;
		private LmsBillingStatus _status;

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private static IStringLocalizer<T> Strings<T>()
		{
			var localizer = new Mock<IStringLocalizer<T>>();
			localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			return localizer.Object;
		}

		private LmsBillingController Build(string userId, bool offered, LmsBillingStatus status, int enabledSeats = 0, bool canManageSubscription = false)
		{
			_status = status;
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = OwnerId });
			departments.Setup(x => x.GetDepartmentMemberAsync(userId, DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = userId });

			var authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			authorization.Setup(x => x.CanUserManageSubscriptionAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync(canManageSubscription);

			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(x => x.EvaluateFreshAsync(FeatureFlagKeys.TrainingLms, DepartmentId))
				.ReturnsAsync(new FeatureFlagEvaluation { Key = FeatureFlagKeys.TrainingLms, IsEnabled = offered });

			_billing = new Mock<ILmsBillingService>();
			_billing.Setup(x => x.GetAsync(DepartmentId)).ReturnsAsync(() => _status);
			_seats = new Mock<ILmsSeatService>();
			var minimum = Math.Max(1, (int)Math.Ceiling(enabledSeats / 25d));
			_seats.Setup(x => x.GetMinimumPacksAsync(DepartmentId)).ReturnsAsync(minimum);
			_seats.Setup(x => x.GetSummaryAsync(DepartmentId)).ReturnsAsync(new LmsSeatSummary
			{
				State = status?.Active == true ? LmsSubscriptionStates.Active : LmsSubscriptionStates.None,
				PurchasedPacks = status?.Packs ?? 0, PackSize = 25, EnabledSeats = enabledSeats, MinimumPacks = minimum
			});
			_events = new Mock<IEventAggregator>();

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, userId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			return new LmsBillingController(_billing.Object, _seats.Object, _events.Object, departments.Object, authorization.Object, _flags.Object,
				Strings<Resgrid.Localization.Areas.User.Addons.Addons>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext },
				Url = Mock.Of<IUrlHelper>()
			};
		}

		private static LmsBillingStatus NotHeld(bool hasPlan = true) => new LmsBillingStatus
			{ Provider = "Stripe", Currency = "USD", CheckoutAvailable = true, HasEntityPlan = hasPlan };

		private static LmsBillingStatus Held(int packs, int? scheduled = null) => new LmsBillingStatus
		{
			Provider = "Stripe", Currency = "USD", CheckoutAvailable = true, HasEntityPlan = true, Active = true, CanCancel = true, Packs = packs,
			ScheduledPacks = scheduled, PeriodStartUtc = DateTime.UtcNow.AddDays(-15), PaidThroughUtc = DateTime.UtcNow.AddDays(15), MonthlyAmount = 79m * packs
		};

		private static AddonBillingView ViewOf(IActionResult result) =>
			result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<AddonBillingView>().Subject;

		[Test]
		public async Task Without_the_operator_toggle_the_lms_is_not_offered()
		{
			var controller = Build(OwnerId, offered: false, NotHeld());

			(await controller.Index()).Should().BeOfType<RedirectToActionResult>().Which.ControllerName.Should().Be("Subscription");
		}

		[Test]
		public async Task Without_a_paid_base_plan_the_lms_cannot_be_bought()
		{
			var controller = Build(OwnerId, offered: true, NotHeld(hasPlan: false));

			var view = ViewOf(await controller.Index());

			view.CanBuy.Should().BeFalse("the LMS is sold only with a paid base plan (§25.4a item 9)");
			view.Notices.Should().Contain("LmsRequiresPaidPlan");

			controller.ControllerContext.HttpContext.Request.ContentType = "application/x-www-form-urlencoded";
			controller.ControllerContext.HttpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["packs"] = "2" });
			(await controller.Checkout()).Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
			_billing.Verify(x => x.BeginCheckoutAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_first_purchase_proposes_the_packs_that_seat_the_enabled_members_and_checks_out_that_quantity()
		{
			var controller = Build(OwnerId, offered: true, NotHeld(), enabledSeats: 30);

			var view = ViewOf(await controller.Index());

			view.CanBuy.Should().BeTrue();
			view.Packs.Should().NotBeNull();
			view.Packs.Packs.Should().Be(0);
			view.Packs.ProposedPacks.Should().Be(2);

			_billing.Setup(x => x.BeginCheckoutAsync(DepartmentId, 2)).ReturnsAsync(new LmsBillingCheckout { Provider = "Stripe", Url = "https://checkout.stripe.com/c/pay/cs_test_1" });
			controller.ControllerContext.HttpContext.Request.ContentType = "application/x-www-form-urlencoded";
			controller.ControllerContext.HttpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["packs"] = "2" });
			(await controller.Checkout()).Should().BeOfType<JsonResult>();
			_billing.Verify(x => x.BeginCheckoutAsync(DepartmentId, 2), Times.Once);
		}

		[Test]
		public async Task A_held_lms_shows_its_packs_seats_and_next_months_price()
		{
			var controller = Build(OwnerId, offered: true, Held(3, scheduled: 2), enabledSeats: 40);

			var view = ViewOf(await controller.Index());

			view.CanChangePacks.Should().BeTrue();
			view.Packs.Packs.Should().Be(3);
			view.Packs.PurchasedSeats.Should().Be(75);
			view.Packs.EnabledSeats.Should().Be(40);
			view.Packs.CurrentMonthly.Should().Be(237m);
			view.Packs.NextMonthly.Should().Be(158m, "a decrease to 2 packs is scheduled for the next billing date");
			view.Packs.MinimumPacks.Should().Be(2);
		}

		[Test]
		public async Task The_price_of_a_quantity_change_is_previewed_before_anything_changes()
		{
			var controller = Build(OwnerId, offered: true, Held(3));

			var json = JObject.FromObject((await controller.PreviewPacks(5)).Should().BeOfType<JsonResult>().Subject.Value);

			((decimal)json["nextMonthly"]).Should().Be(365m);
			((decimal)json["monthlyChange"]).Should().Be(128m);
			((bool)json["increase"]).Should().BeTrue();
			((decimal)json["proratedNow"]).Should().BeInRange(60m, 68m, "about half of the month is left");
			json["refusal"].Type.Should().Be(JTokenType.Null);
			_billing.Verify(x => x.ChangePacksAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Adding_packs_is_sent_to_the_billing_api_with_the_quantity_seen_and_audited()
		{
			var controller = Build(OwnerId, offered: true, Held(3));
			_billing.Setup(x => x.ChangePacksAsync(DepartmentId, 5, 3)).ReturnsAsync(new LmsPackChangeResult { Succeeded = true, Packs = 5, AmountChargedNow = 64m });

			var result = await controller.ChangePacks(5, 3);

			result.Should().BeOfType<RedirectToActionResult>().Which.RouteValues["result"].Should().Be(AddonBillingResults.PacksAdded);
			_billing.Verify(x => x.ChangePacksAsync(DepartmentId, 5, 3), Times.Once);
			_events.Verify(x => x.SendMessage(It.Is<AuditEvent>(a => a.Type == AuditLogTypes.LmsPacksChanged && a.UserId == OwnerId)), Times.Once);
		}

		[Test]
		public async Task Removing_packs_is_scheduled_and_never_below_the_lms_enabled_members()
		{
			var controller = Build(OwnerId, offered: true, Held(5), enabledSeats: 60);

			(await controller.ChangePacks(2, 5)).Should().BeOfType<RedirectToActionResult>().Which.RouteValues["result"].Should().Be(AddonBillingResults.PacksBelowSeats);
			_billing.Verify(x => x.ChangePacksAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);

			_billing.Setup(x => x.ChangePacksAsync(DepartmentId, 3, 5)).ReturnsAsync(new LmsPackChangeResult { Succeeded = true, Packs = 5, ScheduledPacks = 3 });
			(await controller.ChangePacks(3, 5)).Should().BeOfType<RedirectToActionResult>().Which.RouteValues["result"].Should().Be(AddonBillingResults.PacksScheduled);
		}

		[Test]
		public async Task A_stale_page_or_a_failed_billing_call_changes_nothing()
		{
			var controller = Build(OwnerId, offered: true, Held(3));

			(await controller.ChangePacks(5, 4)).Should().BeOfType<RedirectToActionResult>().Which.RouteValues["result"].Should().Be(AddonBillingResults.PacksFailed);
			_billing.Verify(x => x.ChangePacksAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);

			_billing.Setup(x => x.ChangePacksAsync(DepartmentId, 5, 3)).ReturnsAsync((LmsPackChangeResult)null);
			(await controller.ChangePacks(5, 3)).Should().BeOfType<RedirectToActionResult>().Which.RouteValues["result"].Should().Be(AddonBillingResults.PacksFailed);
			_events.Verify(x => x.SendMessage(It.IsAny<AuditEvent>()), Times.Never);
		}

		[Test]
		public async Task Only_the_managing_member_may_change_packs()
		{
			var controller = Build("officer", offered: true, Held(3), canManageSubscription: true);

			(await controller.ChangePacks(5, 3)).Should().BeOfType<ForbidResult>();
			(await controller.PreviewPacks(5)).Should().BeOfType<JsonResult>("members who manage the subscription may see prices");
			ViewOf(await controller.Index()).CanChangePacks.Should().BeFalse();
			_billing.Verify(x => x.ChangePacksAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Pack_outcomes_are_shown_and_unknown_results_ignored()
		{
			var controller = Build(OwnerId, offered: true, Held(3));

			ViewOf(await controller.Index(AddonBillingResults.PacksScheduled)).Result.Should().Be(AddonBillingResults.PacksScheduled);
			ViewOf(await controller.Index("<script>")).Result.Should().BeNull();
		}
	}
}
