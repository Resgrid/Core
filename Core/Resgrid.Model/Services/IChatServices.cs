using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Channel lifecycle: creation, membership, preferences and the idempotent Ensure* provisioning for
	/// default (department/group), incident (call/lane/command) and chatbot channels.
	/// AUTHORIZATION: unless a method documents its own enforcement,
	/// the CALLER must verify access via IChatPermissionService before invoking these methods — the service
	/// executes, it does not gate reads.
	/// </summary>
	public interface IChatChannelService
	{
		/// <summary>Raw channel lookup; the CALLER must verify the user can access the returned channel.</summary>
		Task<ChatChannel> GetChannelByIdAsync(string chatChannelId);

		/// <summary>Batch channel lookup (single query, distinct ids). The CALLER must verify access per channel. Missing ids are simply absent from the result; order is not guaranteed.</summary>
		Task<List<ChatChannel>> GetChannelsByIdsAsync(IEnumerable<string> chatChannelIds);

		/// <summary>
		/// Assembles the channel list for a user: implicit-audience channels they can access (department,
		/// groups, active incidents) plus explicit memberships (DMs, ad-hoc, custom, chatbot). Excludes
		/// archived channels unless <paramref name="includeArchived"/>. Access is evaluated per channel
		/// inside this method; the result is briefly cached per user.
		/// </summary>
		Task<List<ChatChannel>> GetChannelsForUserAsync(int departmentId, string userId, int? activeUnitId, bool includeArchived = false);

		/// <summary>
		/// Finds or creates the 1:1 channel between the creator and a user or unit (DmKey dedup).
		/// Enforces cross-tenant rules: the creator and target user/unit must belong to the department
		/// (UnauthorizedAccessException otherwise). The CALLER must verify the creator may open DMs.
		/// </summary>
		Task<ChatChannel> GetOrCreateDirectMessageChannelAsync(int departmentId, string creatorUserId, string targetUserId, int? targetUnitId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Creates an ad-hoc group channel. Enforces that the creator and every memberUserId belong to the department
		/// (UnauthorizedAccessException otherwise). The CALLER must verify the creator may create groups.
		/// </summary>
		Task<ChatChannel> CreateAdHocGroupChannelAsync(int departmentId, string creatorUserId, string name, List<string> memberUserIds, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Creates a permission-locked custom channel; rules are OR-evaluated (groups/roles/users). Enforces department membership and department-admin authority for the creator.</summary>
		Task<ChatChannel> CreateCustomChannelAsync(int departmentId, string creatorUserId, string name, string topic, List<ChatChannelAccessRule> accessRules, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Name/topic update; enforces the supplied department boundary and moderator rights internally.</summary>
		Task<ChatChannel> UpdateChannelAsync(int departmentId, string chatChannelId, string name, string topic, string byUserId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Archive/unarchive; enforces the supplied department boundary and moderator rights internally.</summary>
		Task<bool> SetChannelArchivedAsync(int departmentId, string chatChannelId, bool archived, string byUserId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Raw member list; the CALLER must verify the user can access the channel first.</summary>
		Task<List<ChatChannelMember>> GetMembersAsync(string chatChannelId);

		/// <summary>A user's active (not-removed) explicit memberships across the department — used for unread/preference lookups.</summary>
		Task<List<ChatChannelMember>> GetActiveMembershipsForUserAsync(int departmentId, string userId);

		/// <summary>Active member rows for a set of channels in one query — used to label DM channels with the counterpart's name.</summary>
		Task<List<ChatChannelMember>> GetActiveMembersForChannelsAsync(List<string> chatChannelIds);

		/// <summary>A unit's active (not-removed) memberships across the department — read pointers/preferences for unit-participant channels.</summary>
		Task<List<ChatChannelMember>> GetActiveMembershipsForUnitAsync(int departmentId, int unitId);

		/// <summary>A user's member row for a single channel (null if none); does not lazily create one.</summary>
		Task<ChatChannelMember> GetUserMembershipAsync(string chatChannelId, string userId);

		/// <summary>A unit's member row for a single channel (null if none); does not lazily create one.</summary>
		Task<ChatChannelMember> GetUnitMembershipAsync(string chatChannelId, int unitId);

		/// <summary>
		/// Adds members. Enforces the supplied department boundary, supported channel types, actor membership
		/// or moderator authority, and that every userId belongs to the channel's department.
		/// </summary>
		Task<List<ChatChannelMember>> AddMembersAsync(int departmentId, string chatChannelId, List<string> userIds, string addedByUserId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Marks the member removed (leave or kick); enforces department scope and self-or-moderator authority internally.</summary>
		Task<bool> RemoveMemberAsync(int departmentId, string chatChannelId, string userId, string removedByUserId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Replaces all access rules atomically; enforces department scope and moderator rights internally.</summary>
		Task<bool> ReplaceAccessRulesAsync(int departmentId, string chatChannelId, List<ChatChannelAccessRule> accessRules, string byUserId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Returns the participant's member row for the channel, lazily creating one for implicit-audience
		/// channels (department/group/incident/chatbot) so read pointers / preferences have a home.
		/// DirectMessage/AdHocGroup/CustomLocked channels never self-grant: an existing row is reactivated,
		/// a missing row throws UnauthorizedAccessException. Access must already be verified by the CALLER.
		/// </summary>
		Task<ChatChannelMember> EnsureMemberStateAsync(string chatChannelId, int departmentId, string userId, int? unitId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Sets the caller's own notification preference; goes through EnsureMemberStateAsync (see its self-grant rules).</summary>
		Task<bool> SetNotificationPreferenceAsync(string chatChannelId, int departmentId, string userId, ChatNotificationPreference preference, CancellationToken cancellationToken = default(CancellationToken));

		// ----- Idempotent provisioning (safe to call repeatedly; unique indexes backstop races) -----

		Task<ChatChannel> EnsureDepartmentChannelAsync(int departmentId, CancellationToken cancellationToken = default(CancellationToken));

		Task<ChatChannel> EnsureGroupChannelAsync(DepartmentGroup group, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Ensures the main incident channel for a call exists.</summary>
		Task<ChatChannel> EnsureIncidentChannelAsync(int departmentId, int callId, string callName, CancellationToken cancellationToken = default(CancellationToken));

		Task<ChatChannel> EnsureLaneChannelAsync(CommandStructureNode node, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Provisions lane channels for a set of nodes belonging to a single call, reading the call's
		/// existing channels once (avoids the per-node lookup) and inserting only the missing lanes.
		/// </summary>
		Task EnsureLaneChannelsAsync(IEnumerable<CommandStructureNode> nodes, CancellationToken cancellationToken = default(CancellationToken));

		Task<ChatChannel> EnsureCommandChannelAsync(IncidentCommand command, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Ensures the incident's "All Leads" channel: the IC and every lane's primary/secondary lead.</summary>
		Task<ChatChannel> EnsureLeadsChannelAsync(IncidentCommand command, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Ensures the incident's line to the dispatch desk.</summary>
		Task<ChatChannel> EnsureDispatchChannelAsync(int departmentId, int callId, string incidentCommandId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Ensures the unit's standing line to the dispatch desk: the unit-shared identity plus every
		/// dispatch-authorized user. Department-wide and permanent (not call-scoped). The unit is stamped
		/// as an explicit member row so its operators see the channel through the unit-membership pass;
		/// the dispatch side is an implicit audience. Also refreshes the channel name if the unit was renamed.
		/// </summary>
		Task<ChatChannel> EnsureUnitDispatchChannelAsync(int departmentId, int unitId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Ensures the requester's private line to the incident's current commander ("Message the IC").
		/// One channel per (call, requester); the requester is stamped as an explicit member row while the
		/// commander side stays implicit, so a command transfer moves the conversation to the incoming
		/// commander without touching its history. Returns null when the call has no established command
		/// with a current commander — the button that calls this is expected to stay disabled until then.
		/// </summary>
		Task<ChatChannel> EnsureIncidentCommanderLineAsync(int departmentId, int callId, string requesterUserId,
			int? requesterUnitId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Backfills every chat channel an ACTIVE incident should have — the call's incident channel, the
		/// command and "All Leads" channels, and one per live lane — inserting only what is missing.
		///
		/// Exists for incidents that were established before those channels were a thing: rather than a
		/// one-off migration, the read paths call this and the incident heals itself the first time someone
		/// opens it. Idempotent, and guarded by a short-lived marker so a board that refreshes on a timer
		/// pays one cache read instead of a channel query. Closed commands are skipped — provisioning a
		/// channel there would create it unarchived and quietly un-freeze a point-in-time record.
		/// </summary>
		Task EnsureIncidentChannelsAsync(IncidentCommand command, IEnumerable<CommandStructureNode> nodes, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Provisions the per-user chatbot channel; only call when a chatbot session starts (never on the channel-list path).</summary>
		Task<ChatChannel> EnsureChatbotChannelAsync(int departmentId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Archives every channel anchored to a call (call closed); unarchive on reopen.</summary>
		Task<bool> SetIncidentChannelsArchivedAsync(int callId, bool archived, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Archives every channel anchored to ONE incident command — its command channel and its lane
		/// channels — leaving the call's own incident channel alone. Used when command is closed while the
		/// call itself keeps running: the command conversation becomes a point-in-time record while the
		/// call channel stays live. Unarchive on reopen.
		/// </summary>
		Task<bool> SetCommandChannelsArchivedAsync(string incidentCommandId, bool archived, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Department chat settings (config defaults when no row exists); no authorization — safe for any department-scoped caller.</summary>
		Task<ChatDepartmentSetting> GetDepartmentSettingsAsync(int departmentId);

		/// <summary>Persists department chat settings after enforcing authenticated department-admin rights.</summary>
		Task<ChatDepartmentSetting> SaveDepartmentSettingsAsync(int departmentId, string byUserId, ChatDepartmentSetting settings, CancellationToken cancellationToken = default(CancellationToken));
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
		/// <summary>True only when the user is a current, active member of the authenticated department.</summary>
		Task<bool> IsActiveDepartmentUserAsync(int departmentId, string userId);

		/// <summary>Can the user (optionally acting for a unit) read/join this channel.</summary>
		Task<bool> CanAccessChannelAsync(ChatChannel channel, string userId, int? activeUnitId);

		/// <summary>Access plus posting constraints: not archived, not locked (unless moderator), not muted/banned.</summary>
		Task<bool> CanPostAsync(ChatChannel channel, string userId, int? asUnitId);

		/// <summary>Department admins moderate everything; group admins their group channel; ICs incident channels; explicit member moderators.</summary>
		Task<bool> CanModerateChannelAsync(ChatChannel channel, string userId);

		/// <summary>True when the unit belongs to the department AND the user actively crews it (active unit role).</summary>
		Task<bool> CanSendAsUnitAsync(string userId, int unitId, int departmentId);

		/// <summary>True when the user has dispatch access in this department and may see its shared operational channels.</summary>
		Task<bool> CanAccessDepartmentOperationalChannelsAsync(int departmentId, string userId);

		/// <summary>True when the user holds an active incident-command role (or is the current IC) on the call.</summary>
		Task<bool> CanSendAsIcAsync(string userId, int callId, int departmentId);

		/// <summary>True when the active user/unit is currently assigned or dispatched to this same-department incident. Department-wide dispatch visibility is deliberately excluded.</summary>
		Task<bool> CanAccessIncidentAsync(int departmentId, int callId, string userId, int? activeUnitId);

		/// <summary>
		/// Resolves the full user audience of a channel (for push notifications and urgent-ack provisioning).
		/// Unit participants expand to their active crew. Excludes removed/banned members.
		/// </summary>
		Task<List<string>> ResolveChannelAudienceUserIdsAsync(ChatChannel channel);

		/// <summary>True when the user can administer the department (used to widen provisioning/visibility, e.g. every group default channel).</summary>
		Task<bool> IsDepartmentAdminAsync(int departmentId, string userId);

		/// <summary>Drops cached permission evaluations for a channel (membership/roles changed) and bumps the channel-list cache version.</summary>
		Task InvalidateChannelCacheAsync(string chatChannelId);

		/// <summary>Current distributed authorization epoch used to isolate realtime channel groups after access changes.</summary>
		Task<string> GetChannelAccessVersionAsync(string chatChannelId);
	}

	/// <summary>
	/// Push notification fan-out for chat messages. Single enforcement point for per-channel
	/// notification preferences, mention overrides and urgent-overrides-mute.
	/// INTERNAL: invoked off the request path by the message pipeline; not an authorization boundary.
	/// </summary>
	public interface IChatNotificationService
	{
		/// <summary>
		/// Notifies the channel audience about a new message: resolves recipients, applies preferences
		/// (Muted / MentionsOnly / urgent override), suppresses users currently online (they get SignalR),
		/// computes badges and pushes via IPushService (user + IC subscribers, plus unit-device
		/// subscribers for unit participants). channel.LastMessageSeq must reflect the new message's seq
		/// for correct badge counts.
		/// </summary>
		Task NotifyMessageSentAsync(ChatChannel channel, ChatMessage message, List<ChatMessageMention> mentions);
	}

	/// <summary>
	/// Request-scoped forensic context for moderation audit rows (SIEM/forensics). Supplied by the
	/// controller from the HTTP request; null when a moderation action originates outside a request
	/// (background job), in which case only the server-derived fields are recorded.
	/// </summary>
	public class ChatModerationContext
	{
		public string IpAddress { get; set; }
		public string UserAgent { get; set; }
		/// <summary>Request correlation id (e.g. HttpContext.TraceIdentifier) for cross-log stitching.</summary>
		public string TraceId { get; set; }
		/// <summary>The actor's authority for this action, e.g. "DepartmentAdmin" or "ChannelModerator".</summary>
		public string ActorRole { get; set; }
	}

	/// <summary>
	/// Moderation: user flags, moderator actions (delete/mute/ban/lock), the immutable moderation audit
	/// trail (mirrored to the department AuditLog) and records-request exports. Mutations accept the
	/// authenticated department/user separately and re-evaluate tenant and moderator authority internally.
	/// </summary>
	public interface IChatModerationService
	{
		/// <summary>Flags a message for review; enforces department scope and current channel access, then dedupes an existing open flag by the same user.</summary>
		Task<ChatMessageFlag> FlagMessageAsync(int departmentId, string chatMessageId, string flaggedByUserId, ChatFlagReason reason, string note, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Flag queue for current department administrators.</summary>
		Task<List<ChatMessageFlag>> GetFlagsAsync(int departmentId, string byUserId, ChatFlagStatus status, int page, int pageSize);

		/// <summary>Resolves a flag for a current department administrator; cross-department ids are rejected and only Open flags transition.</summary>
		Task<ChatMessageFlag> ResolveFlagAsync(string chatMessageFlagId, int departmentId, string byUserId, ChatFlagStatus resolution, string resolutionNote, CancellationToken cancellationToken = default(CancellationToken), ChatModerationContext context = null);

		/// <summary>Moderator tombstone-delete; enforces department scope and current channel moderator rights before audit.</summary>
		Task<bool> ModeratorDeleteMessageAsync(int departmentId, string chatMessageId, string byUserId, string reason, CancellationToken cancellationToken = default(CancellationToken), ChatModerationContext context = null);

		/// <summary>Mute/unmute a participant; enforces department scope and current channel moderator rights.</summary>
		Task<bool> SetUserMutedAsync(int departmentId, string chatChannelId, string targetUserId, DateTime? mutedUntil, string byUserId, string reason, CancellationToken cancellationToken = default(CancellationToken), ChatModerationContext context = null);

		/// <summary>Ban/unban a participant; enforces department scope and current channel moderator rights.</summary>
		Task<bool> SetUserBannedAsync(int departmentId, string chatChannelId, string targetUserId, bool banned, string byUserId, string reason, CancellationToken cancellationToken = default(CancellationToken), ChatModerationContext context = null);

		/// <summary>Lock/unlock a channel; enforces department scope and current channel moderator rights.</summary>
		Task<bool> SetChannelLockedAsync(int departmentId, string chatChannelId, bool locked, string byUserId, string reason, CancellationToken cancellationToken = default(CancellationToken), ChatModerationContext context = null);

		/// <summary>Moderation audit trail for current department administrators.</summary>
		Task<List<ChatModerationAction>> GetModerationActionsAsync(int departmentId, string byUserId, string chatChannelId, int page, int pageSize);

		/// <summary>Queues a transcript export after enforcing current department-admin and channel scope.</summary>
		Task<ChatExport> RequestExportAsync(int departmentId, string byUserId, string chatChannelId, DateTime? startDate, DateTime? endDate, ChatExportFormat format, CancellationToken cancellationToken = default(CancellationToken), ChatModerationContext context = null);

		/// <summary>Export list without result blobs for current department administrators.</summary>
		Task<List<ChatExport>> GetExportsAsync(int departmentId, string byUserId);

		/// <summary>Full export row including result data; enforces current department-admin rights and audits the download.</summary>
		Task<ChatExport> GetExportForDownloadAsync(string chatExportId, int departmentId, string byUserId, CancellationToken cancellationToken = default(CancellationToken), ChatModerationContext context = null);
	}

	/// <summary>
	/// Chat presence backed by short-TTL cache entries. A user is online while any of their connections
	/// keeps the entry alive (refreshed on connect + heartbeat); entries expire naturally on disconnect,
	/// so "offline" is eventually-consistent within the TTL. INTERNAL plumbing — not an authorization boundary.
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

		/// <summary>
		/// Records the channel the user currently has open (null/empty clears it). When the user is acting
		/// as a unit, the unit's active channel is recorded too so rig-device pushes can be suppressed.
		/// </summary>
		Task SetActiveChannelAsync(int departmentId, string userId, string channelId, int? unitId = null);

		/// <summary>Clears the user's (and their acting unit's) active-channel marker.</summary>
		Task ClearActiveChannelAsync(int departmentId, string userId);

		/// <summary>Bulk lookup: the subset of userIds actively viewing the given channel right now.</summary>
		Task<List<string>> GetUsersActiveInChannelAsync(int departmentId, List<string> userIds, string channelId);

		/// <summary>True when the unit's device currently has the given channel open.</summary>
		Task<bool> IsUnitActiveInChannelAsync(int departmentId, int unitId, string channelId);
	}

	/// <summary>
	/// Parameters for sending a chat message (REST-first write path). The sender identity is NOT part of
	/// the request: it is the authenticated user passed separately to SendMessageAsync. Bot sends go
	/// through SendBotMessageAsync — there is no client-settable way to spoof sender identity or bypass
	/// permission checks.
	/// </summary>
	public class ChatMessageSendRequest
	{
		public string ChatChannelId { get; set; }
		public int DepartmentId { get; set; }
		/// <summary>Send as a unit identity ("Engine 6"); the sender still recorded for audit. Requires active crew on the unit.</summary>
		public int? AsUnitId { get; set; }
		/// <summary>Send as the Incident Commander identity; validated against active command roles.</summary>
		public bool AsIncidentCommander { get; set; }
		public string Body { get; set; }
		public ChatMessageType MessageType { get; set; }
		/// <summary>Urgent is moderator-only: non-moderators are silently downgraded to Normal.</summary>
		public ChatMessagePriority Priority { get; set; }
		public string ThreadRootMessageId { get; set; }
		public bool AlsoSendToChannel { get; set; }
		/// <summary>Client idempotency key; resends return the original message.</summary>
		public string ClientMessageId { get; set; }
		/// <summary>Link preview / GIF / location payload (JSON). Validated server-side per MessageType; invalid payloads are dropped (nulled), never fail the send.</summary>
		public string MetadataJson { get; set; }
		/// <summary>Resolved mentions from the client. Validated server-side: User targets must be channel-audience members (invalid ones dropped) and Everyone mentions require a moderator (dropped otherwise).</summary>
		public List<ChatMessageMention> Mentions { get; set; }
	}

	/// <summary>
	/// Message pipeline: validation, sequence allocation, mentions, urgent acks, edits/deletes with audit
	/// history, reactions, pins, read pointers, paging/delta-sync, search. Publishes ChatEventRaised
	/// envelopes for realtime fan-out. Every mutation takes the authenticated department and user identity
	/// separately from client DTOs and re-evaluates tenant scope and channel permissions internally.
	/// </summary>
	public interface IChatMessageService
	{
		/// <summary>
		/// Sends a message as the authenticated user. Enforces CanPostAsync (access, mute/ban, lock) and
		/// AsUnitId/AsIncidentCommander identity checks internally; <paramref name="departmentId"/> and
		/// <paramref name="senderUserId"/> MUST come from the authenticated principal — never client input.
		/// </summary>
		Task<ChatMessage> SendMessageAsync(int departmentId, string senderUserId, ChatMessageSendRequest request, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Internal bot send (chatbot pipeline): skips user permission checks, records no SenderUserId,
		/// posts with the Bot participant type. Never exposed to clients.
		/// </summary>
		Task<ChatMessage> SendBotMessageAsync(string channelId, string departmentId, string body, string senderDisplayName, string metadataJson = null);

		/// <summary>Raw message lookup; the CALLER must verify channel access for the requesting user first.</summary>
		Task<ChatMessage> GetMessageByIdAsync(string chatMessageId);

		/// <summary>Keyset page; the CALLER must verify channel access first.</summary>
		Task<List<ChatMessage>> GetMessagesPageAsync(string chatChannelId, long? beforeSeq, int limit);

		/// <summary>Delta sync for reconnect: everything after the client's last seen sequence. The CALLER must verify channel access first.</summary>
		Task<List<ChatMessage>> GetMessagesAfterAsync(string chatChannelId, long afterSeq, int limit);

		/// <summary>Thread page; the CALLER must verify channel access first.</summary>
		Task<List<ChatMessage>> GetThreadPageAsync(string threadRootMessageId, long? beforeSeq, int limit);

		/// <summary>Sender edit; enforces department scope, channel access and original-sender identity internally.</summary>
		Task<ChatMessage> EditMessageAsync(int departmentId, string chatMessageId, string editorUserId, string newBody, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Tombstone delete; enforces department scope and channel access. A requested moderator delete is independently verified against current channel permissions.</summary>
		Task<bool> DeleteMessageAsync(int departmentId, string chatMessageId, string byUserId, bool asModerator, string reason, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Adds a reaction; enforces department scope, current channel access, unit authority and mute/ban state internally.</summary>
		Task<bool> AddReactionAsync(int departmentId, string chatMessageId, string userId, int? unitId, string emoji, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Removes a reaction; enforces department scope, current channel access and unit authority internally.</summary>
		Task<bool> RemoveReactionAsync(int departmentId, string chatMessageId, string userId, int? unitId, string emoji, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Reaction rows for rendering; the CALLER must verify channel access first.</summary>
		Task<List<ChatMessageReaction>> GetReactionsForMessagesAsync(List<string> chatMessageIds);

		/// <summary>Attachment metadata for rendering; the CALLER must verify channel access first.</summary>
		Task<List<ChatAttachment>> GetAttachmentMetadataForMessagesAsync(List<string> chatMessageIds);

		/// <summary>Pin/unpin; enforces department scope and moderator rights internally.</summary>
		Task<bool> SetMessagePinnedAsync(int departmentId, string chatMessageId, string byUserId, bool pinned, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Pinned messages; the CALLER must verify channel access first.</summary>
		Task<List<ChatMessage>> GetPinnedMessagesAsync(string chatChannelId);

		/// <summary>Acknowledges an urgent message; enforces department scope and current channel access before stamping the caller's row.</summary>
		Task<int> AcknowledgeMessageAsync(int departmentId, string chatMessageId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Ack rows for a message; the CALLER must verify channel access first.</summary>
		Task<List<ChatMessageAck>> GetAcksForMessageAsync(string chatMessageId);

		/// <summary>The user's own pending acks (scoped to the supplied userId).</summary>
		Task<List<ChatMessageAck>> GetPendingAcksForUserAsync(int departmentId, string userId);

		/// <summary>Advances the participant's read pointer (monotonic) and emits a receipt event. The CALLER must verify channel access first (EnsureMemberStateAsync self-grant rules apply).</summary>
		Task<bool> MarkReadAsync(string chatChannelId, int departmentId, string userId, int? unitId, long seq, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Advances the delivered pointer; the CALLER must verify channel access first.</summary>
		Task<bool> MarkDeliveredAsync(string chatChannelId, int departmentId, string userId, int? unitId, long seq, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Searches message bodies across every channel the user can access (or one channel when supplied). Access is evaluated inside this method.</summary>
		Task<List<ChatMessage>> SearchAsync(int departmentId, string userId, int? activeUnitId, string query, string chatChannelId, DateTime? from, DateTime? to, int page, int pageSize);
	}
}
