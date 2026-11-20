using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services.AiDestination;
using Resgrid.Web.Services.Models.v4.AiDestinations;
using Resgrid.Workers.Framework.Logic;
using ApiController = Resgrid.Web.Services.Controllers.v4.AiDestinationsController;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using WebClaims = Resgrid.Web.Helpers.ClaimsAuthorizationHelper;
using WebController = Resgrid.Web.Areas.User.Controllers.AiDestinationsController;

namespace Resgrid.Tests.AiDestination
{
	/// <summary>
	/// Who may read and act on destination advice (v4 and web): reading needs Call_View and the right to view the call; asking, applying
	/// and dismissing need Call_Update and the right to edit the call. Checks happen before the advisor is touched.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AiDestinationsControllerTests
	{
		private const int DepartmentId = 7;
		private const int CallId = 300;

		private IHttpContextAccessor _previousApi;
		private IHttpContextAccessor _previousWeb;
		private Mock<IAiDestinationAdvisorService> _advisor;
		private Mock<ICallsService> _calls;
		private Mock<Resgrid.Model.Services.IAuthorizationService> _authorization;
		private ApiController _api;
		private WebController _web;
		private AiDestinationAdvice _advice;

		[SetUp]
		public void SetUp()
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, "user-1"), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "Test"))
			};
			_previousApi = ApiClaims._httpContextAccessor;
			_previousWeb = WebClaims._httpContextAccessor;
			ApiClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			WebClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			_advice = new AiDestinationAdvice
			{
				AiDestinationAdviceId = "adv-1", DepartmentId = DepartmentId, CallId = CallId, CurrentPoiId = 10, RecommendedPoiId = 20, Rationale = "Level I trauma.",
				Confidence = 0.8, IsChange = true, State = (int)AiDestinationAdviceStates.Open, ReviewTrigger = 4, CreatedOn = new DateTime(2026, 11, 19, 15, 0, 0)
			};
			_advisor = new Mock<IAiDestinationAdvisorService>();
			_advisor.Setup(a => a.IsAvailableAsync(DepartmentId)).ReturnsAsync(true);
			_advisor.Setup(a => a.GetOpenAdviceAsync(DepartmentId, CallId)).ReturnsAsync(() => _advice);
			_advisor.Setup(a => a.GetAdviceByIdAsync(DepartmentId, "adv-1")).ReturnsAsync(() => _advice);
			_advisor.Setup(a => a.DescribeAsync(DepartmentId, It.IsAny<AiDestinationAdvice>())).ReturnsAsync(new AiDestinationAdviceDetails
			{
				CurrentPoiName = "Community Hospital", RecommendedPoiName = "Regional Trauma Center",
				RequiredCapabilities = { new System.Collections.Generic.KeyValuePair<string, string>("cap-1", "Level I Trauma") }
			});
			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = CallId, DepartmentId = DepartmentId, State = (int)CallStates.Active });
			_authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();

			_api = new ApiController(_advisor.Object, _calls.Object, _authorization.Object) { ControllerContext = new ControllerContext { HttpContext = http } };
			_web = new WebController(_advisor.Object, _calls.Object, _authorization.Object) { ControllerContext = new ControllerContext { HttpContext = http } };
		}

		[TearDown]
		public void TearDown()
		{
			ApiClaims._httpContextAccessor = _previousApi;
			WebClaims._httpContextAccessor = _previousWeb;
		}

		private void CanView(bool value) => _authorization.Setup(a => a.CanUserViewCallAsync("user-1", CallId)).ReturnsAsync(value);
		private void CanEdit(bool value) => _authorization.Setup(a => a.CanUserEditCallAsync("user-1", CallId)).ReturnsAsync(value);

		private static AiDestinationAdviceResult Body(ActionResult<AiDestinationAdviceResult> result) =>
			(AiDestinationAdviceResult)((OkObjectResult)result.Result).Value;

		[TestCase(nameof(ApiController.GetAdvice), ResgridResources.Call_View)]
		[TestCase(nameof(ApiController.RequestAdvice), ResgridResources.Call_Update)]
		[TestCase(nameof(ApiController.ApplyAdvice), ResgridResources.Call_Update)]
		[TestCase(nameof(ApiController.DismissAdvice), ResgridResources.Call_Update)]
		public void Each_v4_action_keeps_its_resource_policy(string action, string policy)
		{
			typeof(ApiController).GetMethod(action)!.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).Should().Contain(policy);
		}

		[TestCase(nameof(WebController.Advice), ResgridResources.Call_View)]
		[TestCase(nameof(WebController.Ask), ResgridResources.Call_Update)]
		[TestCase(nameof(WebController.Apply), ResgridResources.Call_Update)]
		[TestCase(nameof(WebController.Dismiss), ResgridResources.Call_Update)]
		public void Each_web_action_keeps_its_resource_policy_and_posts_need_antiforgery(string action, string policy)
		{
			var method = typeof(WebController).GetMethod(action)!;
			method.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).Should().Contain(policy);
			if (method.GetCustomAttribute<HttpPostAttribute>() != null)
				method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
		}

		[Test]
		public async Task Reading_needs_the_right_to_view_the_call()
		{
			CanView(false);
			(await _api.GetAdvice(CallId)).Result.Should().BeOfType<UnauthorizedResult>();
			_advisor.Verify(a => a.GetOpenAdviceAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_viewer_sees_the_advice_and_whether_they_may_act_on_it()
		{
			CanView(true);
			CanEdit(false);

			var body = Body(await _api.GetAdvice(CallId));

			body.Data.Available.Should().BeTrue();
			body.Data.CanApply.Should().BeFalse();
			body.Data.Advice.RecommendedPoiName.Should().Be("Regional Trauma Center");
			body.Data.Advice.RequiredCapabilities.Should().ContainSingle(c => c.Name == "Level I Trauma");
			body.Data.Advice.CreatedOn.Kind.Should().Be(DateTimeKind.Utc);
			body.Data.Advice.IsConfident.Should().Be(0.8 >= AiDestinationConfig.MinimumConfidence);
			body.Data.Advice.Trigger.Should().Be(4);
		}

		[Test]
		public async Task An_unavailable_department_gets_no_advice()
		{
			CanView(true);
			_advisor.Setup(a => a.IsAvailableAsync(DepartmentId)).ReturnsAsync(false);

			var body = Body(await _api.GetAdvice(CallId));

			body.Data.Available.Should().BeFalse();
			body.Data.Advice.Should().BeNull();
		}

		[Test]
		public async Task Asking_applying_and_dismissing_need_the_right_to_edit_the_call()
		{
			CanView(true);
			CanEdit(false);

			(await _api.RequestAdvice(new RequestAiDestinationAdviceInput { CallId = CallId }, CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
			(await _api.ApplyAdvice(new ApplyAiDestinationAdviceInput { AdviceId = "adv-1" }, CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
			(await _api.DismissAdvice(new DismissAiDestinationAdviceInput { AdviceId = "adv-1" }, CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
			(await _web.Apply("adv-1", CancellationToken.None)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			(await _web.Ask(CallId, CancellationToken.None)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

			_advisor.Verify(a => a.AdviseNowAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_advisor.Verify(a => a.ApplyAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_advisor.Verify(a => a.DismissAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task An_editor_applies_advice_of_their_department_only()
		{
			CanEdit(true);
			_advisor.Setup(a => a.ApplyAsync(DepartmentId, "adv-1", "user-1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.Applied, _advice));

			var applied = Body(await _api.ApplyAdvice(new ApplyAiDestinationAdviceInput { AdviceId = "adv-1" }, CancellationToken.None));
			applied.Data.Outcome.Should().Be(AiDestinationOutcomes.Applied);

			// Another department's advice is simply not found (GetAdviceByIdAsync is department-scoped).
			var missing = Body(await _api.ApplyAdvice(new ApplyAiDestinationAdviceInput { AdviceId = "adv-other" }, CancellationToken.None));
			missing.Status.Should().Be("not_found");
			_advisor.Verify(a => a.ApplyAsync(It.IsAny<int>(), "adv-other", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Asking_reports_why_no_advice_came_back()
		{
			CanEdit(true);
			_advisor.Setup(a => a.AdviseNowAsync(DepartmentId, CallId, "user-1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(AiDestinationAdviceOutcome.Of(AiDestinationOutcomes.ProtectionUnsupported));

			var body = Body(await _api.RequestAdvice(new RequestAiDestinationAdviceInput { CallId = CallId }, CancellationToken.None));

			body.Data.Available.Should().BeFalse();
			body.Data.Outcome.Should().Be(AiDestinationOutcomes.ProtectionUnsupported);
			body.Data.Advice.Should().BeNull();
		}

		[Test]
		public async Task Worker_81_idles_while_the_feature_is_off()
		{
			var saved = AiDestinationConfig.Enabled;
			try
			{
				AiDestinationConfig.Enabled = false;
				var result = await new AiDestinationAdviceLogic().Process(CancellationToken.None);
				result.Item1.Should().BeTrue();
				result.Item2.Should().Contain("turned off");
			}
			finally
			{
				AiDestinationConfig.Enabled = saved;
			}
		}

		[Test]
		public void A_status_only_call_update_does_not_queue_a_review_twice()
		{
			var listener = new AiDestinationReviewEventListener(Mock.Of<Resgrid.Model.Providers.IEventAggregator>(), Mock.Of<Autofac.ILifetimeScope>());
			var call = new Call { CallId = 5, DepartmentId = DepartmentId, NatureOfCall = "Fall", DestinationPoiId = 10, State = (int)CallStates.Active };
			var now = new DateTime(2026, 11, 19, 15, 0, 0, DateTimeKind.Utc);

			listener.HasRelevantChange(call, now).Should().BeTrue();
			listener.HasRelevantChange(call, now.AddSeconds(10)).Should().BeFalse("nothing the advice reads changed");

			call.NatureOfCall = "Fall, now unresponsive";
			listener.HasRelevantChange(call, now.AddSeconds(20)).Should().BeTrue();
			listener.HasRelevantChange(call, now.AddHours(1)).Should().BeTrue("the memory is short");
		}
	}
}
