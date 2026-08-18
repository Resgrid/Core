using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.Chat;

#region Result Objects

/// <summary>
/// Gets the chat channels for the current user
/// </summary>
public class GetChatChannelsResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public List<ChatChannelResultData> Data { get; set; }

	/// <summary>
	/// Default constructor
	/// </summary>
	public GetChatChannelsResult()
	{
		Data = new List<ChatChannelResultData>();
	}
}

/// <summary>
/// Gets a single chat channel
/// </summary>
public class GetChatChannelResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public ChatChannelResultData Data { get; set; }
}

/// <summary>
/// Gets a page of chat messages
/// </summary>
public class GetChatMessagesResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public List<ChatMessageResultData> Data { get; set; }

	/// <summary>
	/// Default constructor
	/// </summary>
	public GetChatMessagesResult()
	{
		Data = new List<ChatMessageResultData>();
	}
}

/// <summary>
/// Gets a single chat message
/// </summary>
public class GetChatMessageResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public ChatMessageResultData Data { get; set; }
}

/// <summary>
/// Gets the members of a chat channel
/// </summary>
public class GetChatMembersResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public List<ChatMemberResultData> Data { get; set; }

	/// <summary>
	/// Default constructor
	/// </summary>
	public GetChatMembersResult()
	{
		Data = new List<ChatMemberResultData>();
	}
}

/// <summary>
/// Gets acknowledgment rows for an urgent chat message (or the user's pending acks)
/// </summary>
public class GetChatAcksResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public List<ChatAckResultData> Data { get; set; }

	/// <summary>
	/// Default constructor
	/// </summary>
	public GetChatAcksResult()
	{
		Data = new List<ChatAckResultData>();
	}
}

/// <summary>
/// Gets flagged chat messages for moderator review
/// </summary>
public class GetChatFlagsResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public List<ChatFlagResultData> Data { get; set; }

	/// <summary>
	/// Default constructor
	/// </summary>
	public GetChatFlagsResult()
	{
		Data = new List<ChatFlagResultData>();
	}
}

/// <summary>
/// Gets the chat moderation audit trail
/// </summary>
public class GetChatModerationActionsResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public List<ChatModerationActionResultData> Data { get; set; }

	/// <summary>
	/// Default constructor
	/// </summary>
	public GetChatModerationActionsResult()
	{
		Data = new List<ChatModerationActionResultData>();
	}
}

/// <summary>
/// Gets the per-department chat settings
/// </summary>
public class GetChatSettingsResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public ChatSettingsResultData Data { get; set; }
}

/// <summary>
/// Gets the chat transcript export jobs for a department
/// </summary>
public class GetChatExportsResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public List<ChatExportResultData> Data { get; set; }

	/// <summary>
	/// Default constructor
	/// </summary>
	public GetChatExportsResult()
	{
		Data = new List<ChatExportResultData>();
	}
}

/// <summary>
/// Gets GIF search results from the configured GIF provider
/// </summary>
public class GetGifSearchResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public List<GifResultData> Data { get; set; }

	/// <summary>
	/// Default constructor
	/// </summary>
	public GetGifSearchResult()
	{
		Data = new List<GifResultData>();
	}
}

/// <summary>
/// Gets chat presence (which of the requested users are currently online)
/// </summary>
public class GetChatPresenceResult : StandardApiResponseV4Base
{
	/// <summary>
	/// UserIds from the request that are currently online
	/// </summary>
	public List<string> OnlineUserIds { get; set; }

	/// <summary>
	/// Default constructor
	/// </summary>
	public GetChatPresenceResult()
	{
		OnlineUserIds = new List<string>();
	}
}

/// <summary>
/// Result of creating (or finding) a chat channel
/// </summary>
public class ChatChannelCreatedResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public ChatChannelResultData Data { get; set; }
}

/// <summary>
/// Result of sending a chat message
/// </summary>
public class ChatMessageSentResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public ChatMessageResultData Data { get; set; }
}

/// <summary>
/// Result of a simple chat write operation
/// </summary>
public class ChatActionResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Whether the operation succeeded
	/// </summary>
	public bool Success { get; set; }
}

/// <summary>
/// Result of uploading a chat attachment
/// </summary>
public class ChatAttachmentUploadedResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Identifier of the created attachment
	/// </summary>
	public string ChatAttachmentId { get; set; }
}

/// <summary>
/// Gets the caller's chatbot conversation channel
/// </summary>
public class ChatbotChannelResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public ChatbotChannelResultData Data { get; set; }
}

