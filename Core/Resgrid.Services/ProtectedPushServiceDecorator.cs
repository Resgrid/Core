using System;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Messages;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Outbound-boundary net for push (ADP plan section 7.5, queue side). Every push the platform
	/// sends is composed into a <see cref="StandardPushMessage"/> or <see cref="StandardPushCall"/>
	/// and handed to <see cref="IPushService"/>, so scrubbing here covers dispatch, notifications
	/// and chat without touching each caller.
	///
	/// Push is the channel where a leak is most visible and least recoverable: the title and
	/// subtitle land on a lock screen, in a notification centre, and in the OS's own logs. It is
	/// also a channel that must not be blocked, so this scrubs and logs rather than refusing —
	/// the notification still wakes the responder, and the log names the path that skipped its
	/// protected projection.
	///
	/// Registration-side methods pass straight through; they carry no department content.
	/// </summary>
	public class ProtectedPushServiceDecorator : IPushService
	{
		private readonly IPushService _inner;

		public ProtectedPushServiceDecorator(IPushService inner)
		{
			_inner = inner;
		}

		public Task<bool> PushMessage(StandardPushMessage message, string userId, UserProfile profile = null)
			=> _inner.PushMessage(Sanitize(message, "message"), userId, profile);

		public Task<bool> PushCall(StandardPushCall call, string userId, UserProfile profile = null,
			DepartmentCallPriority priority = null)
			=> _inner.PushCall(Sanitize(call), userId, profile, priority);

		public Task<bool> PushNotification(StandardPushMessage message, string userId, UserProfile profile = null)
			=> _inner.PushNotification(Sanitize(message, "notification"), userId, profile);

		public Task<bool> PushICNotification(StandardPushMessage message, string userId, UserProfile profile = null)
			=> _inner.PushICNotification(Sanitize(message, "ic-notification"), userId, profile);

		public Task<bool> PushChat(StandardPushMessage message, string userId, UserProfile profile = null)
			=> _inner.PushChat(Sanitize(message, "chat"), userId, profile);

		public Task<bool> PushCallUnit(StandardPushCall call, int unitId, DepartmentCallPriority priority = null)
			=> _inner.PushCallUnit(Sanitize(call), unitId, priority);

		public Task<bool> PushChatMessage(StandardPushMessage message, string userId, string eventCode,
			int unreadCount, bool includeIncidentCommandApp, UserProfile profile = null)
			=> _inner.PushChatMessage(Sanitize(message, "chat-message"), userId, eventCode, unreadCount,
				includeIncidentCommandApp, profile);

		public Task<bool> PushChatMessageUnit(StandardPushMessage message, int unitId, string eventCode, int unreadCount)
			=> _inner.PushChatMessageUnit(Sanitize(message, "chat-message-unit"), unitId, eventCode, unreadCount);

		public Task<bool> Register(PushUri pushUri) => _inner.Register(pushUri);

		public Task<bool> UnRegister(PushUri pushUri) => _inner.UnRegister(pushUri);

		public void UnRegisterNotificationOnly(PushUri pushUri) => _inner.UnRegisterNotificationOnly(pushUri);

		public Task<bool> RegisterUnit(PushUri pushUri) => _inner.RegisterUnit(pushUri);

		public Task<bool> UnRegisterUnit(PushUri pushUri) => _inner.UnRegisterUnit(pushUri);

		private static StandardPushMessage Sanitize(StandardPushMessage message, string kind)
		{
			if (message == null)
				return null;

			try
			{
				var scrubbed = 0;

				message.Title = ProtectedOutboundGuard.Scrub(message.Title, out var titleCount);
				scrubbed += titleCount;

				message.SubTitle = ProtectedOutboundGuard.Scrub(message.SubTitle, out var subTitleCount);
				scrubbed += subTitleCount;

				if (scrubbed > 0)
					Logging.LogError($"ADP outbound net scrubbed {scrubbed} enveloped value(s) from a {kind} push for department " +
						$"{message.DepartmentId?.ToString() ?? "unknown"}. A notification path is missing its protected projection.");
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "ProtectedPushServiceDecorator failed while sanitizing a push message");
			}

			return message;
		}

		private static StandardPushCall Sanitize(StandardPushCall call)
		{
			if (call == null)
				return null;

			try
			{
				var scrubbed = 0;

				call.Title = ProtectedOutboundGuard.Scrub(call.Title, out var titleCount);
				scrubbed += titleCount;

				call.SubTitle = ProtectedOutboundGuard.Scrub(call.SubTitle, out var subTitleCount);
				scrubbed += subTitleCount;

				if (scrubbed > 0)
					Logging.LogError($"ADP outbound net scrubbed {scrubbed} enveloped value(s) from a dispatch push for department " +
						$"{call.DepartmentId?.ToString() ?? "unknown"} (call {call.CallId}). " +
						"A dispatch path is missing its protected projection.");
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "ProtectedPushServiceDecorator failed while sanitizing a dispatch push");
			}

			return call;
		}
	}
}
