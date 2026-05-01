using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Services
{
	/// <summary>
	/// OAuth linking service for Discord and Slack platforms.
	/// Users authenticate via OAuth2 and the external user ID is stored in ChatbotUserIdentities.
	/// </summary>
	public class OAuthLinkingService
	{
		private readonly IChatbotUserIdentityService _userIdentityService;

		public OAuthLinkingService(IChatbotUserIdentityService userIdentityService)
		{
			_userIdentityService = userIdentityService;
		}

		/// <summary>
		/// Links a Discord/Slack user to a Resgrid user after OAuth2 flow completes.
		/// </summary>
		public async Task<LinkResult> LinkViaOAuthAsync(
			string resgridUserId,
			ChatbotPlatform platform,
			string externalUserId,
			string externalUsername,
			string displayName)
		{
			if (string.IsNullOrWhiteSpace(resgridUserId) ||
				string.IsNullOrWhiteSpace(externalUserId))
				return LinkResult.Fail("Missing required parameters.");

			if (platform != ChatbotPlatform.Discord && platform != ChatbotPlatform.Slack)
				return LinkResult.Fail("OAuth linking is only supported for Discord and Slack.");

			var existing = await _userIdentityService.GetIdentityAsync(platform, externalUserId);
			if (existing != null)
			{
				if (existing.UserId == resgridUserId)
					return LinkResult.Ok("Account already linked.", existing);
				return LinkResult.Fail("This account is already linked to a different user.");
			}

			var identity = await _userIdentityService.LinkUserAsync(
				resgridUserId,
				platform,
				externalUserId,
				displayName,
				"oauth2");

			return LinkResult.Ok("Account linked successfully.", identity);
		}

		/// <summary>
		/// Unlinks a platform account from a Resgrid user.
		/// </summary>
		public async Task<LinkResult> UnlinkAsync(string resgridUserId, ChatbotPlatform platform, string externalUserId)
		{
			var identity = await _userIdentityService.GetIdentityAsync(platform, externalUserId);
			if (identity == null)
				return LinkResult.Fail("No linked account found.");

			if (identity.UserId != resgridUserId)
				return LinkResult.Fail("This account is linked to a different user.");

			await _userIdentityService.RemoveLinkAsync(resgridUserId, platform);
			return LinkResult.Ok("Account unlinked successfully.", null);
		}
	}

	public class LinkResult
	{
		public bool Success { get; set; }
		public string Message { get; set; }
		public ChatbotUserIdentity Identity { get; set; }

		public static LinkResult Ok(string message, ChatbotUserIdentity identity) =>
			new LinkResult { Success = true, Message = message, Identity = identity };

		public static LinkResult Fail(string message) =>
			new LinkResult { Success = false, Message = message };
	}
}