/// <summary>
/// Result of sending a message to the chatbot
/// </summary>
public class ChatbotMessageSentResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public ChatbotMessageSentResultData Data { get; set; }
}

/// <summary>
/// Result of resetting the chatbot conversational session
/// </summary>
public class ChatbotSessionResetResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Whether the session was reset
	/// </summary>
	public bool Success { get; set; }
}

#endregion Result Objects

#region Result Data

/// <summary>
/// Chatbot conversation channel data
/// </summary>
public class ChatbotChannelResultData
{
	/// <summary>
	/// Chat channel identifier
	/// </summary>
	public string ChatChannelId { get; set; }

	/// <summary>
	/// Name of the channel
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Highest message sequence in the channel
	/// </summary>
	public long LastMessageSeq { get; set; }

	/// <summary>
	/// When the last message was sent
	/// </summary>
	public DateTime? LastMessageOn { get; set; }
}

/// <summary>
/// Chatbot message send data
/// </summary>
public class ChatbotMessageSentResultData
{
	/// <summary>
	/// Chat message identifier
	/// </summary>
	public string ChatMessageId { get; set; }

	/// <summary>
	/// Per-channel monotonic message sequence
	/// </summary>
	public long MessageSeq { get; set; }

	/// <summary>
	/// When the message was sent (UTC)
	/// </summary>
	public DateTime SentOn { get; set; }
}

/// <summary>
/// Chat channel data
/// </summary>
public class ChatChannelResultData
{
	/// <summary>
	/// Chat channel identifier
	/// </summary>
	public string ChatChannelId { get; set; }

	/// <summary>
	/// Channel type (0 = DirectMessage, 1 = AdHocGroup, 2 = DepartmentDefault, 3 = GroupDefault, 4 = CustomLocked, 5 = Incident, 6 = IncidentLane, 7 = IncidentCommand, 8 = Chatbot, 9 = IncidentLeads, 10 = IncidentDispatch, 11 = UnitDispatch, 12 = IncidentCommanderLine)
	/// </summary>
	public int ChannelType { get; set; }

	/// <summary>
	/// Name of the channel
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Topic of the channel
	/// </summary>
	public string Topic { get; set; }

	/// <summary>
	/// Group anchor for GroupDefault channels
	/// </summary>
	public int? GroupId { get; set; }

	/// <summary>
	/// Call anchor for incident channels
	/// </summary>
	public int? CallId { get; set; }

	/// <summary>
	/// Command structure node anchor for incident lane channels
	/// </summary>
	public string CommandStructureNodeId { get; set; }

	/// <summary>
	/// Owner user for chatbot channels
	/// </summary>
	public string OwnerUserId { get; set; }

	/// <summary>
	/// Is the channel archived
	/// </summary>
	public bool IsArchived { get; set; }

	/// <summary>
	/// Is the channel locked (only moderators can post)
	/// </summary>
	public bool IsLocked { get; set; }

	/// <summary>
	/// Highest message sequence in the channel
	/// </summary>
	public long LastMessageSeq { get; set; }

	/// <summary>
	/// When the last message was sent
	/// </summary>
	public DateTime? LastMessageOn { get; set; }

	/// <summary>
	/// When the channel was created
	/// </summary>
	public DateTime CreatedOn { get; set; }

	/// <summary>
	/// Number of messages the current user has not read
	/// </summary>
	public long UnreadCount { get; set; }

	/// <summary>
	/// The current user's notification preference for this channel (0 = Default, 1 = All, 2 = MentionsOnly, 3 = Muted)
	/// </summary>
	public int NotificationPreference { get; set; }

	/// <summary>
	/// The current user's last read message sequence for this channel
	/// </summary>
	public long MyLastReadSeq { get; set; }
}

/// <summary>
/// Chat message data
/// </summary>
public class ChatMessageResultData
{
	/// <summary>
	/// Chat message identifier
	/// </summary>
	public string ChatMessageId { get; set; }

	/// <summary>
	/// Chat channel identifier the message belongs to
	/// </summary>
	public string ChatChannelId { get; set; }

	/// <summary>
	/// Department identifier
	/// </summary>
	public int DepartmentId { get; set; }

	/// <summary>
	/// Per-channel monotonic message sequence
	/// </summary>
	public long MessageSeq { get; set; }

	/// <summary>
	/// Sender participant type (0 = User, 1 = Unit, 2 = Bot)
	/// </summary>
	public int SenderParticipantType { get; set; }

