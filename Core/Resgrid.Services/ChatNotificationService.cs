using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Messages;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Chat push fan-out. The only place chat pushes originate, so preference/mention/urgent rules are
	/// enforced exactly once: Muted suppresses everything except urgent (when the department allows the
	/// override), MentionsOnly requires a direct/@everyone mention or an urgent message, Default/All
	/// always notify. Unit participants additionally alert the unit-device subscriber ("Engine 6" rig).
	/// Users currently online are suppressed (they receive the message over SignalR instead).
	/// </summary>
	public class ChatNotificationService : IChatNotificationService
	{
		private const int MaxConcurrentPushes = 8;

		private readonly IChatPermissionService _chatPermissionService;
		private readonly IChatChannelService _chatChannelService;
		private readonly IChatChannelMemberRepository _chatChannelMemberRepository;
		private readonly IChatPresenceService _chatPresenceService;
		private readonly IPushService _pushService;
		private readonly IDepartmentsService _departmentsService;

		public ChatNotificationService(IChatPermissionService chatPermissionService, IChatChannelService chatChannelService,
			IChatChannelMemberRepository chatChannelMemberRepository, IChatPresenceService chatPresenceService,
			IPushService pushService, IDepartmentsService departmentsService)
		{
			_chatPermissionService = chatPermissionService;
			_chatChannelService = chatChannelService;
			_chatChannelMemberRepository = chatChannelMemberRepository;
			_chatPresenceService = chatPresenceService;
			_pushService = pushService;
			_departmentsService = departmentsService;
		}

		public async Task NotifyMessageSentAsync(ChatChannel channel, ChatMessage message, List<ChatMessageMention> mentions)
		{
			if (channel == null || message == null)
				return;

			// The bot channel never pushes for the bot's own replies through this path; the chatbot
			// pipeline decides its own notification behavior.
			if (channel.ChannelType == (int)ChatChannelType.Chatbot && message.SenderParticipantType == (int)ChatParticipantType.Bot)
				return;

			var department = await _departmentsService.GetDepartmentByIdAsync(channel.DepartmentId);
			if (department == null)
				return;

			var settings = await _chatChannelService.GetDepartmentSettingsAsync(channel.DepartmentId);
			var isUrgent = message.Priority == (int)ChatMessagePriority.Urgent;
			var urgentOverridesMute = settings == null || settings.UrgentOverridesMute;

			var audience = await _chatPermissionService.ResolveChannelAudienceUserIdsAsync(channel);

			var memberRows = (await _chatChannelMemberRepository.GetByChannelIdAsync(channel.ChatChannelId))?.ToList() ?? new List<ChatChannelMember>();
			var membersByUser = memberRows
				.Where(m => m.ParticipantType == (int)ChatParticipantType.User && !string.IsNullOrWhiteSpace(m.UserId))
				.GroupBy(m => m.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

			var mentionedEveryone = mentions != null && mentions.Any(m => m.MentionType == (int)ChatMentionType.Everyone);
			var mentionedUsers = new HashSet<string>(
				mentions?.Where(m => m.MentionType == (int)ChatMentionType.User && !string.IsNullOrWhiteSpace(m.TargetUserId)).Select(m => m.TargetUserId) ?? Enumerable.Empty<string>(),
				StringComparer.OrdinalIgnoreCase);

			// Active-channel suppression: only viewers with THIS conversation open are skipped — they see
			// the message live over SignalR. Online-but-elsewhere users still get a push so background
			// channels can alert them.
			var activeUsers = new HashSet<string>(
				await _chatPresenceService.GetUsersActiveInChannelAsync(channel.DepartmentId, audience, channel.ChatChannelId),
				StringComparer.OrdinalIgnoreCase);

			// The commander line is one-to-one, so it carries the DM deep-link prefix ("t:") rather than the
			// group one — the apps route it to a conversation view, not a channel view.
			var isDm = channel.ChannelType == (int)ChatChannelType.DirectMessage
				|| channel.ChannelType == (int)ChatChannelType.IncidentCommanderLine;
			var channelType = (ChatChannelType)channel.ChannelType;
			var notifyIncidentCommandApp = ShouldNotifyIncidentCommandApp(channelType);
			var notifyUnitApp = ShouldNotifyUnitApp(channelType);
			var eventCode = $"{(isDm ? "t" : "g")}:{channel.ChatChannelId}";
			var title = BuildTitle(channel, message, isDm, isUrgent);
			var body = BuildPreview(message);

			var pushMessage = new StandardPushMessage
			{
				Title = title,
				SubTitle = body,
				Id = eventCode,
				DepartmentId = channel.DepartmentId,
				DepartmentCode = department.Code
			};

			using (var throttler = new SemaphoreSlim(MaxConcurrentPushes))
			{
				var pushes = new List<Task>();
				var suppressedActive = 0;
				var suppressedPreference = 0;

				foreach (var userId in audience)
				{
					if (string.Equals(userId, message.SenderUserId, StringComparison.OrdinalIgnoreCase))
						continue;

					if (activeUsers.Contains(userId))
					{
						suppressedActive++;
						continue;
					}

					membersByUser.TryGetValue(userId, out var member);

					if (!ShouldNotify(member, isUrgent, urgentOverridesMute, mentionedEveryone || mentionedUsers.Contains(userId)))
					{
						suppressedPreference++;
						continue;
					}

					var unread = (int)Math.Max(0, channel.LastMessageSeq - (member?.LastReadSeq ?? 0));

					pushes.Add(SendThrottledAsync(throttler, () => _pushService.PushChatMessage(pushMessage, userId, eventCode, Math.Max(unread, 1), notifyIncidentCommandApp)));
				}

				// The fan-out runs detached from the request, so without this line an empty audience, a
				// stale active-channel marker and a channel full of muted members are indistinguishable
				// from "pushes were sent" when someone reports missing chat notifications.
				Logging.LogInfo($"Chat push fan-out for channel {channel.ChatChannelId} (type {channel.ChannelType}, event {eventCode}): audience {audience.Count}, queued {pushes.Count}, suppressed active {suppressedActive}, suppressed by preference {suppressedPreference}, IC app {notifyIncidentCommandApp}, unit app {notifyUnitApp}.");

				// Unit participants (DM to "Engine 6", unit invited to a group chat): alert the rig device,
				// but only for the conversations the rig owns — see ShouldNotifyUnitApp.
				foreach (var unitMember in notifyUnitApp
					? memberRows.Where(m => m.ParticipantType == (int)ChatParticipantType.Unit && m.UnitId.HasValue && !m.RemovedOn.HasValue && !m.IsBanned)
					: Enumerable.Empty<ChatChannelMember>())
				{
					if (message.SenderUnitId.HasValue && message.SenderUnitId.Value == unitMember.UnitId.Value)
						continue;

					if (await _chatPresenceService.IsUnitActiveInChannelAsync(channel.DepartmentId, unitMember.UnitId.Value, channel.ChatChannelId))
						continue;

					if (!ShouldNotify(unitMember, isUrgent, urgentOverridesMute, mentionedEveryone))
						continue;

					var unread = (int)Math.Max(0, channel.LastMessageSeq - unitMember.LastReadSeq);

					pushes.Add(SendThrottledAsync(throttler, () => _pushService.PushChatMessageUnit(pushMessage, unitMember.UnitId.Value, eventCode, Math.Max(unread, 1))));
				}

				await Task.WhenAll(pushes);
			}
		}

		/// <summary>
		/// Whether this channel's traffic may wake a recipient's IC app. The IC app is an incident device:
		/// it carries incident conversations only, so department-wide, station, ad-hoc, custom and peer
		/// chatter reaches the person on their Responder app instead of burying incident traffic on the
		/// device they are commanding from. A plain user-to-user DM stays off the IC app deliberately —
		/// "messaging Mike" and "messaging the IC" are different conversations, and the latter belongs to
		/// the command role rather than to whoever currently holds it.
		/// </summary>
		private static bool ShouldNotifyIncidentCommandApp(ChatChannelType channelType)
		{
			switch (channelType)
			{
				case ChatChannelType.Incident:
				case ChatChannelType.IncidentLane:
				case ChatChannelType.IncidentCommand:
				case ChatChannelType.IncidentLeads:
				case ChatChannelType.IncidentDispatch:
				case ChatChannelType.IncidentCommanderLine:
					return true;

				default:
					return false;
			}
		}

		/// <summary>
		/// Whether this channel's traffic may wake a unit's rig device. A unit is woken by its own standing
		/// dispatch line, by any incident channel it is working, and by a DM addressed to the unit identity
		/// ("Engine 6") — that last one from any sender, because a unit has no Responder app of its own and
		/// an unnotified DM would reach nobody. Department-wide, station, ad-hoc and custom channels are
		/// excluded: the rig can read and post in them, but those members are notified as users.
		/// </summary>
		private static bool ShouldNotifyUnitApp(ChatChannelType channelType)
		{
			switch (channelType)
			{
				case ChatChannelType.DirectMessage:
				case ChatChannelType.UnitDispatch:
				case ChatChannelType.Incident:
				case ChatChannelType.IncidentLane:
				case ChatChannelType.IncidentCommand:
				case ChatChannelType.IncidentLeads:
				case ChatChannelType.IncidentDispatch:
				case ChatChannelType.IncidentCommanderLine:
					return true;

				default:
					return false;
			}
		}

		/// <summary>Bounded-concurrency send: one recipient's failure is logged, never fails the fan-out.</summary>
		private static async Task SendThrottledAsync(SemaphoreSlim throttler, Func<Task> send)
		{
			await throttler.WaitAsync();
			try
			{
				await send();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
			finally
			{
				throttler.Release();
			}
		}

		private static bool ShouldNotify(ChatChannelMember member, bool isUrgent, bool urgentOverridesMute, bool isMentioned)
		{
			if (member != null && (member.IsBanned || member.RemovedOn.HasValue))
				return false;

			var preference = (ChatNotificationPreference)(member?.NotificationPreference ?? (int)ChatNotificationPreference.Default);

			switch (preference)
			{
				case ChatNotificationPreference.Muted:
					return isUrgent && urgentOverridesMute;

				case ChatNotificationPreference.MentionsOnly:
					return isMentioned || isUrgent;

				default:
					return true;
			}
		}

		private static string BuildTitle(ChatChannel channel, ChatMessage message, bool isDm, bool isUrgent)
		{
			var sender = string.IsNullOrWhiteSpace(message.SenderDisplayName) ? "New message" : message.SenderDisplayName;
			var title = isDm || string.IsNullOrWhiteSpace(channel.Name) ? sender : $"{sender} in {channel.Name}";

			return isUrgent ? $"URGENT: {title}" : title;
		}

		private static string BuildPreview(ChatMessage message)
		{
			switch ((ChatMessageType)message.MessageType)
			{
				case ChatMessageType.Image:
					return "Sent an image";
				case ChatMessageType.Gif:
					return "Sent a GIF";
				case ChatMessageType.Location:
					return "Shared a location";
				default:
					var body = message.Body ?? string.Empty;
					return body.Length <= 120 ? body : body.Substring(0, 117) + "...";
			}
		}
	}
}
