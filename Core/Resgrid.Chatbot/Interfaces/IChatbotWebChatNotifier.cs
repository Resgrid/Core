using System.Threading.Tasks;

namespace Resgrid.Chatbot.Interfaces
{
	/// <summary>
	/// Pushes a chatbot response to a connected Web Chat user's live session. The real implementation
	/// lives in the web layer (SignalR via the EventingHub, emitting a <c>ChatbotMessageReceived</c>
	/// event to the user's connection); a no-op default (<see cref="Resgrid.Chatbot.Services"/>) keeps the
	/// WebChat adapter resolvable in hosts that don't load the web SignalR layer. If the user has no live
	/// connection the push is a no-op and the outbound channel falls back to other transports.
	/// </summary>
	public interface IChatbotWebChatNotifier
	{
		Task PushToUserAsync(string userId, string text);

		/// <summary>
		/// Pushes to the user's chatbot channel in a specific (ingress-resolved) department. When
		/// <paramref name="departmentId"/> is &lt;= 0 the notifier falls back to resolving the user's
		/// first active department membership.
		/// </summary>
		Task PushToUserAsync(string userId, string text, int departmentId);
	}
}