	/// <summary>
	/// The human behind the message (null only for bot messages)
	/// </summary>
	public string SenderUserId { get; set; }

	/// <summary>
	/// Unit the message was sent as, when sent as a unit identity
	/// </summary>
	public int? SenderUnitId { get; set; }

	/// <summary>
	/// Display identity snapshot at send time
	/// </summary>
	public string SenderDisplayName { get; set; }

	/// <summary>
	/// Body of the message
	/// </summary>
	public string Body { get; set; }

	/// <summary>
	/// Message type (0 = Text, 1 = Image, 2 = Gif, 3 = Location, 4 = System, 5 = Bot)
	/// </summary>
	public int MessageType { get; set; }

	/// <summary>
	/// Message priority (0 = Normal, 1 = Urgent)
	/// </summary>
	public int Priority { get; set; }

	/// <summary>
	/// Root message when this is a thread reply
	/// </summary>
	public string ThreadRootMessageId { get; set; }

	/// <summary>
	/// Reply count maintained on thread roots
	/// </summary>
	public int ThreadReplyCount { get; set; }

	/// <summary>
	/// When the last thread reply was made
	/// </summary>
	public DateTime? LastThreadReplyOn { get; set; }

	/// <summary>
	/// Thread reply flagged to also appear in the main channel stream
	/// </summary>
	public bool AlsoSendToChannel { get; set; }

	/// <summary>
	/// JSON payload for link previews, GIFs or shared locations
	/// </summary>
	public string MetadataJson { get; set; }

	/// <summary>
	/// Client-supplied idempotency key
	/// </summary>
	public string ClientMessageId { get; set; }

	/// <summary>
	/// When the message was sent (UTC)
	/// </summary>
	public DateTime SentOn { get; set; }

	/// <summary>
	/// When the message was last edited
	/// </summary>
	public DateTime? EditedOn { get; set; }

	/// <summary>
	/// When the message was deleted (tombstone)
	/// </summary>
	public DateTime? DeletedOn { get; set; }

	/// <summary>
	/// Who deleted the message
	/// </summary>
	public string DeletedByUserId { get; set; }

	/// <summary>
	/// Whether the tombstone represents a moderation action
	/// </summary>
	public bool IsModerated { get; set; }

	/// <summary>
	/// When the message was pinned
	/// </summary>
	public DateTime? PinnedOn { get; set; }

	/// <summary>
	/// Who pinned the message
	/// </summary>
	public string PinnedByUserId { get; set; }

	/// <summary>
	/// Emoji reactions on this message
	/// </summary>
	public List<ChatReactionResultData> Reactions { get; set; }

	/// <summary>
	/// Attachment metadata for this message (no file data)
	/// </summary>
	public List<ChatAttachmentResultData> Attachments { get; set; }

	/// <summary>
	/// Default constructor
	/// </summary>
	public ChatMessageResultData()
	{
		Reactions = new List<ChatReactionResultData>();
		Attachments = new List<ChatAttachmentResultData>();
	}
}

/// <summary>
/// Chat attachment metadata (file data is downloaded separately)
/// </summary>
public class ChatAttachmentResultData
{
	/// <summary>
	/// Chat attachment identifier
	/// </summary>
	public string ChatAttachmentId { get; set; }

	/// <summary>
	/// Original file name
	/// </summary>
	public string FileName { get; set; }

	/// <summary>
	/// Mime content type of the file
	/// </summary>
	public string ContentType { get; set; }

	/// <summary>
	/// Size of the file in bytes
	/// </summary>
	public long Size { get; set; }
}

/// <summary>
/// An emoji reaction on a chat message
/// </summary>
public class ChatReactionResultData
{
	/// <summary>
	/// Unicode emoji string
	/// </summary>
	public string Emoji { get; set; }

	/// <summary>
	/// Participant type of the reactor (0 = User, 1 = Unit, 2 = Bot)
	/// </summary>
	public int ParticipantType { get; set; }

	/// <summary>
	/// UserId of the reactor
	/// </summary>
	public string UserId { get; set; }

	/// <summary>
	/// UnitId of the reactor when reacting as a unit
	/// </summary>
	public int? UnitId { get; set; }
}

/// <summary>
/// A chat channel member's state
/// </summary>
public class ChatMemberResultData
{
	/// <summary>
	/// Chat channel member identifier
	/// </summary>
	public string ChatChannelMemberId { get; set; }

	/// <summary>
	/// Chat channel identifier
	/// </summary>
	public string ChatChannelId { get; set; }

