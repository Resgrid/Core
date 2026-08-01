// Framework-free external store for chat state, consumed via useChatStore (useSyncExternalStore).
import {
  ChatMessageType,
  toMessageDto,
  type ChatChannelDto,
  type ChatMemberDto,
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

export type ChatConnectionStatus = 'connected' | 'reconnecting' | 'offline';

export interface ChatState {
  chatAvailable: boolean;
  channelsLoaded: boolean;
  channels: ChatChannelDto[];
  messagesByChannel: Record<string, ChatMessageDto[]>;
  threadMessagesByRoot: Record<string, ChatMessageDto[]>;
  hasMoreByChannel: Record<string, boolean>;
  membersByChannel: Record<string, ChatMemberDto[]>;
  typingByChannel: Record<string, TypingEntry[]>;
  botTypingByChannel: Record<string, boolean>;
  onlineUserIds: string[];
  pendingAckMessageIds: string[];
  activeChannelId: string | null;
  connectionStatus: ChatConnectionStatus;
  authError: boolean;
  notice: string | null;
  highlightMessageId: string | null;
}

type Listener = () => void;

const TYPING_TTL_MS = 5000;

let state: ChatState = {
  chatAvailable: true,
  channelsLoaded: false,
  channels: [],
  messagesByChannel: {},
  threadMessagesByRoot: {},
  hasMoreByChannel: {},
  membersByChannel: {},
  typingByChannel: {},
  botTypingByChannel: {},
  onlineUserIds: [],
  pendingAckMessageIds: [],
  activeChannelId: null,
  connectionStatus: 'offline',
  authError: false,
  notice: null,
  highlightMessageId: null,
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
      // Prefer the incoming row (fresher edit/delete flags) but never let a pending/failed
      // client status leak back onto a server-acknowledged message.
      const { ClientStatus: _drop, ...rest } = message;
      result[index] = { ...result[index], ...rest, ClientStatus: undefined };
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

export function removeChannel(channelId: string): void {
  const channels = state.channels.filter((candidate) => candidate.ChatChannelId !== channelId);
  const messagesByChannel = { ...state.messagesByChannel };
  delete messagesByChannel[channelId];
  setState({
    channels,
    messagesByChannel,
    activeChannelId: state.activeChannelId === channelId ? null : state.activeChannelId,
  });
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

// ---- Connection / auth / notices ----

export function setConnectionStatus(status: ChatConnectionStatus): void {
  if (state.connectionStatus !== status) {
    setState({ connectionStatus: status });
  }
}

export function setAuthError(hasError: boolean): void {
  if (state.authError !== hasError) {
    setState({ authError: hasError });
  }
}

export function setNotice(notice: string | null): void {
  setState({ notice });
}

export function setHighlightMessage(messageId: string | null): void {
  setState({ highlightMessageId: messageId });
}

// ---- Messages ----

// Bootstrap page load: MERGE into any messages that arrived via the hub while the fetch
// was in flight instead of replacing the list and dropping them.
export function setChannelMessages(channelId: string, messages: ChatMessageDto[], hasMore: boolean): void {
  const existing = state.messagesByChannel[channelId] ?? [];
  setState({
    messagesByChannel: { ...state.messagesByChannel, [channelId]: mergeMessages(existing, messages) },
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

export function removeMessageLocally(channelId: string, messageId: string): void {
  const existing = state.messagesByChannel[channelId];
  if (!existing) {
    return;
  }
  setState({
    messagesByChannel: {
      ...state.messagesByChannel,
      [channelId]: existing.filter((message) => message.ChatMessageId !== messageId),
    },
  });
}

export function markMessageFailed(channelId: string, clientMessageId: string): void {
  const existing = state.messagesByChannel[channelId] ?? [];
  const target = existing.find((message) => message.ClientMessageId === clientMessageId);
  if (!target) {
    return;
  }
  setState({
    messagesByChannel: {
      ...state.messagesByChannel,
      [channelId]: existing.map((message) =>
        message.ClientMessageId === clientMessageId ? { ...message, ClientStatus: 'failed' as const } : message,
      ),
    },
  });
}

export function markMessagePending(channelId: string, clientMessageId: string): void {
  const existing = state.messagesByChannel[channelId] ?? [];
  const target = existing.find((message) => message.ClientMessageId === clientMessageId);
  if (!target) {
    return;
  }
  setState({
    messagesByChannel: {
      ...state.messagesByChannel,
      [channelId]: existing.map((message) =>
        message.ClientMessageId === clientMessageId ? { ...message, ClientStatus: 'pending' as const } : message,
      ),
    },
  });
}

// ---- Thread replies (live) ----

export function setThreadMessages(rootMessageId: string, messages: ChatMessageDto[]): void {
  const existing = state.threadMessagesByRoot[rootMessageId] ?? [];
  setState({
    threadMessagesByRoot: { ...state.threadMessagesByRoot, [rootMessageId]: mergeMessages(existing, messages) },
  });
}

export function upsertThreadMessage(rootMessageId: string, message: ChatMessageDto): void {
  const existing = state.threadMessagesByRoot[rootMessageId] ?? [];
  setState({
    threadMessagesByRoot: { ...state.threadMessagesByRoot, [rootMessageId]: mergeMessages(existing, [message]) },
  });
}

export function applyHubMessage(payload: HubMessagePayload): void {
  const message = toMessageDto(payload);

  // Thread-only replies are broadcast to the channel group but must not land in the main list.
  if (message.ThreadRootMessageId && !message.AlsoSendToChannel) {
    upsertThreadMessage(message.ThreadRootMessageId, message);
  } else {
    upsertMessage(message);
  }

  const channel = state.channels.find((candidate) => candidate.ChatChannelId === message.ChatChannelId);
  if (channel) {
    const isActive = state.activeChannelId === message.ChatChannelId;
    const patch: Partial<ChatChannelDto> = {
      LastMessageSeq: Math.max(channel.LastMessageSeq, message.MessageSeq),
      LastMessageOn: message.SentOn,
    };
    if (!isActive && !message.ThreadRootMessageId) {
      patch.UnreadCount = Math.max(0, message.MessageSeq - channel.MyLastReadSeq);
    }
    patchChannel(message.ChatChannelId, patch);
  }
}

export function applyHubEdit(payload: HubMessagePayload): void {
  const existing = (state.messagesByChannel[payload.ChatChannelId] ?? []).find(
    (candidate) => candidate.ChatMessageId === payload.ChatMessageId,
  );
  if (existing) {
    upsertMessages(payload.ChatChannelId, [{ ...existing, Body: payload.Body, EditedOn: payload.EditedOn }]);
    return;
  }
  if (payload.ThreadRootMessageId) {
    const thread = state.threadMessagesByRoot[payload.ThreadRootMessageId] ?? [];
    const reply = thread.find((candidate) => candidate.ChatMessageId === payload.ChatMessageId);
    if (reply) {
      upsertThreadMessage(payload.ThreadRootMessageId, { ...reply, Body: payload.Body, EditedOn: payload.EditedOn });
    }
  }
}

export function applyHubDelete(payload: HubDeletedPayload): void {
  const existing = (state.messagesByChannel[payload.ChatChannelId] ?? []).find(
    (candidate) => candidate.ChatMessageId === payload.ChatMessageId,
  );
  if (existing) {
    upsertMessages(payload.ChatChannelId, [
      { ...existing, DeletedOn: payload.DeletedOn, Body: null },
    ]);
    return;
  }
  for (const [rootId, thread] of Object.entries(state.threadMessagesByRoot)) {
    const reply = thread.find((candidate) => candidate.ChatMessageId === payload.ChatMessageId);
    if (reply) {
      upsertThreadMessage(rootId, { ...reply, DeletedOn: payload.DeletedOn, Body: null });
      return;
    }
  }
}

export function applyHubReaction(payload: HubReactionPayload): void {
  const applyTo = (existing: ChatMessageDto): ChatMessageDto => {
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
    return { ...existing, Reactions: reactions };
  };

  const messages = state.messagesByChannel[payload.ChatChannelId] ?? [];
  const existing = messages.find((candidate) => candidate.ChatMessageId === payload.ChatMessageId);
  if (existing) {
    upsertMessages(payload.ChatChannelId, [applyTo(existing)]);
    return;
  }
  for (const [rootId, thread] of Object.entries(state.threadMessagesByRoot)) {
    const reply = thread.find((candidate) => candidate.ChatMessageId === payload.ChatMessageId);
    if (reply) {
      upsertThreadMessage(rootId, applyTo(reply));
      return;
    }
  }
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

// ---- Members ----

export function setChannelMembers(channelId: string, members: ChatMemberDto[]): void {
  setState({ membersByChannel: { ...state.membersByChannel, [channelId]: members } });
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

export function seedPresence(userIds: string[]): void {
  setPresenceBulk(Array.from(new Set([...state.onlineUserIds, ...userIds])));
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
    ClientStatus: 'pending',
  };
}

export function isPendingMessage(message: ChatMessageDto): boolean {
  return message.MessageSeq >= PENDING_SEQ_BASE;
}
