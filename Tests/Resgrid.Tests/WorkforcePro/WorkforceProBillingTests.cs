using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
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
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Subscription;
using RestSharp;
using RestSharp.Serializers.NewtonsoftJson;

namespace Resgrid.Tests.WorkforcePro
{
	/// <summary>
	/// The Workforce Pro billing page (shifts plan §7.2, §7.4) on the shared pack add-on path: offered only with the Workforce.Pro toggle, sold
	/// only with a paid base plan, priced on Workforce Pro's own schedule, packs changed through api/WorkforceProBilling with the quantity seen and
	/// audited, decreases never below the members switched on; and the proxy calls exactly the LmsBilling contract on the Workforce Pro route.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class WorkforceProBillingTests
	{
		private const int DepartmentId = 82;
		private const string OwnerId = "owner";

		private Mock<IWorkforceProBillingService> _billing;
		private Mock<IWorkforceProSeatService> _seats;
		private Mock<IEventAggregator> _events;
		private LmsBillingStatus _status;

		[TearDown]
		public void TearDown() => Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;

		private static IStringLocalizer<T> Strings<T>()
		{
			var localizer = new Mock<IStringLocalizer<T>>();
			localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			return localizer.Object;
		}

		private WorkforceProBillingController Build(string userId, bool offered, LmsBillingStatus status, int enabledSeats = 0)
		{
			_status = status;
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = OwnerId });
			departments.Setup(x => x.GetDepartmentMemberAsync(userId, DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = userId });
			var authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(x => x.EvaluateFreshAsync(FeatureFlagKeys.WorkforcePro, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { Key = FeatureFlagKeys.WorkforcePro, IsEnabled = offered });

			_billing = new Mock<IWorkforceProBillingService>();
			_billing.Setup(x => x.GetAsync(DepartmentId)).ReturnsAsync(() => _status);
			_seats = new Mock<IWorkforceProSeatService>();
			var minimum = Math.Max(1, (int)Math.Ceiling(enabledSeats / 25d));
			_seats.Setup(x => x.GetMinimumPacksAsync(DepartmentId)).ReturnsAsync(minimum);
			_seats.Setup(x => x.GetSummaryAsync(DepartmentId)).ReturnsAsync(new WorkforceProSeatSummary
			{
				Offered = offered, State = status?.Active == true ? WorkforceProSubscriptionStates.Active : WorkforceProSubscriptionStates.None,
				PurchasedPacks = status?.Packs ?? 0, PackSize = 25, EnabledSeats = enabledSeats, MinimumPacks = minimum
			});
			_events = new Mock<IEventAggregator>();

			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, userId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return new WorkforceProBillingController(_billing.Object, _seats.Object, _events.Object, departments.Object, authorization.Object, flags.Object,
				Strings<Resgrid.Localization.Areas.User.Addons.Addons>())
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				Url = Mock.Of<IUrlHelper>()
			};
		}

		private static LmsBillingStatus NotHeld(bool hasPlan = true) => new LmsBillingStatus { Provider = "Stripe", Currency = "USD", CheckoutAvailable = true, HasEntityPlan = hasPlan };

		private static LmsBillingStatus Held(int packs) => new LmsBillingStatus
		{
			Provider = "Stripe", Currency = "USD", CheckoutAvailable = true, HasEntityPlan = true, Active = true, CanCancel = true, Packs = packs,
			PeriodStartUtc = DateTime.UtcNow.AddDays(-15), PaidThroughUtc = DateTime.UtcNow.AddDays(15)
		};

		private static AddonBillingView ViewOf(IActionResult result) =>
			result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<AddonBillingView>().Subject;

		[Test]
		public async Task Without_the_operator_toggle_Workforce_Pro_is_not_offered()
		{
			(await Build(OwnerId, offered: false, NotHeld()).Index()).Should().BeOfType<RedirectToActionResult>().Which.ControllerName.Should().Be("Subscription");
		}

		[Test]
		public async Task Without_a_paid_base_plan_Workforce_Pro_cannot_be_bought()
		{
			var controller = Build(OwnerId, offered: true, NotHeld(hasPlan: false));

			var view = ViewOf(await controller.Index());
			view.CanBuy.Should().BeFalse();
			view.Notices.Should().Contain("WorkforceProRequiresPaidPlan");

			controller.ControllerContext.HttpContext.Request.ContentType = "application/x-www-form-urlencoded";
			controller.ControllerContext.HttpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["packs"] = "2" });
			(await controller.Checkout()).Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
			_billing.Verify(x => x.BeginCheckoutAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_held_add_on_shows_its_packs_on_the_Workforce_Pro_schedule_and_previews_a_change()
		{
			var controller = Build(OwnerId, offered: true, Held(4), enabledSeats: 90);

			var view = ViewOf(await controller.Index());
			view.Packs.Packs.Should().Be(4);
			view.Packs.CurrentMonthly.Should().Be(476m, "4 × $119");
			view.Notes.Should().Contain("WorkforceProBillingPacksNote");

			var json = JObject.FromObject((await controller.PreviewPacks(5)).Should().BeOfType<JsonResult>().Subject.Value);
			((decimal)json["nextMonthly"]).Should().Be(555m, "the fifth pack is billed at the second tier, $79");
			((decimal)json["monthlyChange"]).Should().Be(79m);
		}

		[Test]
		public async Task Adding_packs_is_sent_with_the_quantity_seen_and_audited_and_a_decrease_never_strands_members()
		{
			var controller = Build(OwnerId, offered: true, Held(3), enabledSeats: 60);
			_billing.Setup(x => x.ChangePacksAsync(DepartmentId, 5, 3)).ReturnsAsync(new LmsPackChangeResult { Succeeded = true, Packs = 5 });

			(await controller.ChangePacks(5, 3)).Should().BeOfType<RedirectToActionResult>().Which.RouteValues["result"].Should().Be(AddonBillingResults.PacksAdded);
			_events.Verify(x => x.SendMessage(It.Is<AuditEvent>(a => a.Type == AuditLogTypes.WorkforceProPacksChanged && a.UserId == OwnerId)), Times.Once);

			(await controller.ChangePacks(2, 3)).Should().BeOfType<RedirectToActionResult>().Which.RouteValues["result"].Should().Be(AddonBillingResults.PacksBelowSeats);
			_billing.Verify(x => x.ChangePacksAsync(DepartmentId, 2, It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Only_the_managing_member_may_change_packs()
		{
			(await Build("officer", offered: true, Held(3)).ChangePacks(5, 3)).Should().BeOfType<ForbidResult>();
			_billing.Verify(x => x.ChangePacksAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		private sealed class CapturingHandler : System.Net.Http.HttpMessageHandler
		{
			public readonly List<(string Method, string Path, string Query, string Body)> Requests = new List<(string, string, string, string)>();

			protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
			{
				var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
				Requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, body));
				var json = request.RequestUri.AbsolutePath.EndsWith("/Status") ? "{\"Active\":true,\"Packs\":2,\"Currency\":\"USD\",\"HasEntityPlan\":true}"
					: request.RequestUri.AbsolutePath.EndsWith("/ChangePacks") ? "{\"Succeeded\":true,\"Packs\":3}"
					: request.RequestUri.AbsolutePath.EndsWith("/Checkout") ? "{\"Provider\":\"Stripe\",\"Url\":\"https://checkout.stripe.com/c/pay/x\"}" : "true";
				return new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK) { Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json") };
			}
		}

		[Test]
		public async Task The_proxy_calls_the_LmsBilling_contract_on_the_Workforce_Pro_route()
		{
			var url = Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl;
			var key = Resgrid.Config.ApiConfig.BackendInternalApikey;
			try
			{
				Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.invalid";
				Resgrid.Config.ApiConfig.BackendInternalApikey = "unit-test-only";
				var handler = new CapturingHandler();
				// The production client (ServicesModule) serializes with Newtonsoft: PascalCase bodies, as the Billing API binds them.
				var client = new RestClient(new RestClientOptions("https://billing.example.invalid") { ConfigureMessageHandler = _ => handler },
					configureSerialization: serializer => serializer.UseNewtonsoftJson());
				var proxy = new WorkforceProBillingService(() => client);

				(await proxy.GetAsync(DepartmentId)).Packs.Should().Be(2);
				(await proxy.BeginCheckoutAsync(DepartmentId, 2)).Provider.Should().Be("Stripe");
				(await proxy.ChangePacksAsync(DepartmentId, 3, 2)).Succeeded.Should().BeTrue();
				(await proxy.CancelRenewalAsync(DepartmentId)).Should().BeTrue();

				handler.Requests.Select(r => r.Path).Should().Equal("/api/WorkforceProBilling/Status", "/api/WorkforceProBilling/Checkout",
					"/api/WorkforceProBilling/ChangePacks", "/api/WorkforceProBilling/CancelRenewal");
				handler.Requests[0].Query.Should().Contain("departmentId=82");
				// The same body as the LMS proxy (the Billing API's DepartmentInput binds names case-insensitively).
				int Field(string body, string name) => JObject.Parse(body).GetValue(name, StringComparison.OrdinalIgnoreCase)!.Value<int>();
				Field(handler.Requests[1].Body, "DepartmentId").Should().Be(DepartmentId);
				Field(handler.Requests[1].Body, "Packs").Should().Be(2);
				Field(handler.Requests[2].Body, "Packs").Should().Be(3);
				Field(handler.Requests[2].Body, "ExpectedPacks").Should().Be(2);

				// Billing not configured: nothing is called and callers fail closed.
				Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = null;
				(await proxy.GetAsync(DepartmentId)).Should().BeNull();
				handler.Requests.Should().HaveCount(4);
			}
			finally
			{
				Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = url;
				Resgrid.Config.ApiConfig.BackendInternalApikey = key;
			}
		}
	}
}