	/// <summary>
	/// Participant type (0 = User, 1 = Unit, 2 = Bot)
	/// </summary>
	public int ParticipantType { get; set; }

	/// <summary>
	/// UserId of the member when a person
	/// </summary>
	public string UserId { get; set; }

	/// <summary>
	/// UnitId of the member when a unit-shared identity
	/// </summary>
	public int? UnitId { get; set; }

	/// <summary>
	/// Display identity override for the member
	/// </summary>
	public string DisplayNameOverride { get; set; }

	/// <summary>
	/// Is the member a channel moderator
	/// </summary>
	public bool IsModerator { get; set; }

	/// <summary>
	/// When the member joined the channel
	/// </summary>
	public DateTime JoinedOn { get; set; }

	/// <summary>
	/// Set when the participant left or was removed
	/// </summary>
	public DateTime? RemovedOn { get; set; }

	/// <summary>
	/// Highest message sequence this member has read (moderators only)
	/// </summary>
	public long? LastReadSeq { get; set; }

	/// <summary>
	/// When the member last advanced their read pointer
	/// </summary>
	public DateTime? LastReadOn { get; set; }

	/// <summary>
	/// Highest message sequence delivered to any of this member's devices (moderators only)
	/// </summary>
	public long? LastDeliveredSeq { get; set; }

	/// <summary>
	/// Member cannot post until this UTC time (null = not muted, moderators only)
	/// </summary>
	public DateTime? MutedUntil { get; set; }

	/// <summary>
	/// Is the member banned from the channel (moderators only)
	/// </summary>
	public bool? IsBanned { get; set; }

	/// <summary>
	/// The member's notification preference (0 = Default, 1 = All, 2 = MentionsOnly, 3 = Muted)
	/// </summary>
	public int NotificationPreference { get; set; }
}

/// <summary>
/// An acknowledgment row for an urgent chat message
/// </summary>
public class ChatAckResultData
{
	/// <summary>
	/// Chat message ack identifier
	/// </summary>
	public string ChatMessageAckId { get; set; }

	/// <summary>
	/// Chat message identifier the ack belongs to
	/// </summary>
	public string ChatMessageId { get; set; }

	/// <summary>
	/// Chat channel identifier
	/// </summary>
	public string ChatChannelId { get; set; }

	/// <summary>
	/// UserId the acknowledgment is required from
	/// </summary>
	public string UserId { get; set; }

	/// <summary>
	/// The unit this ack requirement was expanded from
	/// </summary>
	public int? UnitId { get; set; }

	/// <summary>
	/// When the acknowledgment was required (message send time)
	/// </summary>
	public DateTime RequiredOn { get; set; }

	/// <summary>
	/// When the user acknowledged (null = still pending)
	/// </summary>
	public DateTime? AcknowledgedOn { get; set; }

	/// <summary>
	/// Display name of the user the acknowledgment is required from (populated by GetAcks)
	/// </summary>
	public string DisplayName { get; set; }
}

/// <summary>
/// A user report ("flag") of a chat message
/// </summary>
public class ChatFlagResultData
{
	/// <summary>
	/// Chat message flag identifier
	/// </summary>
	public string ChatMessageFlagId { get; set; }

	/// <summary>
	/// Chat message identifier that was flagged
	/// </summary>
	public string ChatMessageId { get; set; }

	/// <summary>
	/// Chat channel identifier
	/// </summary>
	public string ChatChannelId { get; set; }

	/// <summary>
	/// UserId of the flagger
	/// </summary>
	public string FlaggedByUserId { get; set; }

	/// <summary>
	/// Reason for the flag (0 = Other, 1 = Inappropriate, 2 = Harassment, 3 = Spam, 4 = SensitiveInformation, 5 = PolicyViolation)
	/// </summary>
	public int Reason { get; set; }

	/// <summary>
	/// Optional note from the flagger
	/// </summary>
	public string Note { get; set; }

	/// <summary>
	/// When the message was flagged
	/// </summary>
	public DateTime FlaggedOn { get; set; }

	/// <summary>
	/// Flag status (0 = Open, 1 = Reviewed, 2 = Dismissed, 3 = ActionTaken)
	/// </summary>
	public int Status { get; set; }

	/// <summary>
	/// UserId of the reviewing moderator
	/// </summary>
	public string ReviewedByUserId { get; set; }

	/// <summary>
	/// When the flag was reviewed
	/// </summary>
	public DateTime? ReviewedOn { get; set; }

