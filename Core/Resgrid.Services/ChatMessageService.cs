using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommonServiceLocator;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Chat message pipeline: REST-first writes with client idempotency, per-channel sequence allocation,
	/// mentions, urgent acknowledgments, tombstone deletes with audit history, reactions, pins, read
	/// pointers and search. Emits ChatEventRaised envelopes that the eventing host relays over SignalR.
	/// </summary>
	public class ChatMessageService : IChatMessageService
	{
		private readonly IChatChannelRepository _chatChannelRepository;
		private readonly IChatMessageRepository _chatMessageRepository;
		private readonly IChatMessageEditRepository _chatMessageEditRepository;
		private readonly IChatAttachmentRepository _chatAttachmentRepository;
		private readonly IChatMessageReactionRepository _chatMessageReactionRepository;
		private readonly IChatMessageMentionRepository _chatMessageMentionRepository;
		private readonly IChatMessageAckRepository _chatMessageAckRepository;
		private readonly IChatChannelMemberRepository _chatChannelMemberRepository;
		private readonly IChatChannelService _chatChannelService;
		private readonly IChatPermissionService _chatPermissionService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUnitsService _unitsService;
		private readonly IEventAggregator _eventAggregator;

		public ChatMessageService(IChatChannelRepository chatChannelRepository, IChatMessageRepository chatMessageRepository,
			IChatMessageEditRepository chatMessageEditRepository, IChatAttachmentRepository chatAttachmentRepository,
			IChatMessageReactionRepository chatMessageReactionRepository, IChatMessageMentionRepository chatMessageMentionRepository,
			IChatMessageAckRepository chatMessageAckRepository, IChatChannelMemberRepository chatChannelMemberRepository,
			IChatChannelService chatChannelService, IChatPermissionService chatPermissionService, IUserProfileService userProfileService,
			IUnitsService unitsService, IEventAggregator eventAggregator)
		{
			_chatChannelRepository = chatChannelRepository;
			_chatMessageRepository = chatMessageRepository;
			_chatMessageEditRepository = chatMessageEditRepository;
			_chatAttachmentRepository = chatAttachmentRepository;
			_chatMessageReactionRepository = chatMessageReactionRepository;
			_chatMessageMentionRepository = chatMessageMentionRepository;
			_chatMessageAckRepository = chatMessageAckRepository;
			_chatChannelMemberRepository = chatChannelMemberRepository;
			_chatChannelService = chatChannelService;
			_chatPermissionService = chatPermissionService;
			_userProfileService = userProfileService;
			_unitsService = unitsService;
			_eventAggregator = eventAggregator;
		}

		public async Task<ChatMessage> SendMessageAsync(ChatMessageSendRequest request, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (request == null || string.IsNullOrWhiteSpace(request.ChatChannelId))
				return null;

			var channel = await _chatChannelRepository.GetByIdAsync(request.ChatChannelId);
			if (channel == null || channel.DepartmentId != request.DepartmentId)
				return null;

			// Idempotent resend from the mobile offline outbox.
			if (!string.IsNullOrWhiteSpace(request.ClientMessageId) && !string.IsNullOrWhiteSpace(request.SenderUserId))
			{
				var existing = await _chatMessageRepository.GetByClientMessageIdAsync(channel.ChatChannelId, request.SenderUserId, request.ClientMessageId);
				if (existing != null)
					return existing;
			}

			if (!request.AsBot)
			{
				if (string.IsNullOrWhiteSpace(request.SenderUserId))
					return null;

				if (!await _chatPermissionService.CanPostAsync(channel, request.SenderUserId, request.AsUnitId))
					return null;

				if (request.AsIncidentCommander &&
					(!channel.CallId.HasValue || !await _chatPermissionService.CanSendAsIcAsync(request.SenderUserId, channel.CallId.Value, channel.DepartmentId)))
					return null;
			}

			var settings = await _chatChannelService.GetDepartmentSettingsAsync(channel.DepartmentId);
			if (!ValidateContent(request, settings))
				return null;

			ChatMessage threadRoot = null;
			if (!string.IsNullOrWhiteSpace(request.ThreadRootMessageId))
			{
				threadRoot = await _chatMessageRepository.GetByIdAsync(request.ThreadRootMessageId);
				if (threadRoot == null || threadRoot.ChatChannelId != channel.ChatChannelId || !string.IsNullOrWhiteSpace(threadRoot.ThreadRootMessageId))
					return null;
			}

			var senderDisplayName = await ResolveSenderDisplayNameAsync(request, channel);

			var seq = await _chatChannelRepository.AllocateNextMessageSeqAsync(channel.ChatChannelId, DateTime.UtcNow);

			var message = new ChatMessage
			{
				ChatMessageId = Guid.NewGuid().ToString(),
				ChatChannelId = channel.ChatChannelId,
				DepartmentId = channel.DepartmentId,
				MessageSeq = seq,
				SenderParticipantType = request.AsBot ? (int)ChatParticipantType.Bot : (request.AsUnitId.HasValue ? (int)ChatParticipantType.Unit : (int)ChatParticipantType.User),
				SenderUserId = request.SenderUserId,
				SenderUnitId = request.AsUnitId,
				SenderDisplayName = senderDisplayName,
				Body = request.Body,
				MessageType = (int)request.MessageType,
				Priority = (int)request.Priority,
				ThreadRootMessageId = request.ThreadRootMessageId,
				AlsoSendToChannel = request.AlsoSendToChannel,
				MetadataJson = request.MetadataJson,
				ClientMessageId = request.ClientMessageId,
				SentOn = DateTime.UtcNow
			};

			try
			{
				await _chatMessageRepository.InsertAsync(message, cancellationToken);
			}
			catch (Exception)
			{
				// Unique (Channel, Sender, ClientMessageId) index backstops concurrent resends.
				if (!string.IsNullOrWhiteSpace(request.ClientMessageId) && !string.IsNullOrWhiteSpace(request.SenderUserId))
				{
					var winner = await _chatMessageRepository.GetByClientMessageIdAsync(channel.ChatChannelId, request.SenderUserId, request.ClientMessageId);
					if (winner != null)
						return winner;
				}

				throw;
			}

			if (threadRoot != null)
			{
				await _chatMessageRepository.IncrementThreadReplyAsync(threadRoot.ChatMessageId, message.SentOn);
				PublishEvent(channel, ChatEventKinds.ThreadUpdated, new
				{
					threadRoot.ChatMessageId,
					ThreadReplyCount = threadRoot.ThreadReplyCount + 1,
					LastThreadReplyOn = message.SentOn
				});
			}

			await SaveMentionsAsync(request, message, cancellationToken);

			if (message.Priority == (int)ChatMessagePriority.Urgent)
				await ProvisionAcksAsync(channel, message, cancellationToken);

			// The sender has obviously read their own message.
			if (!request.AsBot && !string.IsNullOrWhiteSpace(request.SenderUserId))
			{
				var member = await _chatChannelService.EnsureMemberStateAsync(channel.ChatChannelId, channel.DepartmentId, request.SenderUserId, request.AsUnitId, cancellationToken);
				if (member != null)
					await AdvancePointersAsync(member, seq, markRead: true);
			}

			PublishEvent(channel, ChatEventKinds.MessageReceived, BuildMessageDto(message));

			// Push fan-out off the request path: per-recipient Novu calls can be slow for large channels,
			// and a push failure must never fail the send. Fresh resolution inside the task keeps us off
			// the request's disposed lifetime scope (ChatProvisioningEventService pattern).
			var mentionsForPush = request.Mentions;
			_ = Task.Run(async () =>
			{
				try
				{
					var notifier = ServiceLocator.Current.GetInstance<IChatNotificationService>();
					await notifier.NotifyMessageSentAsync(channel, message, mentionsForPush);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			});

			return message;
		}

		public async Task<ChatMessage> GetMessageByIdAsync(string chatMessageId)
		{
			return await _chatMessageRepository.GetByIdAsync(chatMessageId);
		}

		public async Task<List<ChatMessage>> GetMessagesPageAsync(string chatChannelId, long? beforeSeq, int limit)
		{
			var messages = await _chatMessageRepository.GetPageAsync(chatChannelId, beforeSeq, NormalizeLimit(limit));
			return messages?.ToList() ?? new List<ChatMessage>();
		}

		public async Task<List<ChatMessage>> GetMessagesAfterAsync(string chatChannelId, long afterSeq, int limit)
		{
			var messages = await _chatMessageRepository.GetAfterSeqAsync(chatChannelId, afterSeq, NormalizeLimit(limit));
			return messages?.ToList() ?? new List<ChatMessage>();
		}

		public async Task<List<ChatMessage>> GetThreadPageAsync(string threadRootMessageId, long? beforeSeq, int limit)
		{
			var messages = await _chatMessageRepository.GetThreadPageAsync(threadRootMessageId, beforeSeq, NormalizeLimit(limit));
			return messages?.ToList() ?? new List<ChatMessage>();
		}

		public async Task<ChatMessage> EditMessageAsync(string chatMessageId, string editorUserId, string newBody, CancellationToken cancellationToken = default(CancellationToken))
		{
			var message = await _chatMessageRepository.GetByIdAsync(chatMessageId);
			if (message == null || message.DeletedOn.HasValue)
				return null;

			if (!string.Equals(message.SenderUserId, editorUserId, StringComparison.OrdinalIgnoreCase))
				return null;

			if (string.IsNullOrWhiteSpace(newBody) || newBody.Length > ChatConfig.MaxMessageLength)
				return null;

			await SaveEditHistoryAsync(message, ChatMessageEditType.Edit, editorUserId, cancellationToken);

			message.Body = newBody;
			message.EditedOn = DateTime.UtcNow;

			var saved = await _chatMessageRepository.UpdateAsync(message, cancellationToken);

			var channel = await _chatChannelRepository.GetByIdAsync(message.ChatChannelId);
			PublishEvent(channel, ChatEventKinds.MessageEdited, BuildMessageDto(saved));

			return saved;
		}

		public async Task<bool> DeleteMessageAsync(string chatMessageId, string byUserId, bool asModerator, string reason, CancellationToken cancellationToken = default(CancellationToken))
		{
			var message = await _chatMessageRepository.GetByIdAsync(chatMessageId);
			if (message == null || message.DeletedOn.HasValue)
				return false;

			var isSender = string.Equals(message.SenderUserId, byUserId, StringComparison.OrdinalIgnoreCase);
			if (!isSender && !asModerator)
				return false;

			await SaveEditHistoryAsync(message, asModerator && !isSender ? ChatMessageEditType.ModeratorDelete : ChatMessageEditType.SenderDelete, byUserId, cancellationToken);

			message.Body = null;
			message.MetadataJson = null;
			message.DeletedOn = DateTime.UtcNow;
			message.DeletedByUserId = byUserId;

			await _chatMessageRepository.UpdateAsync(message, cancellationToken);

			var channel = await _chatChannelRepository.GetByIdAsync(message.ChatChannelId);
			PublishEvent(channel, ChatEventKinds.MessageDeleted, new
			{
				message.ChatMessageId,
				message.ChatChannelId,
				message.MessageSeq,
				message.DeletedOn,
				DeletedByModerator = asModerator && !isSender
			});

			return true;
		}

		public async Task<bool> AddReactionAsync(string chatMessageId, string userId, int? unitId, string emoji, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (string.IsNullOrWhiteSpace(emoji) || emoji.Length > 64)
				return false;

			var message = await _chatMessageRepository.GetByIdAsync(chatMessageId);
			if (message == null || message.DeletedOn.HasValue)
				return false;

			try
			{
				await _chatMessageReactionRepository.InsertAsync(new ChatMessageReaction
				{
					ChatMessageReactionId = Guid.NewGuid().ToString(),
					ChatMessageId = chatMessageId,
					ChatChannelId = message.ChatChannelId,
					DepartmentId = message.DepartmentId,
					ParticipantType = unitId.HasValue ? (int)ChatParticipantType.Unit : (int)ChatParticipantType.User,
					UserId = unitId.HasValue ? null : userId,
					UnitId = unitId,
					Emoji = emoji,
					ReactedOn = DateTime.UtcNow
				}, cancellationToken);
			}
			catch (Exception)
			{
				// Unique index: reacting twice with the same emoji is a no-op.
				return true;
			}

			var channel = await _chatChannelRepository.GetByIdAsync(message.ChatChannelId);
			PublishEvent(channel, ChatEventKinds.ReactionUpdated, new { message.ChatMessageId, message.ChatChannelId, Emoji = emoji, UserId = userId, UnitId = unitId, Added = true });

			return true;
		}

		public async Task<bool> RemoveReactionAsync(string chatMessageId, string userId, int? unitId, string emoji, CancellationToken cancellationToken = default(CancellationToken))
		{
			var message = await _chatMessageRepository.GetByIdAsync(chatMessageId);
			if (message == null)
				return false;

			var participantType = unitId.HasValue ? (int)ChatParticipantType.Unit : (int)ChatParticipantType.User;
			var removed = await _chatMessageReactionRepository.DeleteReactionAsync(chatMessageId, participantType, unitId.HasValue ? null : userId, unitId, emoji, cancellationToken);

			if (removed)
			{
				var channel = await _chatChannelRepository.GetByIdAsync(message.ChatChannelId);
				PublishEvent(channel, ChatEventKinds.ReactionUpdated, new { message.ChatMessageId, message.ChatChannelId, Emoji = emoji, UserId = userId, UnitId = unitId, Added = false });
			}

			return removed;
		}

		public async Task<List<ChatMessageReaction>> GetReactionsForMessagesAsync(List<string> chatMessageIds)
		{
			var reactions = await _chatMessageReactionRepository.GetByMessageIdsAsync(chatMessageIds ?? new List<string>());
			return reactions?.ToList() ?? new List<ChatMessageReaction>();
		}

		public async Task<List<ChatAttachment>> GetAttachmentMetadataForMessagesAsync(List<string> chatMessageIds)
		{
			var attachments = await _chatAttachmentRepository.GetMetadataByMessageIdsAsync(chatMessageIds ?? new List<string>());
			return attachments?.ToList() ?? new List<ChatAttachment>();
		}

		public async Task<bool> SetMessagePinnedAsync(string chatMessageId, string byUserId, bool pinned, CancellationToken cancellationToken = default(CancellationToken))
		{
			var message = await _chatMessageRepository.GetByIdAsync(chatMessageId);
			if (message == null || message.DeletedOn.HasValue)
				return false;

			message.PinnedOn = pinned ? DateTime.UtcNow : (DateTime?)null;
			message.PinnedByUserId = pinned ? byUserId : null;

			await _chatMessageRepository.UpdateAsync(message, cancellationToken);

			var channel = await _chatChannelRepository.GetByIdAsync(message.ChatChannelId);
			PublishEvent(channel, ChatEventKinds.ChannelUpdated, new { message.ChatChannelId, PinnedMessageId = message.ChatMessageId, Pinned = pinned });

			return true;
		}

		public async Task<List<ChatMessage>> GetPinnedMessagesAsync(string chatChannelId)
		{
			var pinned = await _chatMessageRepository.GetPinnedByChannelIdAsync(chatChannelId);
			return pinned?.ToList() ?? new List<ChatMessage>();
		}

		public async Task<int> AcknowledgeMessageAsync(string chatMessageId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var stamped = await _chatMessageAckRepository.AcknowledgeAsync(chatMessageId, userId, DateTime.UtcNow);

			if (stamped > 0)
			{
				var message = await _chatMessageRepository.GetByIdAsync(chatMessageId);
				if (message != null)
				{
					var channel = await _chatChannelRepository.GetByIdAsync(message.ChatChannelId);
					PublishEvent(channel, ChatEventKinds.ReceiptUpdated, new { message.ChatMessageId, message.ChatChannelId, Type = "ack", UserId = userId });
				}
			}

			return stamped;
		}

		public async Task<List<ChatMessageAck>> GetAcksForMessageAsync(string chatMessageId)
		{
			var acks = await _chatMessageAckRepository.GetByMessageIdAsync(chatMessageId);
			return acks?.ToList() ?? new List<ChatMessageAck>();
		}

		public async Task<List<ChatMessageAck>> GetPendingAcksForUserAsync(int departmentId, string userId)
		{
			var acks = await _chatMessageAckRepository.GetPendingByUserIdAsync(departmentId, userId);
			return acks?.ToList() ?? new List<ChatMessageAck>();
		}

		public async Task<bool> MarkReadAsync(string chatChannelId, int departmentId, string userId, int? unitId, long seq, CancellationToken cancellationToken = default(CancellationToken))
		{
			var member = await _chatChannelService.EnsureMemberStateAsync(chatChannelId, departmentId, userId, unitId, cancellationToken);
			if (member == null)
				return false;

			var advanced = await AdvancePointersAsync(member, seq, markRead: true);

			if (advanced)
			{
				var channel = await _chatChannelRepository.GetByIdAsync(chatChannelId);
				PublishEvent(channel, ChatEventKinds.ReceiptUpdated, new { ChatChannelId = chatChannelId, Type = "read", UserId = userId, UnitId = unitId, Seq = seq });
			}

			return advanced;
		}

		public async Task<bool> MarkDeliveredAsync(string chatChannelId, int departmentId, string userId, int? unitId, long seq, CancellationToken cancellationToken = default(CancellationToken))
		{
			var member = await _chatChannelService.EnsureMemberStateAsync(chatChannelId, departmentId, userId, unitId, cancellationToken);
			if (member == null)
				return false;

			return await AdvancePointersAsync(member, seq, markRead: false);
		}

		public async Task<List<ChatMessage>> SearchAsync(int departmentId, string userId, int? activeUnitId, string query, string chatChannelId, DateTime? from, DateTime? to, int page, int pageSize)
		{
			if (string.IsNullOrWhiteSpace(query))
				return new List<ChatMessage>();

			List<string> channelIds;
			if (!string.IsNullOrWhiteSpace(chatChannelId))
			{
				var channel = await _chatChannelRepository.GetByIdAsync(chatChannelId);
				if (channel == null || !await _chatPermissionService.CanAccessChannelAsync(channel, userId, activeUnitId))
					return new List<ChatMessage>();

				channelIds = new List<string> { chatChannelId };
			}
			else
			{
				var channels = await _chatChannelService.GetChannelsForUserAsync(departmentId, userId, activeUnitId, includeArchived: true);
				channelIds = channels.Select(c => c.ChatChannelId).ToList();
			}

			if (channelIds.Count == 0)
				return new List<ChatMessage>();

			var results = await _chatMessageRepository.SearchAsync(departmentId, channelIds, query, from, to, Math.Max(page, 0), pageSize <= 0 ? 25 : Math.Min(pageSize, 100));
			return results?.ToList() ?? new List<ChatMessage>();
		}

		private async Task<bool> AdvancePointersAsync(ChatChannelMember member, long seq, bool markRead)
		{
			var deliveredAdvanced = await _chatChannelMemberRepository.AdvanceDeliveredPointerAsync(member.ChatChannelMemberId, seq);

			if (!markRead)
				return deliveredAdvanced;

			return await _chatChannelMemberRepository.AdvanceReadPointerAsync(member.ChatChannelMemberId, seq, DateTime.UtcNow);
		}

		private bool ValidateContent(ChatMessageSendRequest request, ChatDepartmentSetting settings)
		{
			if (string.IsNullOrWhiteSpace(request.Body) && request.MessageType == ChatMessageType.Text)
				return false;

			if (!string.IsNullOrWhiteSpace(request.Body) && request.Body.Length > ChatConfig.MaxMessageLength)
				return false;

			switch (request.MessageType)
			{
				case ChatMessageType.Image:
					return settings == null || settings.AllowImages;
				case ChatMessageType.Gif:
					return settings == null || settings.AllowGifs;
				case ChatMessageType.Location:
					return settings == null || settings.AllowLocationSharing;
				default:
					return true;
			}
		}

		private async Task<string> ResolveSenderDisplayNameAsync(ChatMessageSendRequest request, ChatChannel channel)
		{
			if (!string.IsNullOrWhiteSpace(request.SenderDisplayName))
				return request.SenderDisplayName;

			if (request.AsBot)
				return "Resgrid Assistant";

			string profileName = null;
			var profile = await _userProfileService.GetProfileByUserIdAsync(request.SenderUserId);
			if (profile != null)
				profileName = $"{profile.FirstName} {profile.LastName}".Trim();

			if (request.AsUnitId.HasValue)
			{
				var unit = await _unitsService.GetUnitByIdAsync(request.AsUnitId.Value);
				return unit?.Name ?? profileName ?? "Unit";
			}

			if (request.AsIncidentCommander)
				return string.IsNullOrWhiteSpace(profileName) ? "Incident Commander" : $"Incident Commander ({profileName})";

			return string.IsNullOrWhiteSpace(profileName) ? "Unknown" : profileName;
		}

		private async Task SaveMentionsAsync(ChatMessageSendRequest request, ChatMessage message, CancellationToken cancellationToken)
		{
			if (request.Mentions == null || request.Mentions.Count == 0)
				return;

			foreach (var mention in request.Mentions)
			{
				mention.ChatMessageMentionId = Guid.NewGuid().ToString();
				mention.ChatMessageId = message.ChatMessageId;
				mention.ChatChannelId = message.ChatChannelId;
				mention.DepartmentId = message.DepartmentId;

				await _chatMessageMentionRepository.InsertAsync(mention, cancellationToken);
			}
		}

		private async Task ProvisionAcksAsync(ChatChannel channel, ChatMessage message, CancellationToken cancellationToken)
		{
			var audience = await _chatPermissionService.ResolveChannelAudienceUserIdsAsync(channel);

			foreach (var userId in audience.Where(u => !string.Equals(u, message.SenderUserId, StringComparison.OrdinalIgnoreCase)))
			{
				await _chatMessageAckRepository.InsertAsync(new ChatMessageAck
				{
					ChatMessageAckId = Guid.NewGuid().ToString(),
					ChatMessageId = message.ChatMessageId,
					ChatChannelId = message.ChatChannelId,
					DepartmentId = message.DepartmentId,
					UserId = userId,
					RequiredOn = message.SentOn
				}, cancellationToken);
			}

			PublishEvent(channel, ChatEventKinds.AckRequired, new { message.ChatMessageId, message.ChatChannelId, message.MessageSeq, RequiredCount = audience.Count });
		}

		private async Task SaveEditHistoryAsync(ChatMessage message, ChatMessageEditType editType, string byUserId, CancellationToken cancellationToken)
		{
			await _chatMessageEditRepository.InsertAsync(new ChatMessageEdit
			{
				ChatMessageEditId = Guid.NewGuid().ToString(),
				ChatMessageId = message.ChatMessageId,
				ChatChannelId = message.ChatChannelId,
				DepartmentId = message.DepartmentId,
				PriorBody = message.Body,
				EditType = (int)editType,
				EditedByUserId = byUserId,
				EditedOn = DateTime.UtcNow
			}, cancellationToken);
		}

		private object BuildMessageDto(ChatMessage message)
		{
			return new
			{
				message.ChatMessageId,
				message.ChatChannelId,
				message.DepartmentId,
				message.MessageSeq,
				message.SenderParticipantType,
				message.SenderUserId,
				message.SenderUnitId,
				message.SenderDisplayName,
				message.Body,
				message.MessageType,
				message.Priority,
				message.ThreadRootMessageId,
				message.AlsoSendToChannel,
				message.MetadataJson,
				message.ClientMessageId,
				message.SentOn,
				message.EditedOn
			};
		}

		private void PublishEvent(ChatChannel channel, string kind, object payload)
		{
			if (channel == null)
				return;

			_eventAggregator.SendMessage<ChatEventRaised>(new ChatEventRaised
			{
				DepartmentId = channel.DepartmentId,
				ChatChannelId = channel.ChatChannelId,
				Kind = kind,
				PayloadJson = JsonConvert.SerializeObject(payload)
			});
		}

		private static int NormalizeLimit(int limit)
		{
			if (limit <= 0)
				return 50;

			return Math.Min(limit, 200);
		}
	}
}
