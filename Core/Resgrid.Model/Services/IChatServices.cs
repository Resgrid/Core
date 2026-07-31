using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Channel lifecycle: creation, membership, preferences and the idempotent Ensure* provisioning for
	/// default (department/group), incident (call/lane/command) and chatbot channels.
	/// </summary>
	public interface IChatChannelService
	{
		Task<ChatChannel> GetChannelByIdAsync(string chatChannelId);

		/// <summary>
		/// Assembles the channel list for a user: implicit-audience channels they can access (department,
		/// groups, active incidents) plus explicit memberships (DMs, ad-hoc, custom, chatbot). Excludes
		/// archived channels unless <paramref name="includeArchived"/>.
		/// </summary>
		Task<List<ChatChannel>> GetChannelsForUserAsync(int departmentId, string userId, int? activeUnitId, bool includeArchived = false);

		/// <summary>Finds or creates the 1:1 channel between the creator and a user or unit (DmKey dedup).</summary>
		Task<ChatChannel> GetOrCreateDirectMessageChannelAsync(int departmentId, string creatorUserId, string targetUserId, int? targetUnitId, CancellationToken cancellationToken = default(CancellationToken));

		Task<ChatChannel> CreateAdHocGroupChannelAsync(int departmentId, string creatorUserId, string name, List<string> memberUserIds, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Creates a permission-locked custom channel; rules are OR-evaluated (groups/roles/users).</summary>
		Task<ChatChannel> CreateCustomChannelAsync(int departmentId, string creatorUserId, string name, string topic, List<ChatChannelAccessRule> accessRules, CancellationToken cancellationToken = default(CancellationToken));

		Task<ChatChannel> UpdateChannelAsync(string chatChannelId, string name, string topic, string byUserId, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> SetChannelArchivedAsync(string chatChannelId, bool archived, string byUserId, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<ChatChannelMember>> GetMembersAsync(string chatChannelId);

		Task<List<ChatChannelMember>> AddMembersAsync(string chatChannelId, List<string> userIds, string addedByUserId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Marks the member removed (leave or kick); history row kept.</summary>
		Task<bool> RemoveMemberAsync(string chatChannelId, string userId, string removedByUserId, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> ReplaceAccessRulesAsync(string chatChannelId, List<ChatChannelAccessRule> accessRules, string byUserId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Returns the participant's member row for the channel, lazily creating one for implicit-audience
		/// channels so read pointers / preferences have a home. Access must already be verified.
		/// </summary>
		Task<ChatChannelMember> EnsureMemberStateAsync(string chatChannelId, int departmentId, string userId, int? unitId, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> SetNotificationPreferenceAsync(string chatChannelId, int departmentId, string userId, ChatNotificationPreference preference, CancellationToken cancellationToken = default(CancellationToken));

		// ----- Idempotent provisioning (safe to call repeatedly; unique indexes backstop races) -----

		Task<ChatChannel> EnsureDepartmentChannelAsync(int departmentId, CancellationToken cancellationToken = default(CancellationToken));

		Task<ChatChannel> EnsureGroupChannelAsync(DepartmentGroup group, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Ensures the main incident channel for a call exists.</summary>
		Task<ChatChannel> EnsureIncidentChannelAsync(int departmentId, int callId, string callName, CancellationToken cancellationToken = default(CancellationToken));

		Task<ChatChannel> EnsureLaneChannelAsync(CommandStructureNode node, CancellationToken cancellationToken = default(CancellationToken));

		Task<ChatChannel> EnsureCommandChannelAsync(IncidentCommand command, CancellationToken cancellationToken = default(CancellationToken));

		Task<ChatChannel> EnsureChatbotChannelAsync(int departmentId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Archives every channel anchored to a call (call closed); unarchive on reopen.</summary>
		Task<bool> SetIncidentChannelsArchivedAsync(int callId, bool archived, CancellationToken cancellationToken = default(CancellationToken));

		Task<ChatDepartmentSetting> GetDepartmentSettingsAsync(int departmentId);

		Task<ChatDepartmentSetting> SaveDepartmentSettingsAsync(ChatDepartmentSetting settings, CancellationToken cancellationToken = default(CancellationToken));
	}

	/// <summary>
	/// Marker interface for the auto-activated event listener that provisions chat channels from
	/// call/incident domain events.
	/// </summary>
	public interface IChatProvisioningEventService
	{
	}

	/// <summary>
	/// The single authority for chat access decisions. Controllers and the chat hub must route every
	/// access/post/moderate check through here; results for hot paths are cached briefly.
	/// </summary>
	public interface IChatPermissionService
	{
		/// <summary>Can the user (optionally acting for a unit) read/join this channel.</summary>
		Task<bool> CanAccessChannelAsync(ChatChannel channel, string userId, int? activeUnitId);

		/// <summary>Access plus posting constraints: not archived, not locked (unless moderator), not muted/banned.</summary>
		Task<bool> CanPostAsync(ChatChannel channel, string userId, int? asUnitId);

		/// <summary>Department admins moderate everything; group admins their group channel; ICs incident channels; explicit member moderators.</summary>
		Task<bool> CanModerateChannelAsync(ChatChannel channel, string userId);

		Task<bool> CanSendAsUnitAsync(string userId, int unitId, int departmentId);

		/// <summary>True when the user holds an active incident-command role (or is the current IC) on the call.</summary>
		Task<bool> CanSendAsIcAsync(string userId, int callId, int departmentId);

		/// <summary>
		/// Resolves the full user audience of a channel (for push notifications and urgent-ack provisioning).
		/// Unit participants expand to their active crew. Excludes removed/banned members.
		/// </summary>
		Task<List<string>> ResolveChannelAudienceUserIdsAsync(ChatChannel channel);

		/// <summary>Drops cached permission evaluations for a channel (membership/roles changed).</summary>
		Task InvalidateChannelCacheAsync(string chatChannelId);
	}

	/// <summary>
	/// Push notification fan-out for chat messages. Single enforcement point for per-channel
	/// notification preferences, mention overrides and urgent-overrides-mute.
	/// </summary>
	public interface IChatNotificationService
	{
		/// <summary>
		/// Notifies the channel audience about a new message: resolves recipients, applies preferences
		/// (Muted / MentionsOnly / urgent override), computes badges and pushes via IPushService
		/// (user + IC subscribers, plus unit-device subscribers for unit participants).
		/// </summary>
		Task NotifyMessageSentAsync(ChatChannel channel, ChatMessage message, List<ChatMessageMention> mentions);
	}

	/// <summary>
	/// Moderation: user flags, moderator actions (delete/mute/ban/lock), the immutable moderation audit
	/// trail (mirrored to the department AuditLog) and records-request exports. Permission checks
	/// (CanModerateChannelAsync) are the CALLER's responsibility — controllers gate, this executes.
	/// </summary>
	public interface IChatModerationService
	{
		Task<ChatMessageFlag> FlagMessageAsync(string chatMessageId, string flaggedByUserId, ChatFlagReason reason, string note, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<ChatMessageFlag>> GetFlagsAsync(int departmentId, ChatFlagStatus status, int page, int pageSize);

		/// <summary>Resolves a flag; departmentId must match the flag's department (cross-department ids are rejected).</summary>
		Task<ChatMessageFlag> ResolveFlagAsync(string chatMessageFlagId, int departmentId, string byUserId, ChatFlagStatus resolution, string resolutionNote, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Moderator tombstone-delete; wraps IChatMessageService.DeleteMessageAsync with audit.</summary>
		Task<bool> ModeratorDeleteMessageAsync(string chatMessageId, string byUserId, string reason, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> SetUserMutedAsync(string chatChannelId, string targetUserId, DateTime? mutedUntil, string byUserId, string reason, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> SetUserBannedAsync(string chatChannelId, string targetUserId, bool banned, string byUserId, string reason, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> SetChannelLockedAsync(string chatChannelId, bool locked, string byUserId, string reason, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<ChatModerationAction>> GetModerationActionsAsync(int departmentId, string chatChannelId, int page, int pageSize);

		Task<ChatExport> RequestExportAsync(int departmentId, string byUserId, string chatChannelId, DateTime? startDate, DateTime? endDate, ChatExportFormat format, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Export list without result blobs.</summary>
		Task<List<ChatExport>> GetExportsAsync(int departmentId);

		/// <summary>Full export row including result data; audits the download.</summary>
		Task<ChatExport> GetExportForDownloadAsync(string chatExportId, int departmentId, string byUserId, CancellationToken cancellationToken = default(CancellationToken));
	}

	/// <summary>
	/// Chat presence backed by short-TTL cache entries. A user is online while any of their connections
	/// keeps the entry alive (refreshed on connect + heartbeat); entries expire naturally on disconnect,
	/// so "offline" is eventually-consistent within the TTL.
	/// </summary>
	public interface IChatPresenceService
	{
		/// <summary>Marks the user online; returns true when this transitioned them from offline.</summary>
		Task<bool> SetOnlineAsync(int departmentId, string userId);

		/// <summary>Refreshes the presence TTL (heartbeat).</summary>
		Task TouchAsync(int departmentId, string userId);

		Task<bool> IsOnlineAsync(int departmentId, string userId);

		/// <summary>Bulk presence lookup; returns the subset of userIds currently online.</summary>
		Task<List<string>> GetOnlineUsersAsync(int departmentId, List<string> userIds);
	}

	/// <summary>Parameters for sending a chat message (REST-first write path).</summary>
	public class ChatMessageSendRequest
	{
		public string ChatChannelId { get; set; }
		public int DepartmentId { get; set; }
		public string SenderUserId { get; set; }
		/// <summary>Send as a unit identity ("Engine 6"); SenderUserId still recorded for audit.</summary>
		public int? AsUnitId { get; set; }
		/// <summary>Send as the Incident Commander identity; validated against active command roles.</summary>
		public bool AsIncidentCommander { get; set; }
		public string Body { get; set; }
		public ChatMessageType MessageType { get; set; }
		public ChatMessagePriority Priority { get; set; }
		public string ThreadRootMessageId { get; set; }
		public bool AlsoSendToChannel { get; set; }
		/// <summary>Client idempotency key; resends return the original message.</summary>
		public string ClientMessageId { get; set; }
		/// <summary>Link preview / GIF / location payload (already-validated JSON).</summary>
		public string MetadataJson { get; set; }
		/// <summary>Explicit display-name override; normally computed (profile name, unit name, "Incident Commander (...)").</summary>
		public string SenderDisplayName { get; set; }
		/// <summary>Internal senders (chatbot) bypass user permission checks; never settable from the API.</summary>
		public bool AsBot { get; set; }
		/// <summary>Resolved mentions from the client (targets validated server-side).</summary>
		public List<ChatMessageMention> Mentions { get; set; }
	}

	/// <summary>
	/// Message pipeline: validation, sequence allocation, mentions, urgent acks, edits/deletes with audit
	/// history, reactions, pins, read pointers, paging/delta-sync, search. Publishes ChatEventRaised
	/// envelopes for realtime fan-out.
	/// </summary>
	public interface IChatMessageService
	{
		Task<ChatMessage> SendMessageAsync(ChatMessageSendRequest request, CancellationToken cancellationToken = default(CancellationToken));

		Task<ChatMessage> GetMessageByIdAsync(string chatMessageId);

		Task<List<ChatMessage>> GetMessagesPageAsync(string chatChannelId, long? beforeSeq, int limit);

		/// <summary>Delta sync for reconnect: everything after the client's last seen sequence.</summary>
		Task<List<ChatMessage>> GetMessagesAfterAsync(string chatChannelId, long afterSeq, int limit);

		Task<List<ChatMessage>> GetThreadPageAsync(string threadRootMessageId, long? beforeSeq, int limit);

		/// <summary>Sender edit; prior body preserved in ChatMessageEdits.</summary>
		Task<ChatMessage> EditMessageAsync(string chatMessageId, string editorUserId, string newBody, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Tombstone delete (sender or moderator); body preserved in ChatMessageEdits until retention purge.</summary>
		Task<bool> DeleteMessageAsync(string chatMessageId, string byUserId, bool asModerator, string reason, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> AddReactionAsync(string chatMessageId, string userId, int? unitId, string emoji, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> RemoveReactionAsync(string chatMessageId, string userId, int? unitId, string emoji, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<ChatMessageReaction>> GetReactionsForMessagesAsync(List<string> chatMessageIds);

		Task<List<ChatAttachment>> GetAttachmentMetadataForMessagesAsync(List<string> chatMessageIds);

		Task<bool> SetMessagePinnedAsync(string chatMessageId, string byUserId, bool pinned, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<ChatMessage>> GetPinnedMessagesAsync(string chatChannelId);

		/// <summary>Acknowledges an urgent message for the user; returns rows stamped (0 = nothing pending).</summary>
		Task<int> AcknowledgeMessageAsync(string chatMessageId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<ChatMessageAck>> GetAcksForMessageAsync(string chatMessageId);

		Task<List<ChatMessageAck>> GetPendingAcksForUserAsync(int departmentId, string userId);

		/// <summary>Advances the participant's read pointer (monotonic) and emits a receipt event.</summary>
		Task<bool> MarkReadAsync(string chatChannelId, int departmentId, string userId, int? unitId, long seq, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> MarkDeliveredAsync(string chatChannelId, int departmentId, string userId, int? unitId, long seq, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Searches message bodies across every channel the user can access (or one channel when supplied).</summary>
		Task<List<ChatMessage>> SearchAsync(int departmentId, string userId, int? activeUnitId, string query, string chatChannelId, DateTime? from, DateTime? to, int page, int pageSize);
	}
}