	/// <summary>
	/// Note from the reviewing moderator
	/// </summary>
	public string ResolutionNote { get; set; }
}

/// <summary>
/// An immutable chat moderation audit record
/// </summary>
public class ChatModerationActionResultData
{
	/// <summary>
	/// Chat moderation action identifier
	/// </summary>
	public string ChatModerationActionId { get; set; }

	/// <summary>
	/// Chat channel identifier the action applies to
	/// </summary>
	public string ChatChannelId { get; set; }

	/// <summary>
	/// Chat message identifier the action applies to
	/// </summary>
	public string ChatMessageId { get; set; }

	/// <summary>
	/// UserId the action targeted
	/// </summary>
	public string TargetUserId { get; set; }

	/// <summary>
	/// UnitId the action targeted
	/// </summary>
	public int? TargetUnitId { get; set; }

	/// <summary>
	/// Moderation action type (maps to ChatModerationActionType)
	/// </summary>
	public int ActionType { get; set; }

	/// <summary>
	/// UserId of the moderator that performed the action
	/// </summary>
	public string PerformedByUserId { get; set; }

	/// <summary>
	/// When the action was performed
	/// </summary>
	public DateTime PerformedOn { get; set; }

	/// <summary>
	/// Reason supplied for the action
	/// </summary>
	public string Reason { get; set; }

	/// <summary>
	/// Structured details of the action
	/// </summary>
	public string DetailsJson { get; set; }
}

/// <summary>
/// Per-department chat settings
/// </summary>
public class ChatSettingsResultData
{
	/// <summary>
	/// Chat department setting identifier
	/// </summary>
	public string ChatDepartmentSettingId { get; set; }

	/// <summary>
	/// Days to retain messages (0 = keep forever)
	/// </summary>
	public int RetentionDays { get; set; }

	/// <summary>
	/// Are image attachments allowed
	/// </summary>
	public bool AllowImages { get; set; }

	/// <summary>
	/// Are GIFs allowed
	/// </summary>
	public bool AllowGifs { get; set; }

	/// <summary>
	/// Is location sharing allowed
	/// </summary>
	public bool AllowLocationSharing { get; set; }

	/// <summary>
	/// When true, urgent messages notify even members who muted the channel
	/// </summary>
	public bool UrgentOverridesMute { get; set; }

	/// <summary>
	/// Maximum attachment size in megabytes
	/// </summary>
	public int MaxAttachmentSizeMb { get; set; }

	/// <summary>
	/// Is the chatbot enabled for the department
	/// </summary>
	public bool ChatbotEnabled { get; set; }
}

/// <summary>
/// A chat transcript export job (no result data blob)
/// </summary>
public class ChatExportResultData
{
	/// <summary>
	/// Chat export identifier
	/// </summary>
	public string ChatExportId { get; set; }

	/// <summary>
	/// Channel the export is limited to (null = all department channels)
	/// </summary>
	public string ChatChannelId { get; set; }

	/// <summary>
	/// UserId that requested the export
	/// </summary>
	public string RequestedByUserId { get; set; }

	/// <summary>
	/// When the export was requested
	/// </summary>
	public DateTime RequestedOn { get; set; }

	/// <summary>
	/// Start of the export date range
	/// </summary>
	public DateTime? StartDate { get; set; }

	/// <summary>
	/// End of the export date range
	/// </summary>
	public DateTime? EndDate { get; set; }

	/// <summary>
	/// Export format (0 = Json, 1 = Csv, 2 = Zip)
	/// </summary>
	public int Format { get; set; }

	/// <summary>
	/// Export status (0 = Queued, 1 = Running, 2 = Complete, 3 = Failed)
	/// </summary>
	public int Status { get; set; }

	/// <summary>
	/// When the export completed
	/// </summary>
	public DateTime? CompletedOn { get; set; }

	/// <summary>
	/// Error message when the export failed
	/// </summary>
	public string Error { get; set; }
}

/// <summary>
/// A GIF search hit from the configured GIF provider
/// </summary>
public class GifResultData
{
	/// <summary>
	/// Provider identifier for the GIF
	/// </summary>
	public string Id { get; set; }

	/// <summary>
	/// Title of the GIF
	/// </summary>
	public string Title { get; set; }

	/// <summary>
	/// Small preview/thumbnail url for the picker grid
	/// </summary>
	public string PreviewUrl { get; set; }

	/// <summary>
	/// Full GIF url to embed in the message metadata
	/// </summary>
	public string GifUrl { get; set; }

	/// <summary>
	/// Width of the GIF in pixels
	/// </summary>
	public int Width { get; set; }

