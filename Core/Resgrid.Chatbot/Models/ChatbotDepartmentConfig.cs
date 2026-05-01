using System;

namespace Resgrid.Chatbot.Models
{
	public class ChatbotDepartmentConfig
	{
		public string Id { get; set; }
		public int DepartmentId { get; set; }
		public bool IsEnabled { get; set; }
		public string NluProvider { get; set; } = "keyword";
		public string AllowedPlatforms { get; set; } = "*";
		public int MaxSessionsPerUser { get; set; } = 3;
		public int SessionTtlMinutes { get; set; } = 30;
		public bool AllowDispatchViaChatbot { get; set; }
		public bool RequireConfirmationForStatusChange { get; set; }
		public DateTime CreatedAt { get; set; }
		public DateTime? UpdatedAt { get; set; }
	}
}
