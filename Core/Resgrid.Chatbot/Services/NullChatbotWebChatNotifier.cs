using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;

namespace Resgrid.Chatbot.Services
{
	/// <summary>
	/// No-op Web Chat notifier. Registered as the default (with PreserveExistingDefaults) so the WebChat
	/// adapter always resolves an <see cref="IChatbotWebChatNotifier"/>; the real SignalR-backed
	/// implementation in the web layer overrides it when present.
	/// </summary>
	public class NullChatbotWebChatNotifier : IChatbotWebChatNotifier
	{
		public Task PushToUserAsync(string userId, string text) => Task.CompletedTask;
	}
}