	/// <summary>
	/// Height of the GIF in pixels
	/// </summary>
	public int Height { get; set; }
}

#endregion Result Data

#region Inputs

/// <summary>
/// Input to create (or find) a 1:1 direct message channel
/// </summary>
public class CreateDirectMessageInput
{
	/// <summary>
	/// Target user for the DM (mutually exclusive with TargetUnitId)
	/// </summary>
	public string TargetUserId { get; set; }

	/// <summary>
	/// Target unit for the DM (mutually exclusive with TargetUserId)
	/// </summary>
	public int? TargetUnitId { get; set; }
}

/// <summary>
/// Input to open the caller's private line to an incident's current commander ("Message the IC")
/// </summary>
public class CreateIncidentCommanderLineInput
{
	/// <summary>
	/// The call whose current Incident Commander should be messaged
	/// </summary>
	public int CallId { get; set; }

	/// <summary>
	/// Open the line as this unit rather than as the calling user (the caller must crew the unit)
	/// </summary>
	public int? AsUnitId { get; set; }
}

/// <summary>
/// Input to create an ad-hoc group channel
/// </summary>
public class CreateAdHocChannelInput
{
	/// <summary>
	/// Name of the channel. Optional — when omitted the server names the group after its members.
	/// </summary>
	[StringLength(100)]
	public string Name { get; set; }

	/// <summary>
	/// UserIds of the initial members
	/// </summary>
	[Required]
	public List<string> MemberUserIds { get; set; }
}

/// <summary>
/// Input to create a permission-locked custom channel
/// </summary>
public class CreateCustomChannelInput
{
	/// <summary>
	/// Name of the channel
	/// </summary>
	[Required]
	[StringLength(100)]
	public string Name { get; set; }

	/// <summary>
	/// Topic of the channel
	/// </summary>
	[StringLength(500)]
	public string Topic { get; set; }

	/// <summary>
	/// Access rules for the channel (OR-evaluated)
	/// </summary>
	public List<ChatAccessRuleInput> Rules { get; set; }
}

/// <summary>
/// An access rule for a custom locked channel
/// </summary>
public class ChatAccessRuleInput
{
	/// <summary>
	/// Rule type (0 = GroupMembership, 1 = Role, 2 = User)
	/// </summary>
	[Range(0, 2)]
	public int RuleType { get; set; }

	/// <summary>
	/// Group for GroupMembership rules
	/// </summary>
	public int? GroupId { get; set; }

	/// <summary>
	/// Personnel role for Role rules
	/// </summary>
	public int? PersonnelRoleId { get; set; }

	/// <summary>
	/// User for User rules
	/// </summary>
	public string UserId { get; set; }
}

/// <summary>
/// Input to update a channel's name/topic
/// </summary>
public class UpdateChannelInput
{
	/// <summary>
	/// New name for the channel
	/// </summary>
	[StringLength(100)]
	public string Name { get; set; }

	/// <summary>
	/// New topic for the channel
	/// </summary>
	[StringLength(500)]
	public string Topic { get; set; }
}

/// <summary>
/// Input to add members to a channel
/// </summary>
public class AddMembersInput
{
	/// <summary>
	/// UserIds to add to the channel
	/// </summary>
	[Required]
	public List<string> UserIds { get; set; }
}

/// <summary>
/// Input to set the current user's notification preference for a channel
/// </summary>
public class SetNotificationPreferenceInput
{
	/// <summary>
	/// Notification preference (0 = Default, 1 = All, 2 = MentionsOnly, 3 = Muted)
	/// </summary>
	[Range(0, 3)]
	public int Preference { get; set; }
}

/// <summary>
/// Input to send a chat message
/// </summary>
public class SendChatMessageInput
{
	/// <summary>
	/// Client idempotency key; resends return the original message
	/// </summary>
	[StringLength(100)]
	public string ClientMessageId { get; set; }

	/// <summary>
	/// Body of the message. Image messages carry the picture as an attachment and send an
	/// empty body (the body is only an optional caption), so empty strings are accepted —
	/// null is still rejected.
	/// </summary>
	[Required(AllowEmptyStrings = true)]
	[StringLength(4000)]
	public string Body { get; set; }

	/// <summary>
	/// Message type (0 = Text, 1 = Image, 2 = Gif, 3 = Location)
	/// </summary>
	[Range(0, 3)]
	public int MessageType { get; set; }

	/// <summary>
	/// Message priority (0 = Normal, 1 = Urgent)
	/// </summary>
	[Range(0, 1)]
	public int Priority { get; set; }

