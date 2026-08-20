using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class ChatAuthorizationTests
	{
		[TestCase(true, "user-1", "1", true, true)]
		[TestCase(false, "user-1", "1", true, false)]
		[TestCase(true, null, "1", true, false)]
		[TestCase(true, "", "1", true, false)]
		[TestCase(true, "user-1", null, true, false)]
		[TestCase(true, "user-1", "0", true, false)]
		[TestCase(true, "user-1", "not-a-department", true, false)]
		[TestCase(true, "user-1", "1", false, false)]
		public async Task chat_policy_requires_authenticated_principal_and_all_required_claims(
			bool isAuthenticated, string userId, string departmentId, bool hasMessagesView, bool expected)
		{
			var claims = new List<Claim>();
			if (userId != null)
				claims.Add(new Claim(ClaimTypes.PrimarySid, userId));
			if (departmentId != null)
				claims.Add(new Claim(ClaimTypes.PrimaryGroupSid, departmentId));
			if (hasMessagesView)
				claims.Add(new Claim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.View));

			var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, isAuthenticated ? "Test" : null));
			var policy = new AuthorizationPolicyBuilder().RequireChatAccessClaims().Build();
			var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
			var authorizationService = services.GetRequiredService<IAuthorizationService>();

			var result = await authorizationService.AuthorizeAsync(principal, null, policy);

			result.Succeeded.Should().Be(expected);
		}

		[Test]
		public void chat_controller_requires_messages_view_claim()
		{
			var authorize = typeof(ChatController).GetCustomAttributes<AuthorizeAttribute>()
				.Single(x => x.Policy == ResgridResources.Messages_View);

			authorize.Policy.Should().Be(ResgridResources.Messages_View);
		}

		[Test]
		public void chat_moderation_controller_requires_messages_view_claim()
		{
			var authorize = typeof(ChatModerationController).GetCustomAttributes<AuthorizeAttribute>()
				.Single(x => x.Policy == ResgridResources.Messages_View);

			authorize.Policy.Should().Be(ResgridResources.Messages_View);
		}

		[TestCase(nameof(ChatbotController.GetChatChannel), ResgridResources.Messages_View)]
		[TestCase(nameof(ChatbotController.SendChatMessage), ResgridResources.Messages_View)]
		[TestCase(nameof(ChatbotController.SendChatMessage), ResgridResources.Messages_Create)]
		[TestCase(nameof(ChatbotController.NewChatSession), ResgridResources.Messages_View)]
		[TestCase(nameof(ChatbotController.NewChatSession), ResgridResources.Messages_Create)]
		[TestCase(nameof(ChatbotController.AskIncident), ResgridResources.Messages_View)]
		[TestCase(nameof(ChatbotController.AskIncident), ResgridResources.Messages_Create)]
		[TestCase(nameof(ChatbotController.IncidentSuggestions), ResgridResources.Messages_View)]
		public void chatbot_chat_actions_require_message_claims(string methodName, string policy)
		{
			var method = typeof(ChatbotController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);

			method.Should().NotBeNull();
			method.GetCustomAttributes<AuthorizeAttribute>().Should().ContainSingle(x => x.Policy == policy);
		}

		[TestCase(nameof(ChatController.CreateDirectMessage), ResgridResources.Messages_Create)]
		[TestCase(nameof(ChatController.CreateIncidentCommanderLine), ResgridResources.Messages_Create)]
		[TestCase(nameof(ChatController.CreateAdHocChannel), ResgridResources.Messages_Create)]
		[TestCase(nameof(ChatController.CreateCustomChannel), ResgridResources.Messages_Create)]
		[TestCase(nameof(ChatController.SendMessage), ResgridResources.Messages_Create)]
		[TestCase(nameof(ChatController.AddReaction), ResgridResources.Messages_Create)]
		[TestCase(nameof(ChatController.UploadAttachment), ResgridResources.Messages_Create)]
		[TestCase(nameof(ChatController.FlagMessage), ResgridResources.Messages_Create)]
		[TestCase(nameof(ChatController.UpdateChannel), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatController.AddMembers), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatController.RemoveMember), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatController.SetNotificationPreference), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatController.EditMessage), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatController.RemoveReaction), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatController.Ack), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatController.MarkRead), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatController.PinMessage), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatController.UnpinMessage), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatController.ArchiveChannel), ResgridResources.Messages_Delete)]
		[TestCase(nameof(ChatController.DeleteMessage), ResgridResources.Messages_Delete)]
		public void mutating_chat_actions_require_the_corresponding_message_claim(string methodName, string policy)
		{
			var method = typeof(ChatController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);

			method.Should().NotBeNull();
			method.GetCustomAttributes<AuthorizeAttribute>().Should().ContainSingle(x => x.Policy == policy);
		}

		[TestCase(nameof(ChatModerationController.ResolveFlag), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatModerationController.MuteUser), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatModerationController.BanUser), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatModerationController.LockChannel), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatModerationController.UpdateSettings), ResgridResources.Messages_Update)]
		[TestCase(nameof(ChatModerationController.DeleteMessage), ResgridResources.Messages_Delete)]
		public void mutating_chat_moderation_actions_require_the_corresponding_message_claim(string methodName, string policy)
		{
			var method = typeof(ChatModerationController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);

			method.Should().NotBeNull();
			method.GetCustomAttributes<AuthorizeAttribute>().Should().ContainSingle(x => x.Policy == policy);
		}
	}
}
