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

		/// <summary>Channels in the department carrying a per-channel retention override.</summary>
		Task<IEnumerable<ChatChannel>> GetWithRetentionOverrideAsync(int departmentId);
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
	}

	public interface IChatMessageEditRepository : IRepository<ChatMessageEdit>
	{
		Task<IEnumerable<ChatMessageEdit>> GetByMessageIdAsync(string chatMessageId);
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
	}

	public interface IChatMessageFlagRepository : IRepository<ChatMessageFlag>
	{
		Task<IEnumerable<ChatMessageFlag>> GetByStatusAsync(int departmentId, int status, int page, int pageSize);
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
	}
}
