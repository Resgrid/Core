namespace Resgrid.Chatbot.Models
{
	/// <summary>
	/// A lightweight result of a department-scoped personnel name search. Used by personnel lookup,
	/// message recipient resolution, and inbound identity resolution so they share one matcher.
	/// </summary>
	public class ChatbotUserMatch
	{
		public string UserId { get; set; }
		public string FirstName { get; set; }
		public string LastName { get; set; }
		public string FullName { get; set; }
	}
}