	/// <summary>
	/// Send as a unit identity ("Engine 6")
	/// </summary>
	public int? AsUnitId { get; set; }

	/// <summary>
	/// Send as the Incident Commander identity
	/// </summary>
	public bool AsIncidentCommander { get; set; }

	/// <summary>
	/// Root message when replying in a thread
	/// </summary>
	public string ThreadRootMessageId { get; set; }

	/// <summary>
	/// Thread reply flagged to also appear in the main channel stream
	/// </summary>
	public bool AlsoSendToChannel { get; set; }

	/// <summary>
	/// JSON payload for link previews, GIFs or shared locations
	/// </summary>
	[StringLength(8000)]
	public string MetadataJson { get; set; }

	/// <summary>
	/// Resolved mentions from the client (targets validated server-side)
	/// </summary>
	public List<ChatMentionInput> Mentions { get; set; }
}

/// <summary>
/// An @mention inside a chat message
/// </summary>
public class ChatMentionInput
{
	/// <summary>
	/// Mention type (0 = User, 1 = Unit, 2 = Role, 3 = Group, 4 = Everyone)
	/// </summary>
	[Range(0, 4)]
	public int MentionType { get; set; }

	/// <summary>
	/// Mentioned user for User mentions
	/// </summary>
	public string TargetUserId { get; set; }

	/// <summary>
	/// Mentioned unit for Unit mentions
	/// </summary>
	public int? TargetUnitId { get; set; }

	/// <summary>
	/// Mentioned role for Role mentions
	/// </summary>
	public int? TargetRoleId { get; set; }

	/// <summary>
	/// Mentioned group for Group mentions
	/// </summary>
	public int? TargetGroupId { get; set; }
}

/// <summary>
/// Input to edit a message's body
/// </summary>
public class EditMessageInput
{
	/// <summary>
	/// New body for the message
	/// </summary>
	[Required]
	[StringLength(4000)]
	public string Body { get; set; }
}

/// <summary>
/// Input to add an emoji reaction to a message
/// </summary>
public class AddReactionInput
{
	/// <summary>
	/// Unicode emoji string (e.g. "👍")
	/// </summary>
	[Required]
	[StringLength(64)]
	public string Emoji { get; set; }
}

/// <summary>
/// Input to advance the read pointer for a channel
/// </summary>
public class MarkReadInput
{
	/// <summary>
	/// Highest message sequence read
	/// </summary>
	public long Seq { get; set; }

	/// <summary>
	/// Advance the read pointer as this unit identity
	/// </summary>
	public int? AsUnitId { get; set; }
}

/// <summary>
/// Input to flag a message for moderator review
/// </summary>
public class FlagMessageInput
{
	/// <summary>
	/// Reason for the flag (0 = Other, 1 = Inappropriate, 2 = Harassment, 3 = Spam, 4 = SensitiveInformation, 5 = PolicyViolation)
	/// </summary>
	[Range(0, 5)]
	public int Reason { get; set; }

	/// <summary>
	/// Optional note describing the issue
	/// </summary>
	[StringLength(1000)]
	public string Note { get; set; }
}

/// <summary>
/// Input to resolve a message flag
/// </summary>
public class ResolveFlagInput
{
	/// <summary>
	/// Resolution status (1 = Reviewed, 2 = Dismissed, 3 = ActionTaken)
	/// </summary>
	[Range(1, 3)]
	public int Resolution { get; set; }

	/// <summary>
	/// Note from the reviewing moderator
	/// </summary>
	[StringLength(1000)]
	public string ResolutionNote { get; set; }
}

/// <summary>
/// Input to mute a user in a channel
/// </summary>
public class MuteUserInput
{
	/// <summary>
	/// UserId to mute
	/// </summary>
	[Required]
	public string TargetUserId { get; set; }

	/// <summary>
	/// Mute until this UTC time (null = unmute)
	/// </summary>
	public DateTime? MutedUntil { get; set; }
}

/// <summary>
/// Input to ban (or unban) a user from a channel
/// </summary>
public class BanUserInput
{
	/// <summary>
	/// UserId to ban or unban
	/// </summary>
	[Required]
	public string TargetUserId { get; set; }

	/// <summary>
	/// True to ban, false to unban
	/// </summary>
	public bool Banned { get; set; }
}

/// <summary>
/// Input to lock (or unlock) a channel
/// </summary>
public class LockChannelInput
{
	/// <summary>
	/// True to lock, false to unlock
	/// </summary>
	public bool Locked { get; set; }

