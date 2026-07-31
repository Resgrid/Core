// Framework-free external store for chat state, consumed via useChatStore (useSyncExternalStore).
import {
  ChatMessageType,
  toMessageDto,
  type ChatChannelDto,
  type ChatMessageDto,
  type HubDeletedPayload,
  type HubMessagePayload,
  type HubReactionPayload,
  type HubReceiptPayload,
  type HubThreadUpdatedPayload,
} from './types';

export interface TypingEntry {
  userId: string;
  displayName: string;
  expiresAt: number;
}

export interface ChatState {
  chatAvailable: boolean;
  channelsLoaded: boolean;
  channels: ChatChannelDto[];
  messagesByChannel: Record<string, ChatMessageDto[]>;
  hasMoreByChannel: Record<string, boolean>;
  typingByChannel: Record<string, TypingEntry[]>;
  botTypingByChannel: Record<string, boolean>;
  onlineUserIds: string[];
  pendingAckMessageIds: string[];
  activeChannelId: string | null;
}

type Listener = () => void;

const TYPING_TTL_MS = 5000;

let state: ChatState = {
  chatAvailable: true,
  channelsLoaded: false,
  channels: [],
  messagesByChannel: {},
  hasMoreByChannel: {},
  typingByChannel: {},
  botTypingByChannel: {},
  onlineUserIds: [],
  pendingAckMessageIds: [],
  activeChannelId: null,
};

const listeners = new Set<Listener>();
let typingTimer: ReturnType<typeof setInterval> | null = null;

function emit(): void {
  for (const listener of listeners) {
    listener();
  }
}

function setState(next: Partial<ChatState>): void {
  state = { ...state, ...next };
  emit();
}

export const chatStore = {
  getState(): ChatState {
    return state;
  },
  subscribe(listener: Listener): () => void {
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  },
};

// ---- Message ordering + dedup ----

function messageKeyMatch(a: ChatMessageDto, b: ChatMessageDto): boolean {
  if (a.ChatMessageId && b.ChatMessageId && a.ChatMessageId === b.ChatMessageId) {
    return true;
  }
  if (a.ClientMessageId && b.ClientMessageId && a.ClientMessageId === b.ClientMessageId) {
    return true;
  }
  return false;
}

function sortMessages(messages: ChatMessageDto[]): ChatMessageDto[] {
  return [...messages].sort((a, b) => {
    if (a.MessageSeq !== b.MessageSeq) {
      return a.MessageSeq - b.MessageSeq;
    }
    return new Date(a.SentOn).getTime() - new Date(b.SentOn).getTime();
  });
}

function mergeMessages(existing: ChatMessageDto[], incoming: ChatMessageDto[]): ChatMessageDto[] {
  const result = [...existing];
  for (const message of incoming) {
    const index = result.findIndex((candidate) => messageKeyMatch(candidate, message));
    if (index >= 0) {
      // Prefer the message with the real (non-pending) sequence + newest data.
      result[index] = { ...result[index], ...message };
    } else {
      result.push(message);
    }
  }
  return sortMessages(result);
}

// ---- Channels ----

export function setChatAvailable(available: boolean): void {
  setState({ chatAvailable: available });
}

export function setChannels(channels: ChatChannelDto[]): void {
  setState({ channels, channelsLoaded: true });
}

export function upsertChannel(channel: ChatChannelDto): void {
  const index = state.channels.findIndex((candidate) => candidate.ChatChannelId === channel.ChatChannelId);
  const channels = [...state.channels];
  if (index >= 0) {
    channels[index] = { ...channels[index], ...channel };
  } else {
    channels.push(channel);
  }
  setState({ channels });
}

function patchChannel(channelId: string, patch: Partial<ChatChannelDto>): void {
  const index = state.channels.findIndex((candidate) => candidate.ChatChannelId === channelId);
  if (index < 0) {
    return;
  }
  const channels = [...state.channels];
  channels[index] = { ...channels[index], ...patch };
  setState({ channels });
}

export function setActiveChannel(channelId: string | null): void {
  setState({ activeChannelId: channelId });
}

// ---- Messages ----

export function setChannelMessages(channelId: string, messages: ChatMessageDto[], hasMore: boolean): void {
  setState({
    messagesByChannel: { ...state.messagesByChannel, [channelId]: sortMessages(messages) },
    hasMoreByChannel: { ...state.hasMoreByChannel, [channelId]: hasMore },
  });
}

