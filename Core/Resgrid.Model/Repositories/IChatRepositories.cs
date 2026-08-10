using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IChatChannelRepository : IRepository<ChatChannel>
	{
		/// <summary>Finds a DM/adhoc channel by its normalized participant identity key.</summary>
		Task<ChatChannel> GetByDmKeyAsync(int departmentId, string dmKey);

		/// <summary>All channels anchored to a call (Incident + IncidentLane + IncidentCommand).</summary>
		Task<IEnumerable<ChatChannel>> GetByCallIdAsync(int callId);

		/// <summary>The single channel anchored to a call of a specific type (e.g. Incident or IncidentCommand), or null.</summary>
		Task<ChatChannel> GetByCallIdAndTypeAsync(int callId, int channelType);

		Task<ChatChannel> GetByCommandStructureNodeIdAsync(string commandStructureNodeId);

		Task<ChatChannel> GetByGroupIdAsync(int groupId);

		Task<ChatChannel> GetDepartmentDefaultAsync(int departmentId);

		Task<ChatChannel> GetChatbotChannelAsync(int departmentId, string userId);

		Task<IEnumerable<ChatChannel>> GetByIdsAsync(IEnumerable<string> chatChannelIds);

		/// <summary>
		/// Atomically increments the channel's LastMessageSeq (and stamps LastMessageOn) returning the
		/// allocated sequence. Single UPDATE with OUTPUT/RETURNING so concurrent senders never collide.
		/// </summary>
		Task<long> AllocateNextMessageSeqAsync(string chatChannelId, DateTime lastMessageOn);

		/// <summary>Archives (or unarchives) every channel anchored to a call; returns affected channel ids.</summary>
		Task<IEnumerable<string>> SetArchivedByCallIdAsync(int callId, bool archived, DateTime? archivedOn);

		/// <summary>Archives/unarchives every channel anchored to one incident command (the command channel and its lane channels), returning the affected channel ids.</summary>
		Task<IEnumerable<string>> SetArchivedByIncidentCommandIdAsync(string incidentCommandId, bool archived, DateTime? archivedOn);

		/// <summary>Channels in the department carrying a per-channel retention override.</summary>
		Task<IEnumerable<ChatChannel>> GetWithRetentionOverrideAsync(int departmentId);

		/// <summary>Every channel in the department; archived rows excluded unless <paramref name="includeArchived"/>.</summary>
		Task<IEnumerable<ChatChannel>> GetAllByDepartmentIdAsync(int departmentId, bool includeArchived);

		/// <summary>
		/// Targeted name/topic update: never touches LastMessageSeq/LastMessageOn so the atomic
		/// sequence allocator is never rewound by a stale full-row write.
		/// </summary>
		Task<bool> UpdateChannelInfoAsync(string chatChannelId, string name, string topic, DateTime modifiedOn, CancellationToken cancellationToken);

		/// <summary>Targeted archive flag update (see <see cref="UpdateChannelInfoAsync"/>).</summary>
		Task<bool> SetArchivedAsync(string chatChannelId, bool archived, DateTime? archivedOn, DateTime modifiedOn, CancellationToken cancellationToken);

		/// <summary>Targeted lock flag update (see <see cref="UpdateChannelInfoAsync"/>).</summary>
		Task<bool> SetLockedAsync(string chatChannelId, bool locked, string lockedByUserId, DateTime? lockedOn, DateTime modifiedOn, CancellationToken cancellationToken);

		/// <summary>
		/// Atomically creates a DM channel plus its member rows in one transaction. The channel insert
		/// uses insert-if-absent on (DepartmentId, DmKey) so a losing racer simply reads the winner;
		/// member rows are only written when this call wins the insert. Returns the persisted channel.
		/// </summary>
		Task<ChatChannel> CreateDirectMessageChannelAsync(ChatChannel channel, IEnumerable<ChatChannelMember> members, CancellationToken cancellationToken);
	}

	public interface IChatChannelAccessRuleRepository : IRepository<ChatChannelAccessRule>
	{
		Task<IEnumerable<ChatChannelAccessRule>> GetByChannelIdAsync(string chatChannelId);

		Task<bool> DeleteByChannelIdAsync(string chatChannelId, CancellationToken cancellationToken);
	}

	public interface IChatChannelMemberRepository : IRepository<ChatChannelMember>
	{
		Task<IEnumerable<ChatChannelMember>> GetByChannelIdAsync(string chatChannelId);

		Task<ChatChannelMember> GetUserMemberAsync(string chatChannelId, string userId);

		Task<ChatChannelMember> GetUnitMemberAsync(string chatChannelId, int unitId);

		/// <summary>Active (not removed) explicit memberships for a user across the department.</summary>
		Task<IEnumerable<ChatChannelMember>> GetActiveByUserIdAsync(int departmentId, string userId);

		/// <summary>
		/// Monotonic read/delivered pointer update: only advances when the supplied seq is higher than the
		/// stored one (single UPDATE ... WHERE seq &lt; @seq).
		/// </summary>
		Task<bool> AdvanceReadPointerAsync(string chatChannelMemberId, long seq, DateTime readOn);

		Task<bool> AdvanceDeliveredPointerAsync(string chatChannelMemberId, long seq);

		/// <summary>Targeted mute update: touches only MutedUntil/ModifiedOn so concurrent pointer advances are never rewound.</summary>
		Task<bool> SetMemberMutedAsync(string chatChannelMemberId, DateTime? mutedUntil, CancellationToken cancellationToken);

		/// <summary>Targeted ban update (see <see cref="SetMemberMutedAsync"/>).</summary>
		Task<bool> SetMemberBannedAsync(string chatChannelMemberId, bool isBanned, string bannedByUserId, CancellationToken cancellationToken);

		/// <summary>Targeted notification-preference update (see <see cref="SetMemberMutedAsync"/>).</summary>
		Task<bool> SetMemberNotificationPreferenceAsync(string chatChannelMemberId, int notificationPreference, CancellationToken cancellationToken);

		/// <summary>Targeted active flag update: reactivate clears RemovedOn (restamps JoinedOn), deactivate sets it (see <see cref="SetMemberMutedAsync"/>).</summary>
		Task<bool> SetMemberActiveAsync(string chatChannelMemberId, bool isActive, CancellationToken cancellationToken);
	}

	public interface IChatMessageRepository : IRepository<ChatMessage>
	{
		/// <summary>Keyset page of top-level + AlsoSendToChannel messages, newest first, MessageSeq &lt; beforeSeq.</summary>
		Task<IEnumerable<ChatMessage>> GetPageAsync(string chatChannelId, long? beforeSeq, int limit);

		/// <summary>Delta sync: every message with MessageSeq &gt; afterSeq (includes thread replies), ascending.</summary>
		Task<IEnumerable<ChatMessage>> GetAfterSeqAsync(string chatChannelId, long afterSeq, int limit);

		/// <summary>Keyset page of replies in a thread, newest first.</summary>
		Task<IEnumerable<ChatMessage>> GetThreadPageAsync(string threadRootMessageId, long? beforeSeq, int limit);

		Task<ChatMessage> GetByClientMessageIdAsync(string chatChannelId, string senderUserId, string clientMessageId);

		Task<IEnumerable<ChatMessage>> GetPinnedByChannelIdAsync(string chatChannelId);

		/// <summary>Body search across the supplied channels (LIKE/ILIKE per engine), newest first, paged.</summary>
		Task<IEnumerable<ChatMessage>> SearchAsync(int departmentId, IEnumerable<string> chatChannelIds, string query, DateTime? from, DateTime? to, int page, int pageSize);

		/// <summary>Increments ThreadReplyCount and stamps LastThreadReplyOn on the thread root.</summary>
		Task<bool> IncrementThreadReplyAsync(string threadRootMessageId, DateTime repliedOn);

		/// <summary>
		/// Ids of up to <paramref name="batchSize"/> messages past the retention cutoff. When
		/// <paramref name="chatChannelId"/> is null, only messages in channels WITHOUT a per-channel
		/// retention override are returned (the department-default pass).
		/// </summary>
		Task<List<string>> GetRetentionBatchIdsAsync(int departmentId, string chatChannelId, DateTime cutoffUtc, int batchSize);

		/// <summary>
		/// Hard-deletes the messages and their child rows (edits, reactions, mentions, acks, attachments,
		/// flags) — the retention purge. Moderation action rows are audit and are NOT touched. Returns
		/// the number of messages removed.
		/// </summary>
		Task<int> DeleteMessagesByIdsAsync(List<string> chatMessageIds, CancellationToken cancellationToken);

		/// <summary>Messages for a records-request export, oldest first, capped at maxRows.</summary>
		Task<IEnumerable<ChatMessage>> GetForExportAsync(int departmentId, string chatChannelId, DateTime? from, DateTime? to, int maxRows);

		/// <summary>
		/// Targeted body edit: UPDATE ... WHERE ChatMessageId AND DeletedOn IS NULL so a concurrent
		/// tombstone is never un-deleted and ThreadReplyCount increments are never lost. False = already deleted.
		/// </summary>
		Task<bool> UpdateBodyAsync(string chatMessageId, string body, DateTime editedOn, CancellationToken cancellationToken);

		/// <summary>Targeted tombstone (body/metadata cleared, deletion and moderation state stamped) guarded by DeletedOn IS NULL.</summary>
		Task<bool> TombstoneAsync(string chatMessageId, DateTime deletedOn, string deletedByUserId, bool isModerated, CancellationToken cancellationToken);

		/// <summary>Targeted pin update guarded by DeletedOn IS NULL.</summary>
		Task<bool> SetPinnedAsync(string chatMessageId, DateTime? pinnedOn, string pinnedByUserId, CancellationToken cancellationToken);
	}

	public interface IChatMessageEditRepository : IRepository<ChatMessageEdit>
	{
		Task<IEnumerable<ChatMessageEdit>> GetByMessageIdAsync(string chatMessageId);

		/// <summary>Batched edit-history fetch for exports: all edit rows for the given messages in one query.</summary>
		Task<IEnumerable<ChatMessageEdit>> GetChatExportEditsByMessageIdsAsync(IEnumerable<string> messageIds);
	}

	public interface IChatAttachmentRepository : IRepository<ChatAttachment>
	{
		/// <summary>Attachment rows without the Data/ThumbnailData blobs for message rendering.</summary>
		Task<IEnumerable<ChatAttachment>> GetMetadataByMessageIdsAsync(IEnumerable<string> chatMessageIds);
	}

	public interface IChatMessageReactionRepository : IRepository<ChatMessageReaction>
	{
		Task<IEnumerable<ChatMessageReaction>> GetByMessageIdsAsync(IEnumerable<string> chatMessageIds);

		Task<bool> DeleteReactionAsync(string chatMessageId, int participantType, string userId, int? unitId, string emoji, CancellationToken cancellationToken);
	}

	public interface IChatMessageMentionRepository : IRepository<ChatMessageMention>
	{
		Task<IEnumerable<ChatMessageMention>> GetByMessageIdAsync(string chatMessageId);
	}

	public interface IChatMessageAckRepository : IRepository<ChatMessageAck>
	{
		Task<IEnumerable<ChatMessageAck>> GetByMessageIdAsync(string chatMessageId);

		Task<IEnumerable<ChatMessageAck>> GetPendingByUserIdAsync(int departmentId, string userId);

		/// <summary>Stamps AcknowledgedOn on the user's pending ack rows for a message; returns rows affected.</summary>
		Task<int> AcknowledgeAsync(string chatMessageId, string userId, DateTime acknowledgedOn);

		/// <summary>Single bulk multi-row INSERT of provisioned ack rows (chunked); returns rows written.</summary>
		Task<int> BulkInsertAcksAsync(IEnumerable<ChatMessageAck> acks, CancellationToken cancellationToken);
	}

	public interface IChatMessageFlagRepository : IRepository<ChatMessageFlag>
	{
		Task<IEnumerable<ChatMessageFlag>> GetByStatusAsync(int departmentId, int status, int page, int pageSize);

		/// <summary>The active (Open) flag by this user on this message, when one exists (dedupe).</summary>
		Task<ChatMessageFlag> GetActiveFlagAsync(string chatMessageId, string flaggedByUserId);
	}

	public interface IChatModerationActionRepository : IRepository<ChatModerationAction>
	{
		Task<IEnumerable<ChatModerationAction>> GetByDepartmentAsync(int departmentId, string chatChannelId, int page, int pageSize);
	}

	public interface IChatDepartmentSettingRepository : IRepository<ChatDepartmentSetting>
	{
		Task<ChatDepartmentSetting> GetByDepartmentIdAsync(int departmentId);
	}

	public interface IChatExportRepository : IRepository<ChatExport>
	{
		Task<IEnumerable<ChatExport>> GetQueuedAsync();

		/// <summary>Export rows without the result Data blob for listing.</summary>
		Task<IEnumerable<ChatExport>> GetMetadataByDepartmentIdAsync(int departmentId);

		/// <summary>Atomically moves a queued export to Running; true only for the worker that won the row.</summary>
		Task<bool> ClaimChatExportAsync(string chatExportId);

		/// <summary>Returns Running exports older than the given age to Queued (crashed-worker recovery); rows requeued.</summary>
		Task<int> RequeueStaleRunningChatExportsAsync(TimeSpan stale);

		/// <summary>Hard-deletes export rows (incl. the result Data blob) requested before the cutoff; rows deleted.</summary>
		Task<int> DeleteOldChatExportsAsync(DateTime olderThanUtc);
	}
}
