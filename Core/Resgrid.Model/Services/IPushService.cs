using System;
using System.Threading.Tasks;
using Resgrid.Model.Messages;

namespace Resgrid.Model.Services
{
	public interface IPushService
	{
		Task<bool> PushMessage(StandardPushMessage message, string userId, UserProfile profile = null);
		Task<bool> PushCall(StandardPushCall call, string userId, UserProfile profile = null, DepartmentCallPriority priority = null);
		Task<bool> Register(PushUri pushUri);
		Task<bool> UnRegister(PushUri pushUri);
		void UnRegisterNotificationOnly(PushUri pushUri);
		Task<bool> PushNotification(StandardPushMessage message, string userId, UserProfile profile = null);
		Task<bool> PushICNotification(StandardPushMessage message, string userId, UserProfile profile = null);
		Task<bool> RegisterUnit(PushUri pushUri);
		Task<bool> UnRegisterUnit(PushUri pushUri);
		Task<bool> PushChat(StandardPushMessage message, string userId, UserProfile profile = null);
		Task<bool> PushCallUnit(StandardPushCall call, int unitId, DepartmentCallPriority priority = null);

		/// <summary>
		/// Realtime-chat push to a user's Responder app subscriber, and — only when
		/// <paramref name="includeIncidentCommandApp"/> is set — to their IC app subscriber as well.
		/// EventCode is the chat deep-link (t:{channelId} / g:{channelId}); unreadCount drives the app badge.
		/// The caller decides IC eligibility because it depends on the channel, not the user: see
		/// ChatNotificationService.
		/// </summary>
		Task<bool> PushChatMessage(StandardPushMessage message, string userId, string eventCode, int unreadCount,
			bool includeIncidentCommandApp, UserProfile profile = null);

		/// <summary>Realtime-chat push to a unit-device subscriber (Unit app on the rig).</summary>
		Task<bool> PushChatMessageUnit(StandardPushMessage message, int unitId, string eventCode, int unreadCount);
	}
}
