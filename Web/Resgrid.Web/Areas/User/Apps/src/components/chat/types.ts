// DTO interfaces mirroring Web\Resgrid.Web.Services\Models\v4\Chat\ChatApiModels.cs.
// The v4 API serializes with Newtonsoft's DefaultContractResolver, so property names are PascalCase.

export const ChatChannelType = {
  DirectMessage: 0,
  AdHocGroup: 1,
  DepartmentDefault: 2,
  GroupDefault: 3,
  CustomLocked: 4,
  Incident: 5,
  IncidentLane: 6,
  IncidentCommand: 7,
  Chatbot: 8,
  IncidentLeads: 9,
  IncidentDispatch: 10,
  UnitDispatch: 11,
} as const;

export const ChatMessageType = {
  Text: 0,
  Image: 1,
  Gif: 2,
  Location: 3,
  System: 4,
  Bot: 5,
} as const;

export const ChatMessagePriority = {
  Normal: 0,
  Urgent: 1,
} as const;

export const ChatNotificationPreference = {
  Default: 0,
  All: 1,
  MentionsOnly: 2,
  Muted: 3,
} as const;

export const ChatFlagStatus = {
  Open: 0,
  Reviewed: 1,
  Dismissed: 2,
  ActionTaken: 3,
} as const;

// Server -> client event names. Mirror Core/Resgrid.Model/Events/ChatEvents.cs (ChatEventKinds)
// plus the hub-direct events emitted by the eventing host.
export const CHAT_HUB_EVENTS = {
  MessageReceived: 'chatMessageReceived',
  MessageEdited: 'chatMessageEdited',
  MessageDeleted: 'chatMessageDeleted',
  ReactionUpdated: 'chatReactionUpdated',
  ReceiptUpdated: 'chatReceiptUpdated',
  ChannelUpdated: 'chatChannelUpdated',
  ChannelProvisioned: 'chatChannelProvisioned',
  ModerationApplied: 'chatModerationApplied',
  AckRequired: 'chatMessageAckRequired',
  ThreadUpdated: 'chatThreadUpdated',
  ChatbotMessageReceived: 'chatbotMessageReceived',
  ChatbotTyping: 'chatbotTyping',
  AccessRevoked: 'chatAccessRevoked',
  Typing: 'chatTyping',
  PresenceChanged: 'chatPresenceChanged',
  Connected: 'onChatConnected',
} as const;

// Client -> server hub method names.
export const CHAT_HUB_METHODS = {
  Connect: 'Connect',
  Heartbeat: 'Heartbeat',
  JoinChannel: 'JoinChannel',
  LeaveChannel: 'LeaveChannel',
  Typing: 'Typing',
  MarkRead: 'MarkRead',
  SetActiveChannel: 'SetActiveChannel',
} as const;

// Client-side lifecycle for optimistic messages (not part of the wire DTO).
export type MessageSendStatus = 'pending' | 'sent' | 'failed';

export interface ApiResultBase {
  PageSize: number;
  Page: number;
  Status: string;
}

export interface ApiListResult<TData> extends ApiResultBase {
  Data: TData[];
}

export interface ApiItemResult<TData> extends ApiResultBase {
  Data: TData;
}

export interface ChatChannelDto {
  ChatChannelId: string;
  ChannelType: number;
  Name: string;
  Topic: string | null;
  GroupId: number | null;
  CallId: number | null;
  CommandStructureNodeId: string | null;
  OwnerUserId: string | null;
  IsArchived: boolean;
  IsLocked: boolean;
  LastMessageSeq: number;
  LastMessageOn: string | null;
  CreatedOn: string;
  UnreadCount: number;
  NotificationPreference: number;
  MyLastReadSeq: number;
}

export interface ChatAttachmentDto {
  ChatAttachmentId: string;
  FileName: string;
  ContentType: string;
  Size: number;
}

export interface ChatReactionDto {
  Emoji: string;
  ParticipantType: number;
  UserId: string | null;
  UnitId: number | null;
}

export interface ChatMessageDto {
  ChatMessageId: string;
  ChatChannelId: string;
  DepartmentId: number;
  MessageSeq: number;
  SenderParticipantType: number;
  SenderUserId: string | null;
  SenderUnitId: number | null;
  SenderDisplayName: string | null;
  Body: string | null;
  MessageType: number;
  Priority: number;
  ThreadRootMessageId: string | null;
  ThreadReplyCount: number;
  LastThreadReplyOn: string | null;
  AlsoSendToChannel: boolean;
  MetadataJson: string | null;
  ClientMessageId: string | null;
  SentOn: string;
  EditedOn: string | null;
  DeletedOn: string | null;
  DeletedByUserId: string | null;
  IsModerated: boolean;
  PinnedOn: string | null;
  PinnedByUserId: string | null;
  Reactions: ChatReactionDto[];
  Attachments: ChatAttachmentDto[];
  ClientStatus?: MessageSendStatus;
}

export interface ChatMemberDto {
  ChatChannelMemberId: string;
  ChatChannelId: string;
  ParticipantType: number;
  UserId: string | null;
  UnitId: number | null;
  DisplayNameOverride: string | null;
  IsModerator: boolean;
  JoinedOn: string;
  RemovedOn: string | null;
  LastReadSeq: number;
  LastReadOn: string | null;
  LastDeliveredSeq: number;
  MutedUntil: string | null;
  IsBanned: boolean;
  NotificationPreference: number;
}

