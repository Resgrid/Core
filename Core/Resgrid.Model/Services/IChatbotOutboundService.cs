using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Delivers Resgrid outbound traffic (call dispatches, messages, notifications, reminders) to a
	/// user's linked chat platforms, as a sibling channel to SMS/Email/Push in
	/// <see cref="ICommunicationService"/>. The real implementation lives in the chatbot provider layer;
	/// a no-op default keeps CommunicationService resolvable in hosts that don't load the chatbot module.
	/// The interface lives in Resgrid.Model so CommunicationService (Resgrid.Services) can depend on it
	/// without referencing the provider layer.
	/// </summary>
	public interface IChatbotOutboundService
	{
		Task<ChatbotOutboundResult> SendToUserAsync(string userId, int departmentId, ChatbotOutboundMessage message);
	}
}
