using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Interfaces
{
	public interface IChatbotUserIdentityService
	{
		Task<ChatbotUserIdentity> GetIdentityAsync(ChatbotPlatform platform, string platformUserId);
		Task<ChatbotUserIdentity> GetIdentityByPhoneAsync(string phoneNumber);
		Task<List<ChatbotUserIdentity>> GetUserIdentitiesAsync(string userId);
		Task<ChatbotUserIdentity> LinkUserAsync(string userId, ChatbotPlatform platform, string platformUserId, string platformUserName, string linkingMethod);
		Task<ChatbotUserIdentity> LinkUserAsync(string userId, ChatbotPlatform platform, string platformUserId, string platformUserName, string linkingMethod, string linkingCode);
		Task UnlinkUserAsync(string identityId);
		Task RemoveLinkAsync(string userId, ChatbotPlatform platform);
		Task<bool> IsUserLinkedAsync(string userId, ChatbotPlatform platform);
	}
}
