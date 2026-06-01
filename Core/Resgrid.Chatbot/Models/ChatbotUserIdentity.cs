using System;

namespace Resgrid.Chatbot.Models
{
	public class ChatbotUserIdentity
	{
		public string Id { get; set; }
		public string UserId { get; set; }
		public ChatbotPlatform Platform { get; set; }
		public string PlatformUserId { get; set; }
		public string PlatformUserName { get; set; }
		public bool IsActive { get; set; }
		public DateTime CreatedAt { get; set; }
		public DateTime? LastUsedAt { get; set; }
		public string LinkingMethod { get; set; }
		public string LinkingCode { get; set; }

		// Alias for Id used in OAuthLinkingService
		public string IdentityId { get => Id; set => Id = value; }
	}
}
