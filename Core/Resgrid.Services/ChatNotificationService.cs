using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
	/// </summary>
	public class ChatNotificationService : IChatNotificationService
	{
		private readonly IChatPermissionService _chatPermissionService;
		private readonly IChatChannelService _chatChannelService;
		private readonly IChatChannelMemberRepository _chatChannelMemberRepository;
		private readonly IPushService _pushService;
		private readonly IDepartmentsService _departmentsService;

		public ChatNotificationService(IChatPermissionService chatPermissionService, IChatChannelService chatChannelService,
			IChatChannelMemberRepository chatChannelMemberRepository, IPushService pushService, IDepartmentsService departmentsService)
		{
			_chatPermissionService = chatPermissionService;
			_chatChannelService = chatChannelService;
			_chatChannelMemberRepository = chatChannelMemberRepository;
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

			var isDm = channel.ChannelType == (int)ChatChannelType.DirectMessage;
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

			foreach (var userId in audience)
			{
				if (string.Equals(userId, message.SenderUserId, StringComparison.OrdinalIgnoreCase))
					continue;

				membersByUser.TryGetValue(userId, out var member);

				if (!ShouldNotify(member, isUrgent, urgentOverridesMute, mentionedEveryone || mentionedUsers.Contains(userId)))
					continue;

				var unread = (int)Math.Max(0, channel.LastMessageSeq - (member?.LastReadSeq ?? 0));

				await _pushService.PushChatMessage(pushMessage, userId, eventCode, Math.Max(unread, 1));
			}

			// Unit participants (DM to "Engine 6", unit invited to a group chat): alert the rig device.
			foreach (var unitMember in memberRows.Where(m => m.ParticipantType == (int)ChatParticipantType.Unit && m.UnitId.HasValue && !m.RemovedOn.HasValue && !m.IsBanned))
			{
				if (message.SenderUnitId.HasValue && message.SenderUnitId.Value == unitMember.UnitId.Value)
					continue;

				if (!ShouldNotify(unitMember, isUrgent, urgentOverridesMute, mentionedEveryone))
					continue;

				var unread = (int)Math.Max(0, channel.LastMessageSeq - unitMember.LastReadSeq);

				await _pushService.PushChatMessageUnit(pushMessage, unitMember.UnitId.Value, eventCode, Math.Max(unread, 1));
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
