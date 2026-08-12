using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommonServiceLocator;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
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

		public async Task<ChatMessage> SendMessageAsync(string senderUserId, ChatMessageSendRequest request, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (request == null || string.IsNullOrWhiteSpace(request.ChatChannelId) || string.IsNullOrWhiteSpace(senderUserId))
				return null;

			var channel = await _chatChannelRepository.GetByIdAsync(request.ChatChannelId);
			if (channel == null || channel.DepartmentId != request.DepartmentId)
				return null;

			// Idempotent resend from the mobile offline outbox.
			if (!string.IsNullOrWhiteSpace(request.ClientMessageId))
			{
				var existing = await _chatMessageRepository.GetByClientMessageIdAsync(channel.ChatChannelId, senderUserId, request.ClientMessageId);
				if (existing != null)
					return existing;
			}

			if (!await _chatPermissionService.CanPostAsync(channel, senderUserId, request.AsUnitId))
				return null;

			if (request.AsIncidentCommander &&
				(!channel.CallId.HasValue || !await _chatPermissionService.CanSendAsIcAsync(senderUserId, channel.CallId.Value, channel.DepartmentId)))
				return null;

			var settings = await _chatChannelService.GetDepartmentSettingsAsync(channel.DepartmentId);
			if (!ValidateContent(request, settings))
				return null;

			// Urgent and @everyone are moderator-only: silently downgrade instead of failing the send.
			var priority = request.Priority;
			var mentions = request.Mentions;
			var requiresModerator = priority == ChatMessagePriority.Urgent ||
				(mentions != null && mentions.Any(m => m.MentionType == (int)ChatMentionType.Everyone));
			if (requiresModerator && !await _chatPermissionService.CanModerateChannelAsync(channel, senderUserId))
			{
				priority = ChatMessagePriority.Normal;
				if (mentions != null)
					mentions = mentions.Where(m => m.MentionType != (int)ChatMentionType.Everyone).ToList();
			}

			// User mentions must target a resolvable audience member; anything else is dropped.
			if (mentions != null && mentions.Any(m => m.MentionType == (int)ChatMentionType.User && !string.IsNullOrWhiteSpace(m.TargetUserId)))
			{
				var audience = new HashSet<string>(await _chatPermissionService.ResolveChannelAudienceUserIdsAsync(channel), StringComparer.OrdinalIgnoreCase);
				mentions = mentions
					.Where(m => m.MentionType != (int)ChatMentionType.User || (!string.IsNullOrWhiteSpace(m.TargetUserId) && audience.Contains(m.TargetUserId)))
					.ToList();
			}

			ChatMessage threadRoot = null;
			if (!string.IsNullOrWhiteSpace(request.ThreadRootMessageId))
			{
				threadRoot = await _chatMessageRepository.GetByIdAsync(request.ThreadRootMessageId);
				if (threadRoot == null || threadRoot.ChatChannelId != channel.ChatChannelId || !string.IsNullOrWhiteSpace(threadRoot.ThreadRootMessageId))
					return null;
			}

			var senderDisplayName = await ResolveSenderDisplayNameAsync(senderUserId, request.AsUnitId, request.AsIncidentCommander, false, null);

			var seq = await _chatChannelRepository.AllocateNextMessageSeqAsync(channel.ChatChannelId, DateTime.UtcNow);

			// Keep the in-memory channel current: notification badge counts read LastMessageSeq.
			channel.LastMessageSeq = seq;
			channel.LastMessageOn = DateTime.UtcNow;

			var message = new ChatMessage
			{
				ChatMessageId = Guid.NewGuid().ToString(),
				ChatChannelId = channel.ChatChannelId,
				DepartmentId = channel.DepartmentId,
				MessageSeq = seq,
				SenderParticipantType = request.AsUnitId.HasValue ? (int)ChatParticipantType.Unit : (int)ChatParticipantType.User,
				SenderUserId = senderUserId,
				SenderUnitId = request.AsUnitId,
				SenderDisplayName = senderDisplayName,
				Body = request.Body,
				MessageType = (int)request.MessageType,
				Priority = (int)priority,
				ThreadRootMessageId = request.ThreadRootMessageId,
				AlsoSendToChannel = request.AlsoSendToChannel,
				MetadataJson = ValidateMetadataJson(request.MessageType, request.MetadataJson),
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
				if (!string.IsNullOrWhiteSpace(request.ClientMessageId))
				{
					var winner = await _chatMessageRepository.GetByClientMessageIdAsync(channel.ChatChannelId, senderUserId, request.ClientMessageId);
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

			await SaveMentionsAsync(mentions, message, cancellationToken);

			if (message.Priority == (int)ChatMessagePriority.Urgent)
				await ProvisionAcksAsync(channel, message, cancellationToken);

			// The sender has obviously read their own message. A rule-based CustomLocked poster has no
			// membership row (EnsureMemberStateAsync won't self-grant one) — pointers just don't advance.
			ChatChannelMember member = null;
			try
			{
				member = await _chatChannelService.EnsureMemberStateAsync(channel.ChatChannelId, channel.DepartmentId, senderUserId, request.AsUnitId, cancellationToken);
			}
			catch (UnauthorizedAccessException)
			{
			}

			if (member != null)
				await AdvancePointersAsync(member, seq, markRead: true);

			PublishEvent(channel, ChatEventKinds.MessageReceived, BuildMessageDto(message));

			FireAndForgetNotify(channel, message, mentions);

			return message;
		}

		public async Task<ChatMessage> SendBotMessageAsync(string channelId, string departmentId, string body, string senderDisplayName, string metadataJson = null)
		{
			if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(body) || body.Length > ChatConfig.MaxMessageLength)
				return null;

			var channel = await _chatChannelRepository.GetByIdAsync(channelId);
			if (channel == null || !string.Equals(channel.DepartmentId.ToString(), departmentId, StringComparison.OrdinalIgnoreCase))
				return null;

			var seq = await _chatChannelRepository.AllocateNextMessageSeqAsync(channel.ChatChannelId, DateTime.UtcNow);
			channel.LastMessageSeq = seq;
			channel.LastMessageOn = DateTime.UtcNow;

			var message = new ChatMessage
			{
				ChatMessageId = Guid.NewGuid().ToString(),
				ChatChannelId = channel.ChatChannelId,
				DepartmentId = channel.DepartmentId,
				MessageSeq = seq,
				SenderParticipantType = (int)ChatParticipantType.Bot,
				SenderUserId = null,
				SenderDisplayName = string.IsNullOrWhiteSpace(senderDisplayName) ? "Resgrid Assistant" : senderDisplayName,
				Body = body,
				MessageType = (int)ChatMessageType.Bot,
				Priority = (int)ChatMessagePriority.Normal,
				MetadataJson = ValidateMetadataJson(ChatMessageType.Bot, metadataJson),
				SentOn = DateTime.UtcNow
			};

			await _chatMessageRepository.InsertAsync(message, CancellationToken.None);

			PublishEvent(channel, ChatEventKinds.MessageReceived, BuildMessageDto(message));

			FireAndForgetNotify(channel, message, null);

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

		/// <summary>
		/// True when the channel is archived, i.e. frozen as a point-in-time record: a closed incident
		/// command's channel and its lane channels, or a closed call's channel. Posting is already blocked
		/// by <c>IChatPermissionService.CanPostAsync</c>; this is the matching gate for mutating what is
		/// already there. Moderation (flagging, moderator delete) deliberately does NOT consult it.
		/// A missing channel reads as frozen — fail closed rather than allow an unanchored edit.
		/// </summary>
		private async Task<bool> IsChannelFrozenAsync(string chatChannelId)
		{
			if (string.IsNullOrWhiteSpace(chatChannelId))
				return true;

			var channel = await _chatChannelRepository.GetByIdAsync(chatChannelId);
			return channel == null || channel.IsArchived;
		}

		public async Task<ChatMessage> EditMessageAsync(string chatMessageId, string editorUserId, string newBody, CancellationToken cancellationToken = default(CancellationToken))
		{
			var message = await _chatMessageRepository.GetByIdAsync(chatMessageId);
			if (message == null || message.DeletedOn.HasValue)
				return null;

			// An archived channel is a point-in-time record (a closed incident command/lane chat, a closed
			// call). CanPostAsync already refuses new messages there; the history has to be just as
			// immutable, or the record could still be rewritten after the fact.
			if (await IsChannelFrozenAsync(message.ChatChannelId))
				return null;

			if (!string.Equals(message.SenderUserId, editorUserId, StringComparison.OrdinalIgnoreCase))
				return null;

			if (string.IsNullOrWhiteSpace(newBody) || newBody.Length > ChatConfig.MaxMessageLength)
				return null;

			await SaveEditHistoryAsync(message, ChatMessageEditType.Edit, editorUserId, cancellationToken);

			// Targeted update guarded by DeletedOn IS NULL: a concurrent tombstone wins, and the edit
			// never resurrects it or clobbers ThreadReplyCount increments.
			var editedOn = DateTime.UtcNow;
			if (!await _chatMessageRepository.UpdateBodyAsync(chatMessageId, newBody, editedOn, cancellationToken))
				return null;

			message.Body = newBody;
			message.EditedOn = editedOn;

			var channel = await _chatChannelRepository.GetByIdAsync(message.ChatChannelId);
			PublishEvent(channel, ChatEventKinds.MessageEdited, BuildMessageDto(message));

			return message;
		}

		public async Task<bool> DeleteMessageAsync(string chatMessageId, string byUserId, bool asModerator, string reason, CancellationToken cancellationToken = default(CancellationToken))
		{
			var message = await _chatMessageRepository.GetByIdAsync(chatMessageId);
			if (message == null || message.DeletedOn.HasValue)
				return false;

			var isSender = string.Equals(message.SenderUserId, byUserId, StringComparison.OrdinalIgnoreCase);
			if (!isSender && !asModerator)
				return false;

			var isModeratorDelete = asModerator && !isSender;

			// Frozen channel: the author can no longer retract what they said, but moderation still has to
			// work — flagged content on a closed incident must remain removable.
			if (!isModeratorDelete && await IsChannelFrozenAsync(message.ChatChannelId))
				return false;

			await SaveEditHistoryAsync(message, isModeratorDelete ? ChatMessageEditType.ModeratorDelete : ChatMessageEditType.SenderDelete, byUserId, cancellationToken);

			var deletedOn = DateTime.UtcNow;
			if (!await _chatMessageRepository.TombstoneAsync(chatMessageId, deletedOn, byUserId, isModeratorDelete, cancellationToken))
				return false;

			message.Body = null;
			message.MetadataJson = null;
			message.DeletedOn = deletedOn;
			message.DeletedByUserId = byUserId;
			message.IsModerated = isModeratorDelete;

			var channel = await _chatChannelRepository.GetByIdAsync(message.ChatChannelId);
			PublishEvent(channel, ChatEventKinds.MessageDeleted, new
			{
				message.ChatMessageId,
				message.ChatChannelId,
				message.MessageSeq,
				message.DeletedOn,
				DeletedByModerator = isModeratorDelete,
				message.IsModerated
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

			if (await IsChannelFrozenAsync(message.ChatChannelId))
				return false;

			// Banned or currently-muted participants can't react; silently skip.
			var member = unitId.HasValue
				? await _chatChannelMemberRepository.GetUnitMemberAsync(message.ChatChannelId, unitId.Value)
				: await _chatChannelMemberRepository.GetUserMemberAsync(message.ChatChannelId, userId);
			if (member != null && (member.IsBanned || (member.MutedUntil.HasValue && member.MutedUntil.Value > DateTime.UtcNow)))
				return false;

			// Double-taps are common: bail out before the insert so the ordinary duplicate never
			// reaches the database (RepositoryBase logs every insert exception, so relying on the
			// unique-violation catch below alone floods the error log). The catch still covers the
			// genuine concurrent race two requests can win simultaneously.
			var existingReactions = await _chatMessageReactionRepository.GetByMessageIdsAsync(new[] { chatMessageId });
			var alreadyReacted = existingReactions != null && existingReactions.Any(r =>
				string.Equals(r.Emoji, emoji, StringComparison.Ordinal)
				&& (unitId.HasValue
					? r.UnitId == unitId
					: r.UserId != null && string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase)));
			if (alreadyReacted)
				return true;

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
			catch (Exception ex) when (IsUniqueViolation(ex))
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

			if (await IsChannelFrozenAsync(message.ChatChannelId))
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

			var pinnedOn = pinned ? DateTime.UtcNow : (DateTime?)null;
			if (!await _chatMessageRepository.SetPinnedAsync(chatMessageId, pinnedOn, pinned ? byUserId : null, cancellationToken))
				return false;

			message.PinnedOn = pinnedOn;
			message.PinnedByUserId = pinned ? byUserId : null;

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

			var results = await _chatMessageRepository.SearchAsync(departmentId, channelIds, query, from, to, Math.Max(page, 1), pageSize <= 0 ? 25 : Math.Min(pageSize, 100));
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

		/// <summary>
		/// Server-side metadata validation: the JSON must parse; link urls must be http/https; GIF urls
		/// (both the animation and its preview) must be https on a known GIF CDN host. Invalid payloads
		/// are dropped (null), never fatal.
		///
		/// Clients send a nested envelope — { "gif": { "url", "previewUrl" } }, { "link": { "url" } } —
		/// so the urls are read from that section first. Older mobile builds wrote the url flat at the
		/// root, which is still read as a fallback: checking both shapes is what stops a caller from
		/// evading the CDN allowlist just by picking the shape the validator does not look at.
		/// </summary>
		private static string ValidateMetadataJson(ChatMessageType messageType, string metadataJson)
		{
			if (string.IsNullOrWhiteSpace(metadataJson))
				return metadataJson;

			try
			{
				var metadata = JObject.Parse(metadataJson);

				if (messageType == ChatMessageType.Gif)
				{
					var gif = ReadSection(metadata, "gif");
					var gifUrl = ReadUrl(gif, "url", "gifUrl") ?? ReadUrl(metadata, "url", "gifUrl");
					if (string.IsNullOrWhiteSpace(gifUrl))
						return metadataJson;

					var previewUrl = ReadUrl(gif, "previewUrl") ?? ReadUrl(metadata, "previewUrl");
					if (!IsAllowedGifUrl(gifUrl) || (!string.IsNullOrWhiteSpace(previewUrl) && !IsAllowedGifUrl(previewUrl)))
						return null;

					return metadataJson;
				}

				var link = ReadSection(metadata, "link");
				var url = ReadUrl(link, "url") ?? ReadUrl(metadata, "url");
				if (string.IsNullOrWhiteSpace(url))
					return metadataJson;

				if (!Uri.TryCreate(url, UriKind.Absolute, out var linkUri) ||
					(linkUri.Scheme != Uri.UriSchemeHttp && linkUri.Scheme != Uri.UriSchemeHttps))
					return null;

				return metadataJson;
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>Case-insensitive lookup of a nested metadata object ("gif", "link", ...).</summary>
		private static JObject ReadSection(JObject metadata, string name)
		{
			return metadata?.GetValue(name, StringComparison.OrdinalIgnoreCase) as JObject;
		}

		/// <summary>Case-insensitive lookup of the first non-empty string among the given property names.</summary>
		private static string ReadUrl(JObject source, params string[] names)
		{
			if (source == null)
				return null;

			foreach (var name in names)
			{
				if (source.GetValue(name, StringComparison.OrdinalIgnoreCase) is JValue value &&
					value.Type == JTokenType.String)
				{
					var url = (string)value;
					if (!string.IsNullOrWhiteSpace(url))
						return url;
				}
			}

			return null;
		}

		private static bool IsAllowedGifUrl(string url)
		{
			return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
				uri.Scheme == Uri.UriSchemeHttps &&
				IsGifCdnHost(uri.Host);
		}

		private static bool IsGifCdnHost(string host)
		{
			if (string.IsNullOrWhiteSpace(host) || ChatConfig.GifCdnHosts == null)
				return false;

			return ChatConfig.GifCdnHosts.Any(cdn =>
				string.Equals(host, cdn, StringComparison.OrdinalIgnoreCase) ||
				host.EndsWith("." + cdn, StringComparison.OrdinalIgnoreCase));
		}

		private async Task<string> ResolveSenderDisplayNameAsync(string senderUserId, int? asUnitId, bool asIncidentCommander, bool asBot, string displayNameOverride)
		{
			if (!string.IsNullOrWhiteSpace(displayNameOverride))
				return displayNameOverride;

			if (asBot)
				return "Resgrid Assistant";

			string profileName = null;
			var profile = await _userProfileService.GetProfileByUserIdAsync(senderUserId);
			if (profile != null)
				profileName = $"{profile.FirstName} {profile.LastName}".Trim();

			if (asUnitId.HasValue)
			{
				// Multiple people can be logged in as the same unit — keep the individual visible
				// ("Engine 6 (Alice Smith)") so dispatchers and IC know who is typing.
				var unit = await _unitsService.GetUnitByIdAsync(asUnitId.Value);
				var unitName = unit?.Name ?? "Unit";
				return string.IsNullOrWhiteSpace(profileName) ? unitName : $"{unitName} ({profileName})";
			}

			if (asIncidentCommander)
				return string.IsNullOrWhiteSpace(profileName) ? "Incident Commander" : $"Incident Commander ({profileName})";

			return string.IsNullOrWhiteSpace(profileName) ? "Unknown" : profileName;
		}

		private async Task SaveMentionsAsync(List<ChatMessageMention> mentions, ChatMessage message, CancellationToken cancellationToken)
		{
			if (mentions == null || mentions.Count == 0)
				return;

			foreach (var mention in mentions)
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
			var requiredUserIds = audience.Where(u => !string.Equals(u, message.SenderUserId, StringComparison.OrdinalIgnoreCase)).ToList();

			await _chatMessageAckRepository.BulkInsertAcksAsync(requiredUserIds.Select(userId => new ChatMessageAck
			{
				ChatMessageAckId = Guid.NewGuid().ToString(),
				ChatMessageId = message.ChatMessageId,
				ChatChannelId = message.ChatChannelId,
				DepartmentId = message.DepartmentId,
				UserId = userId,
				RequiredOn = message.SentOn
			}), cancellationToken);

			// SenderUserId lets clients skip the "acknowledge" banner for the sender's own message
			// (the sender has no ack row — see requiredUserIds above).
			PublishEvent(channel, ChatEventKinds.AckRequired, new { message.ChatMessageId, message.ChatChannelId, message.MessageSeq, RequiredCount = requiredUserIds.Count, message.SenderUserId });
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

		/// <summary>
		/// Push fan-out off the request path: per-recipient Novu calls can be slow for large channels,
		/// and a push failure must never fail the send. Fresh resolution inside the task keeps us off
		/// the request's disposed lifetime scope (ChatProvisioningEventService pattern).
		/// </summary>
		private void FireAndForgetNotify(ChatChannel channel, ChatMessage message, List<ChatMessageMention> mentions)
		{
			_ = Task.Run(async () =>
			{
				try
				{
					var notifier = ServiceLocator.Current.GetInstance<IChatNotificationService>();
					await notifier.NotifyMessageSentAsync(channel, message, mentions);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			});
		}

		private static bool IsUniqueViolation(Exception ex)
		{
			if (ex is PostgresException postgres)
				return postgres.SqlState == "23505";

			if (ex is SqlException sql)
				return sql.Number == 2601 || sql.Number == 2627;

			return false;
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
				message.EditedOn,
				message.IsModerated
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