	/// <summary>
	/// Reason for the lock/unlock
	/// </summary>
	[StringLength(1000)]
	public string Reason { get; set; }
}

/// <summary>
/// Input to update the per-department chat settings
/// </summary>
public class UpdateChatSettingsInput
{
	/// <summary>
	/// Days to retain messages (0 = keep forever)
	/// </summary>
	[Range(0, 3650)]
	public int RetentionDays { get; set; }

	/// <summary>
	/// Are image attachments allowed
	/// </summary>
	public bool AllowImages { get; set; }

	/// <summary>
	/// Are GIFs allowed
	/// </summary>
	public bool AllowGifs { get; set; }

	/// <summary>
	/// Is location sharing allowed
	/// </summary>
	public bool AllowLocationSharing { get; set; }

	/// <summary>
	/// When true, urgent messages notify even members who muted the channel
	/// </summary>
	public bool UrgentOverridesMute { get; set; }

	/// <summary>
	/// Maximum attachment size in megabytes
	/// </summary>
	[Range(1, 100)]
	public int MaxAttachmentSizeMb { get; set; }

	/// <summary>
	/// Is the chatbot enabled for the department
	/// </summary>
	public bool ChatbotEnabled { get; set; }
}

/// <summary>
/// Input to request a chat transcript export
/// </summary>
public class RequestExportInput
{
	/// <summary>
	/// Limit the export to one channel (null = all department channels)
	/// </summary>
	public string ChatChannelId { get; set; }

	/// <summary>
	/// Start of the export date range
	/// </summary>
	public DateTime? StartDate { get; set; }

	/// <summary>
	/// End of the export date range
	/// </summary>
	public DateTime? EndDate { get; set; }

	/// <summary>
	/// Export format (0 = Json, 1 = Csv, 2 = Zip)
	/// </summary>
	[Range(0, 2)]
	public int Format { get; set; }
}

public class VerifyExportMfaInput
{
	/// <summary>
	/// The caller's current authenticator (TOTP) code, used to establish a recent-MFA step-up proof for PII exports
	/// </summary>
	[Required]
	public string TotpCode { get; set; }
}

/// <summary>
/// A command-board question for the incident assistant. Answered synchronously so the board can show
/// the reply in place instead of waiting on the chat channel round-trip.
/// </summary>
public class AskIncidentAssistantInput
{
	/// <summary>
	/// The commander's question, in their own words ("PAR", "who's in Division A", "what am I missing")
	/// </summary>
	[Required]
	public string Question { get; set; }

	/// <summary>
	/// Call id of the incident the question is about (the board the caller has open)
	/// </summary>
	public int CallId { get; set; }
}

#endregion Inputs

#region Incident assistant

/// <summary>
/// Answer to a command-board question
/// </summary>
public class IncidentAssistantAnswerResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public IncidentAssistantAnswerResultData Data { get; set; }
}

/// <summary>
/// The assistant's answer plus what it understood the question to be
/// </summary>
public class IncidentAssistantAnswerResultData
{
	/// <summary>
	/// The answer text, ready to display
	/// </summary>
	public string Answer { get; set; }

	/// <summary>
	/// Name of the intent the question was classified as ("IncidentPar", "Unknown", ...)
	/// </summary>
	public string Intent { get; set; }

	/// <summary>
	/// Classifier confidence, 0.0 - 1.0
	/// </summary>
	public double Confidence { get; set; }

	/// <summary>
	/// False when the assistant couldn't answer (unresolved incident, no permission, rate limited)
	/// </summary>
	public bool Processed { get; set; }
}

/// <summary>
/// Suggested questions for an incident, tailored to its type
/// </summary>
public class IncidentAssistantSuggestionsResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public IncidentAssistantSuggestionsResultData Data { get; set; }
}

/// <summary>
/// Incident-type suggestions the command board shows as one-tap prompts
/// </summary>
public class IncidentAssistantSuggestionsResultData
{
	/// <summary>
	/// Inferred incident family ("Structure fire", "Wildland fire", "Mass casualty incident", ...)
	/// </summary>
	public string IncidentType { get; set; }

	/// <summary>
	/// Machine-readable incident family key, matching the app's own playbook ids
	/// </summary>
	public string IncidentTypeKey { get; set; }

	/// <summary>
	/// Questions worth putting in front of the commander for this incident type
	/// </summary>
	public List<string> Questions { get; set; } = new List<string>();
}

#endregion Incident assistant
