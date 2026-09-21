using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Services;
using Resgrid.Model.Repositories;
using Resgrid.Providers.Chatbot.Interfaces;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Chatbot
{
	[TestFixture, NonParallelizable]
	public class MessagingAccountsTests
	{
		private const string UserId = "messaging-owner";
		private IHttpContextAccessor _previousAccessor;
		private DefaultHttpContext _http;
		private Mock<IChatbotUserIdentityService> _identities;
		private Mock<IChatbotAdapterRegistry> _registry;
		private List<ChatbotUserIdentity> _accounts;
		private MessagingAccountsController _controller;

		[SetUp]
		public void SetUp()
		{
			_previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			_http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, "77")
				}, "Test"))
			};
			_http.Request.Method = "POST";
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
			_accounts = new List<ChatbotUserIdentity>();
			_identities = new Mock<IChatbotUserIdentityService>();
			_identities.Setup(i => i.GetUserIdentitiesAsync(UserId)).ReturnsAsync(_accounts);
			_identities.Setup(i => i.UnlinkUserAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
			_registry = new Mock<IChatbotAdapterRegistry>();
			_registry.Setup(r => r.GetAdapter(ChatbotPlatform.Telegram)).Returns(Mock.Of<IExternalChatbotAdapter>());
			_controller = new MessagingAccountsController(_identities.Object,
				new CodeLinkingService(_identities.Object, Mock.Of<IChatbotLinkingCodeRepository>()), _registry.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = _http }
			};
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		[Test]
		public async Task Unlink_IdOutsideCurrentUsersAccounts_DoesNotDelete()
		{
			_accounts.Add(Account("own-link", UserId));

			(await _controller.Unlink("someone-elses-link")).Should().BeOfType<NotFoundResult>();

			_identities.Verify(i => i.GetUserIdentitiesAsync(UserId), Times.Once);
			_identities.Verify(i => i.UnlinkUserAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Unlink_ForeignOwnerReturnedByService_IsStillRejected()
		{
			// Defense in depth: a matching row id alone must never authorize deleting another user's link.
			_accounts.Add(Account("foreign-link", "another-user"));

			(await _controller.Unlink("foreign-link")).Should().BeOfType<NotFoundResult>();

			_identities.Verify(i => i.UnlinkUserAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Unlink_CurrentUsersExternalAccount_DeletesOnlyThatAccount()
		{
			_accounts.Add(Account("own-link", UserId));
			_accounts.Add(Account("another-own-link", UserId));

			var result = await _controller.Unlink("own-link");

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be(nameof(MessagingAccountsController.Index));
			_identities.Verify(i => i.UnlinkUserAsync("own-link"), Times.Once);
			_identities.Verify(i => i.UnlinkUserAsync(It.Is<string>(id => id != "own-link")), Times.Never);
		}

		[Test]
		public async Task Unlink_SmsIdentity_CannotBypassTheProfilePhoneFlow()
		{
			var account = Account("sms-link", UserId);
			account.Platform = ChatbotPlatform.SmsTwilio;
			_accounts.Add(account);

			(await _controller.Unlink("sms-link")).Should().BeOfType<NotFoundResult>();

			_identities.Verify(i => i.UnlinkUserAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Unlink_WithoutAuthenticatedUserId_DoesNotReadOrDeleteAccounts()
		{
			_http.User = new ClaimsPrincipal(new ClaimsIdentity());

			(await _controller.Unlink("own-link")).Should().BeOfType<RedirectResult>()
				.Which.Url.Should().Be("/Public/Unauthorized");

			_identities.Verify(i => i.GetUserIdentitiesAsync(It.IsAny<string>()), Times.Never);
			_identities.Verify(i => i.UnlinkUserAsync(It.IsAny<string>()), Times.Never);
		}

		[TestCase(nameof(MessagingAccountsController.Generate))]
		[TestCase(nameof(MessagingAccountsController.Unlink))]
		public void AccountMutations_RequirePostAndAntiforgery(string action)
		{
			var method = typeof(MessagingAccountsController).GetMethod(action);
			method.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
			method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
			method.GetCustomAttribute<HttpGetAttribute>().Should().BeNull();
		}

		private static ChatbotUserIdentity Account(string id, string userId) => new()
		{
			Id = id, UserId = userId, Platform = ChatbotPlatform.Telegram, PlatformUserId = "1234567", IsActive = true
		};
	}
}
