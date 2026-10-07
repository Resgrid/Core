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
		Task UnRegisterNotificationOnly(PushUri pushUri);
		Task<bool> PushNotification(StandardPushMessage message, string userId, UserProfile profile = null);
		Task<bool> PushICNotification(StandardPushMessage message, string userId, UserProfile profile = null);
		Task<bool> RegisterUnit(PushUri pushUri);
		Task<bool> UnRegisterUnit(PushUri pushUri);

		/// <summary>
		/// Takes a browser or desktop token (PushUri.DeviceId) off a user's web push channel under the
		/// PushLocation department code; Source "IC" targets the IC app's subscriber. Unlike UnRegister this
		/// acts at once rather than through the queue: it runs as the person signs out of that browser.
		/// </summary>
		Task<bool> UnRegisterWebPush(PushUri pushUri);

		/// <summary>Takes a browser or desktop token off a unit's web push channel (PushUri.UnitId, PushLocation, DeviceId).</summary>
		Task<bool> UnRegisterUnitWebPush(PushUri pushUri);
		Task<bool> PushChat(StandardPushMessage message, string userId, UserProfile profile = null);
		Task<bool> PushCallUnit(StandardPushCall call, int unitId, DepartmentCallPriority priority = null);

		/// <summary>
		/// Pushes an informational notice to a unit's device with the notification sound, never a dispatch tone.
		/// The event code comes from <see cref="StandardPushMessage.Id"/> and should lead with "N" so the push is sent
		/// as an ordinary notification rather than a critical call alert.
		/// </summary>
		Task<bool> PushNotificationUnit(StandardPushMessage message, int unitId);

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