export function prependChannelMessages(channelId: string, older: ChatMessageDto[], hasMore: boolean): void {
  const existing = state.messagesByChannel[channelId] ?? [];
  setState({
    messagesByChannel: { ...state.messagesByChannel, [channelId]: mergeMessages(existing, older) },
    hasMoreByChannel: { ...state.hasMoreByChannel, [channelId]: hasMore },
  });
}

export function upsertMessages(channelId: string, incoming: ChatMessageDto[]): void {
  const existing = state.messagesByChannel[channelId] ?? [];
  setState({
    messagesByChannel: { ...state.messagesByChannel, [channelId]: mergeMessages(existing, incoming) },
  });
}

export function upsertMessage(message: ChatMessageDto): void {
  upsertMessages(message.ChatChannelId, [message]);
}

export function applyHubMessage(payload: HubMessagePayload): void {
  const message = toMessageDto(payload);
  upsertMessage(message);

  const channel = state.channels.find((candidate) => candidate.ChatChannelId === message.ChatChannelId);
  if (channel) {
    const isActive = state.activeChannelId === message.ChatChannelId;
    const patch: Partial<ChatChannelDto> = {
      LastMessageSeq: Math.max(channel.LastMessageSeq, message.MessageSeq),
      LastMessageOn: message.SentOn,
    };
    if (!isActive) {
      patch.UnreadCount = Math.max(0, message.MessageSeq - channel.MyLastReadSeq);
    }
    patchChannel(message.ChatChannelId, patch);
  }
}

export function applyHubEdit(payload: HubMessagePayload): void {
  const existing = (state.messagesByChannel[payload.ChatChannelId] ?? []).find(
    (candidate) => candidate.ChatMessageId === payload.ChatMessageId,
  );
  if (!existing) {
    return;
  }
  upsertMessages(payload.ChatChannelId, [{ ...existing, Body: payload.Body, EditedOn: payload.EditedOn }]);
}

export function applyHubDelete(payload: HubDeletedPayload): void {
  const existing = (state.messagesByChannel[payload.ChatChannelId] ?? []).find(
    (candidate) => candidate.ChatMessageId === payload.ChatMessageId,
  );
  if (!existing) {
    return;
  }
  upsertMessages(payload.ChatChannelId, [
    { ...existing, DeletedOn: payload.DeletedOn, Body: null },
  ]);
}

export function applyHubReaction(payload: HubReactionPayload): void {
  const messages = state.messagesByChannel[payload.ChatChannelId] ?? [];
  const existing = messages.find((candidate) => candidate.ChatMessageId === payload.ChatMessageId);
  if (!existing) {
    return;
  }
  const reactions = existing.Reactions.filter(
    (reaction) => !(reaction.Emoji === payload.Emoji && reaction.UserId === payload.UserId && reaction.UnitId === payload.UnitId),
  );
  if (payload.Added) {
    reactions.push({
      Emoji: payload.Emoji,
      ParticipantType: payload.UnitId ? 1 : 0,
      UserId: payload.UserId,
      UnitId: payload.UnitId,
    });
  }
  upsertMessages(payload.ChatChannelId, [{ ...existing, Reactions: reactions }]);
}

export function applyHubReceipt(_payload: HubReceiptPayload): void {
  // Read/ack receipts drive per-member read pointers surfaced by GetMembers; the conversation
  // view refreshes members to render the "seen by" line, so nothing is mutated on the message here.
}

export function applyHubThreadUpdated(payload: HubThreadUpdatedPayload): void {
  for (const [channelId, messages] of Object.entries(state.messagesByChannel)) {
    const existing = messages.find((candidate) => candidate.ChatMessageId === payload.ChatMessageId);
    if (existing) {
      upsertMessages(channelId, [
        { ...existing, ThreadReplyCount: payload.ThreadReplyCount, LastThreadReplyOn: payload.LastThreadReplyOn },
      ]);
      return;
    }
  }
}

// ---- Read pointer / unread ----

export function markChannelRead(channelId: string, seq: number): void {
  const channel = state.channels.find((candidate) => candidate.ChatChannelId === channelId);
  if (!channel) {
    return;
  }
  patchChannel(channelId, {
    MyLastReadSeq: Math.max(channel.MyLastReadSeq, seq),
    UnreadCount: 0,
  });
}

