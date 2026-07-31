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
		/// Realtime-chat push to a user across the Responder and IC app subscribers. EventCode is the
		/// chat deep-link (t:{channelId} / g:{channelId}); unreadCount drives the app badge.
		/// </summary>
		Task<bool> PushChatMessage(StandardPushMessage message, string userId, string eventCode, int unreadCount, UserProfile profile = null);

		/// <summary>Realtime-chat push to a unit-device subscriber (Unit app on the rig).</summary>
		Task<bool> PushChatMessageUnit(StandardPushMessage message, int unitId, string eventCode, int unreadCount);
	}
}