export interface ChatAckDto {
  ChatMessageAckId: string;
  ChatMessageId: string;
  ChatChannelId: string;
  UserId: string;
  UnitId: number | null;
  RequiredOn: string;
  AcknowledgedOn: string | null;
}

export interface ChatFlagDto {
  ChatMessageFlagId: string;
  ChatMessageId: string;
  ChatChannelId: string;
  FlaggedByUserId: string;
  Reason: number;
  Note: string | null;
  FlaggedOn: string;
  Status: number;
  ReviewedByUserId: string | null;
  ReviewedOn: string | null;
  ResolutionNote: string | null;
}

export interface ChatModerationActionDto {
  ChatModerationActionId: string;
  ChatChannelId: string | null;
  ChatMessageId: string | null;
  TargetUserId: string | null;
  TargetUnitId: number | null;
  ActionType: number;
  PerformedByUserId: string;
  PerformedOn: string;
  Reason: string | null;
  DetailsJson: string | null;
}

export interface ChatSettingsDto {
  ChatDepartmentSettingId: string | null;
  RetentionDays: number;
  AllowImages: boolean;
  AllowGifs: boolean;
  AllowLocationSharing: boolean;
  UrgentOverridesMute: boolean;
  MaxAttachmentSizeMb: number;
  ChatbotEnabled: boolean;
}

export interface ChatExportDto {
  ChatExportId: string;
  ChatChannelId: string | null;
  RequestedByUserId: string;
  RequestedOn: string;
  StartDate: string | null;
  EndDate: string | null;
  Format: number;
  Status: number;
  CompletedOn: string | null;
  Error: string | null;
}

export interface GifDto {
  Id: string;
  Title: string;
  PreviewUrl: string;
  GifUrl: string;
  Width: number;
  Height: number;
}

export interface RecipientDto {
  Id: string;
  Type: string;
  Name: string;
}

export interface SendMessageOptions {
  Body: string;
  MessageType?: number;
  Priority?: number;
  ThreadRootMessageId?: string | null;
  AlsoSendToChannel?: boolean;
  MetadataJson?: string | null;
  ClientMessageId?: string;
  AsUnitId?: number | null;
  AsIncidentCommander?: boolean;
}

export interface ChatbotChannelInfo {
  ChatChannelId: string;
  Name: string;
  LastMessageSeq: number;
  LastMessageOn: string | null;
}

// ---- Hub event payloads ----

// Events relayed through the eventing Worker arrive as JSON *strings* serialized
// with Newtonsoft (PascalCase). chatTyping is emitted directly by the hub and arrives
// as an object camelCased by the default SignalR JSON protocol.

export interface HubMessagePayload {
  ChatMessageId: string;
  ChatChannelId: string;
  DepartmentId: number;
  MessageSeq: number;
  SenderParticipantType: number;
  SenderUserId: string | null;
  SenderUnitId: number | null;
  SenderDisplayName: string | null;
  Body: string | null;
  MessageType: number;
  Priority: number;
  ThreadRootMessageId: string | null;
  AlsoSendToChannel: boolean;
  MetadataJson: string | null;
  ClientMessageId: string | null;
  SentOn: string;
  EditedOn: string | null;
}

export interface HubDeletedPayload {
  ChatMessageId: string;
  ChatChannelId: string;
  MessageSeq: number;
  DeletedOn: string;
  DeletedByModerator: boolean;
  IsModerated: boolean;
}

export interface HubReactionPayload {
  ChatMessageId: string;
  ChatChannelId: string;
  Emoji: string;
  UserId: string | null;
  UnitId: number | null;
  Added: boolean;
}

export interface HubReceiptPayload {
  ChatMessageId?: string;
  ChatChannelId: string;
  Type: 'ack' | 'read';
  UserId: string;
  UnitId?: number | null;
  Seq?: number;
}

export interface HubThreadUpdatedPayload {
  ChatMessageId: string;
  ThreadReplyCount: number;
  LastThreadReplyOn: string;
}

export interface HubAckRequiredPayload {
  ChatMessageId: string;
  ChatChannelId: string;
  MessageSeq: number;
  RequiredCount: number;
  SenderUserId?: string | null;
}

export interface HubChatbotTypingPayload {
  ChatChannelId: string;
  IsTyping: boolean;
}

export interface HubAccessRevokedPayload {
  ChannelId: string;
  UserId: string;
}

export function toMessageDto(payload: HubMessagePayload): ChatMessageDto {
  return {
    ...payload,
    ThreadReplyCount: 0,
    LastThreadReplyOn: null,
    DeletedOn: null,
    DeletedByUserId: null,
    IsModerated: false,
    PinnedOn: null,
    PinnedByUserId: null,
    Reactions: [],
    Attachments: [],
  };
}

export function getCurrentUserId(): string {
  const value = (window as unknown as { userId?: string }).userId;
  return typeof value === 'string' ? value : '';
}

export function getCurrentDisplayName(): string {
  const value = (window as unknown as { rgUserDisplayName?: string }).rgUserDisplayName;
  return typeof value === 'string' ? value : '';
}

export function isDepartmentAdmin(): boolean {
  const value = (window as unknown as { rgIsDepartmentAdmin?: boolean | string }).rgIsDepartmentAdmin;
  return value === true || value === 'true' || value === 'True';
}