export function totalUnread(): number {
  return state.channels.reduce((total, channel) => total + (channel.UnreadCount > 0 ? channel.UnreadCount : 0), 0);
}

// ---- Typing ----

function ensureTypingTimer(): void {
  if (typingTimer) {
    return;
  }
  typingTimer = setInterval(() => {
    pruneTyping();
  }, 1000);
}

function pruneTyping(): void {
  const now = Date.now();
  let changed = false;
  const next: Record<string, TypingEntry[]> = {};

  for (const [channelId, entries] of Object.entries(state.typingByChannel)) {
    const live = entries.filter((entry) => entry.expiresAt > now);
    if (live.length !== entries.length) {
      changed = true;
    }
    if (live.length > 0) {
      next[channelId] = live;
    }
  }

  if (changed) {
    setState({ typingByChannel: next });
  }

  if (Object.keys(next).length === 0 && typingTimer) {
    clearInterval(typingTimer);
    typingTimer = null;
  }
}

export function setTyping(channelId: string, userId: string, displayName: string, isTyping: boolean): void {
  const entries = (state.typingByChannel[channelId] ?? []).filter((entry) => entry.userId !== userId);
  if (isTyping) {
    entries.push({ userId, displayName, expiresAt: Date.now() + TYPING_TTL_MS });
  }
  const next = { ...state.typingByChannel };
  if (entries.length > 0) {
    next[channelId] = entries;
  } else {
    delete next[channelId];
  }
  setState({ typingByChannel: next });
  ensureTypingTimer();
}

export function setBotTyping(channelId: string, isTyping: boolean): void {
  setState({ botTypingByChannel: { ...state.botTypingByChannel, [channelId]: isTyping } });
}

// ---- Presence ----

export function setPresenceBulk(userIds: string[]): void {
  setState({ onlineUserIds: Array.from(new Set(userIds)) });
}

export function setPresence(userId: string, isOnline: boolean): void {
  const current = new Set(state.onlineUserIds);
  if (isOnline) {
    current.add(userId);
  } else {
    current.delete(userId);
  }
  setState({ onlineUserIds: Array.from(current) });
}

// ---- Urgent acks ----

export function addPendingAck(messageId: string): void {
  if (state.pendingAckMessageIds.includes(messageId)) {
    return;
  }
  setState({ pendingAckMessageIds: [...state.pendingAckMessageIds, messageId] });
}

export function setPendingAcks(messageIds: string[]): void {
  setState({ pendingAckMessageIds: Array.from(new Set(messageIds)) });
}

export function clearPendingAck(messageId: string): void {
  setState({ pendingAckMessageIds: state.pendingAckMessageIds.filter((id) => id !== messageId) });
}

// ---- Optimistic sends ----

const PENDING_SEQ_BASE = Number.MAX_SAFE_INTEGER - 100000;
let pendingCounter = 0;

export function createOptimisticMessage(
  channelId: string,
  departmentId: number,
  senderUserId: string,
  senderDisplayName: string,
  body: string,
  clientMessageId: string,
  messageType: number = ChatMessageType.Text,
  priority = 0,
  threadRootMessageId: string | null = null,
  metadataJson: string | null = null,
): ChatMessageDto {
  pendingCounter += 1;
  return {
    ChatMessageId: `pending-${clientMessageId}`,
    ChatChannelId: channelId,
    DepartmentId: departmentId,
    MessageSeq: PENDING_SEQ_BASE + pendingCounter,
    SenderParticipantType: 0,
    SenderUserId: senderUserId,
    SenderUnitId: null,
    SenderDisplayName: senderDisplayName,
    Body: body,
    MessageType: messageType,
    Priority: priority,
    ThreadRootMessageId: threadRootMessageId,
    ThreadReplyCount: 0,
    LastThreadReplyOn: null,
    AlsoSendToChannel: false,
    MetadataJson: metadataJson,
    ClientMessageId: clientMessageId,
    SentOn: new Date().toISOString(),
    EditedOn: null,
    DeletedOn: null,
    DeletedByUserId: null,
    PinnedOn: null,
    PinnedByUserId: null,
    Reactions: [],
    Attachments: [],
  };
}

export function isPendingMessage(message: ChatMessageDto): boolean {
  return message.MessageSeq >= PENDING_SEQ_BASE;
}
