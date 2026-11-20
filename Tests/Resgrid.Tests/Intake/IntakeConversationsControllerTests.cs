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
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.IntakeConversations;
using ApiController = Resgrid.Web.Services.Controllers.v4.IntakeConversationsController;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using WebClaims = Resgrid.Web.Helpers.ClaimsAuthorizationHelper;
using WebController = Resgrid.Web.Areas.User.Controllers.IntakeConversationsController;

namespace Resgrid.Tests.Intake
{
	/// <summary>
	/// Who may read and act on an intake SMS conversation (v4 and web, plan §8): reading needs Call_View and the right to view the call
	/// (the texter's number masked unless the reader may edit the call); Connect, Send, Release and Close need Call_Update and the right
	/// to edit the conversation's call. Checks happen before the conversation service acts.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class IntakeConversationsControllerTests
	{
		private const int DepartmentId = 7;
		private const int CallId = 300;
		private const string Texter = "+15305550199";

		private IHttpContextAccessor _previousApi;
		private IHttpContextAccessor _previousWeb;
		private Mock<IIntakeConversationService> _conversations;
		private Mock<IIntakeConversationOperations> _operations;
		private Mock<ICallsService> _calls;
		private Mock<IUserProfileService> _profiles;
		private Mock<Resgrid.Model.Services.IAuthorizationService> _authorization;
		private IntakeConversation _conversation;
		private ApiController _api;
		private WebController _web;

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

			_conversation = new IntakeConversation
			{
				IntakeConversationId = "conv-1", DepartmentId = DepartmentId, CallId = CallId, ChannelType = (int)IntakeChannelTypes.Sms, ParticipantAddress = Texter,
				State = (int)IntakeConversationStates.Open, Handler = (int)IntakeConversationHandlers.Dispatcher, AssignedUserId = "disp-1",
				StartedOn = new DateTime(2026, 11, 20, 15, 0, 0), LastActivityOn = new DateTime(2026, 11, 20, 15, 5, 0)
			};
			var view = new IntakeConversationView
			{
				Conversation = _conversation,
				Messages = new List<IntakeConversationEntry>
				{
					new IntakeConversationEntry { MessageId = "m1", Direction = 1, Author = 1, Text = "Car crash", Status = 1, CreatedOn = new DateTime(2026, 11, 20, 15, 0, 0) },
					new IntakeConversationEntry { MessageId = "m2", Direction = 2, Author = 3, AuthorName = "Jane", Text = "Where?", Status = 3, CreatedOn = new DateTime(2026, 11, 20, 15, 1, 0) }
				}
			};

			_conversations = new Mock<IIntakeConversationService>();
			_conversations.Setup(c => c.GetForCallAsync(DepartmentId, CallId)).ReturnsAsync(() => _conversation);
			_conversations.Setup(c => c.GetByIdAsync(DepartmentId, "conv-1")).ReturnsAsync(() => _conversation);
			_conversations.Setup(c => c.GetViewAsync(DepartmentId, "conv-1", "user-1")).ReturnsAsync(view);
			_conversations.Setup(c => c.ConnectDispatcherAsync(DepartmentId, "conv-1", "user-1", It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(IntakeConversationActionResults.Done);
			_conversations.Setup(c => c.SendDispatcherMessageAsync(DepartmentId, "conv-1", "user-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(IntakeConversationActionResults.HeldByAnotherDispatcher);
			_operations = new Mock<IIntakeConversationOperations>();
			_operations.Setup(o => o.GetViewAsync(DepartmentId, "conv-1", "user-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(view);
			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = CallId, DepartmentId = DepartmentId, State = (int)CallStates.Pending });
			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(p => p.GetProfileByUserIdAsync("disp-1", It.IsAny<bool>())).ReturnsAsync(new UserProfile { UserId = "disp-1", FirstName = "Jane", LastName = "Doe" });
			_authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();

			_api = new ApiController(_conversations.Object, _operations.Object, _calls.Object, _profiles.Object, _authorization.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
			_web = new WebController(_conversations.Object, _calls.Object, _profiles.Object, _authorization.Object) { ControllerContext = new ControllerContext { HttpContext = http } };
		}

		[TearDown]
		public void TearDown()
		{
			ApiClaims._httpContextAccessor = _previousApi;
			WebClaims._httpContextAccessor = _previousWeb;
		}

		private void CanView(bool value) => _authorization.Setup(a => a.CanUserViewCallAsync("user-1", CallId)).ReturnsAsync(value);
		private void CanEdit(bool value) => _authorization.Setup(a => a.CanUserEditCallAsync("user-1", CallId)).ReturnsAsync(value);

		/// <summary>The web area's Unauthorized() redirects to the unauthorized page.</summary>
		private static void WebDenied(IActionResult result) =>
			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

		private static IntakeConversationResult Body(ActionResult<IntakeConversationResult> result) => (IntakeConversationResult)((OkObjectResult)result.Result).Value;

		private void NoActions()
		{
			_conversations.Verify(c => c.ConnectDispatcherAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
			_conversations.Verify(c => c.SendDispatcherMessageAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_conversations.Verify(c => c.ReleaseDispatcherAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_conversations.Verify(c => c.CloseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase(nameof(ApiController.GetForCall), ResgridResources.Call_View)]
		[TestCase(nameof(ApiController.Connect), ResgridResources.Call_Update)]
		[TestCase(nameof(ApiController.Send), ResgridResources.Call_Update)]
		[TestCase(nameof(ApiController.Release), ResgridResources.Call_Update)]
		[TestCase(nameof(ApiController.Close), ResgridResources.Call_Update)]
		public void Each_v4_action_keeps_its_resource_policy(string action, string policy)
		{
			typeof(ApiController).GetMethod(action)!.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).Should().Contain(policy);
		}

		[TestCase(nameof(WebController.Thread), ResgridResources.Call_View)]
		[TestCase(nameof(WebController.Connect), ResgridResources.Call_Update)]
		[TestCase(nameof(WebController.Send), ResgridResources.Call_Update)]
		[TestCase(nameof(WebController.Release), ResgridResources.Call_Update)]
		[TestCase(nameof(WebController.Close), ResgridResources.Call_Update)]
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

			(await _api.GetForCall(CallId, CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
			WebDenied(await _web.Thread(CallId));
			_conversations.Verify(c => c.GetForCallAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_viewer_who_cannot_edit_sees_the_thread_with_the_number_masked()
		{
			CanView(true);
			CanEdit(false);

			var body = Body(await _api.GetForCall(CallId, CancellationToken.None));

			body.Data.CanConnect.Should().BeFalse();
			body.Data.Conversation.ParticipantAddress.Should().Be("***0199");
			body.Data.Conversation.AssignedName.Should().Be("Jane Doe");
			body.Data.Conversation.StartedOn.Kind.Should().Be(DateTimeKind.Utc);
			body.Data.Messages.Select(m => m.Text).Should().Equal("Car crash", "Where?");
			body.Data.Messages[1].AuthorName.Should().Be("Jane");
			body.Data.Outcome.Should().BeNull();
		}

		[Test]
		public async Task An_editor_sees_the_number_and_may_connect()
		{
			CanView(true);
			CanEdit(true);

			var body = Body(await _api.GetForCall(CallId, CancellationToken.None));

			body.Data.CanConnect.Should().BeTrue();
			body.Data.Conversation.ParticipantAddress.Should().Be(Texter);
		}

		[Test]
		public async Task A_call_without_a_conversation_has_none_and_another_departments_call_is_not_found()
		{
			CanView(true);
			_conversation = null;
			Body(await _api.GetForCall(CallId, CancellationToken.None)).Data.Conversation.Should().BeNull();

			_calls.Setup(c => c.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = CallId, DepartmentId = DepartmentId + 1 });
			Body(await _api.GetForCall(CallId, CancellationToken.None)).Status.Should().Be("not_found");
		}

		[Test]
		public async Task Acting_needs_the_right_to_edit_the_call()
		{
			CanEdit(false);

			(await _api.Connect(new ConnectIntakeConversationInput { ConversationId = "conv-1" }, CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
			(await _api.Send(new SendIntakeConversationInput { ConversationId = "conv-1", Text = "Hi" }, CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
			(await _api.Release(new ReleaseIntakeConversationInput { ConversationId = "conv-1" }, CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
			(await _api.Close(new CloseIntakeConversationInput { ConversationId = "conv-1", Notify = true }, CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
			WebDenied(await _web.Connect("conv-1", false));
			WebDenied(await _web.Send("conv-1", "Hi"));
			WebDenied(await _web.Release("conv-1"));
			WebDenied(await _web.Close("conv-1", null, true));
			NoActions();
		}

		[Test]
		public async Task Another_departments_conversation_is_not_found()
		{
			CanEdit(true);

			var body = Body(await _api.Connect(new ConnectIntakeConversationInput { ConversationId = "other" }, CancellationToken.None));

			body.Data.Outcome.Should().Be("NotFound");
			body.Status.Should().Be("not_found");
			NoActions();
		}

		[Test]
		public async Task An_editor_connects_and_the_outcome_and_thread_come_back()
		{
			CanEdit(true);

			var connected = Body(await _api.Connect(new ConnectIntakeConversationInput { ConversationId = "conv-1", TakeOver = true }, CancellationToken.None));
			var refused = Body(await _api.Send(new SendIntakeConversationInput { ConversationId = "conv-1", Text = "Hi" }, CancellationToken.None));

			connected.Data.Outcome.Should().Be("Done");
			connected.Data.Conversation.Id.Should().Be("conv-1");
			refused.Data.Outcome.Should().Be("HeldByAnotherDispatcher");
			_conversations.Verify(c => c.ConnectDispatcherAsync(DepartmentId, "conv-1", "user-1", true, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_sent_text_reports_its_outcome_even_when_the_thread_cannot_be_read_again()
		{
			// A 500 after the text went out would make a client send it again.
			CanEdit(true);
			_conversations.Setup(c => c.SendDispatcherMessageAsync(DepartmentId, "conv-1", "user-1", "Where are you?", It.IsAny<CancellationToken>()))
				.ReturnsAsync(IntakeConversationActionResults.Done);
			_operations.Setup(o => o.GetViewAsync(DepartmentId, "conv-1", "user-1", It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("db"));
			_conversations.Setup(c => c.GetViewAsync(DepartmentId, "conv-1", "user-1")).ThrowsAsync(new TimeoutException("db"));

			var body = Body(await _api.Send(new SendIntakeConversationInput { ConversationId = "conv-1", Text = "Where are you?" }, CancellationToken.None));
			var web = Newtonsoft.Json.JsonConvert.SerializeObject(((JsonResult)await _web.Send("conv-1", "Where are you?")).Value);

			body.Data.Outcome.Should().Be("Done");
			body.Data.Conversation.Should().BeNull();
			body.Status.Should().Be("success");
			web.Should().Contain("\"success\":true").And.Contain("\"outcome\":\"Done\"");
			_conversations.Verify(c => c.SendDispatcherMessageAsync(DepartmentId, "conv-1", "user-1", "Where are you?", It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task Mutations_on_a_deleted_calls_conversation_are_not_found()
		{
			CanEdit(true);
			_calls.Setup(c => c.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = CallId, DepartmentId = DepartmentId, IsDeleted = true });

			Body(await _api.Send(new SendIntakeConversationInput { ConversationId = "conv-1", Text = "Hi" }, CancellationToken.None)).Data.Outcome.Should().Be("NotFound");
			Newtonsoft.Json.JsonConvert.SerializeObject(((JsonResult)await _web.Send("conv-1", "Hi")).Value).Should().Contain("\"outcome\":\"NotFound\"");
			NoActions();
		}

		[Test]
		public async Task The_web_panel_masks_the_number_for_viewers_and_says_whether_it_is_mine()
		{
			CanView(true);
			CanEdit(false);

			var json = (JsonResult)await _web.Thread(CallId);
			var value = Newtonsoft.Json.JsonConvert.SerializeObject(json.Value);

			value.Should().Contain("\"participant\":\"***0199\"").And.Contain("\"canConnect\":false").And.Contain("\"isMine\":false");
			value.Should().NotContain(Texter);
		}
	}
}
