// Singleton SignalR client for the chat hub. Owns connection lifecycle, channel membership,
// reconnect delta-sync and heartbeat, and fans hub events into the chat store.
import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
  type IRetryPolicy,
  type RetryContext,
} from '@microsoft/signalr';
import { getEventingToken } from '../../runtime/eventingToken';
import { getBrowserConfig } from '../../runtime/browserConfig';
import { getMessagesAfter } from './chatApi';
import {
  addPendingAck,
  applyHubDelete,
  applyHubEdit,
  applyHubMessage,
  applyHubReaction,
  applyHubReceipt,
  applyHubThreadUpdated,
  isPendingMessage,
  removeChannel,
  setBotTyping,
  setConnectionStatus,
  setNotice,
  setPresence,
  setTyping,
  upsertMessages,
  chatStore,
} from './chatStore';
import {
  CHAT_HUB_EVENTS,
  CHAT_HUB_METHODS,
  getCurrentUserId,
  type HubAccessRevokedPayload,
  type HubAckRequiredPayload,
  type HubChatbotTypingPayload,
  type HubDeletedPayload,
  type HubMessagePayload,
  type HubReactionPayload,
  type HubReceiptPayload,
  type HubThreadUpdatedPayload,
} from './types';

const HEARTBEAT_INTERVAL_MS = 45000;
const DELTA_SYNC_PAGE_CAP = 200;
const DELTA_SYNC_MAX_PAGES = 5;

// Retry forever: quick attempts first, then a capped 30s cadence.
class CappedRetryPolicy implements IRetryPolicy {
  private static readonly delays = [0, 2000, 5000, 10000, 30000];

  public nextRetryDelayInMilliseconds(retryContext: RetryContext): number {
    const index = Math.min(retryContext.previousRetryCount, CappedRetryPolicy.delays.length - 1);
    return CappedRetryPolicy.delays[index];
  }
}

function parsePayload<T>(arg: unknown): T | null {
  try {
    if (typeof arg === 'string') {
      return JSON.parse(arg) as T;
    }
    return arg as T;
  } catch {
    return null;
  }
}

// Tolerant field accessor for the object-style events (camelCase or PascalCase).
function pick<T>(source: Record<string, unknown>, camel: string, pascal: string): T | undefined {
  const value = source[camel] ?? source[pascal];
  return value as T | undefined;
}

class ChatHub {
  private connection: HubConnection | null = null;
  private startPromise: Promise<void> | null = null;
  private joinedChannels = new Map<string, number | undefined>();
  private heartbeatTimer: ReturnType<typeof setInterval> | null = null;
  private channelsRefreshHandlers = new Set<() => void>();
  private authorizationRefreshPromise: Promise<void> | null = null;

  public subscribeChannelsRefresh(handler: () => void): () => void {
    this.channelsRefreshHandlers.add(handler);
    return () => {
      this.channelsRefreshHandlers.delete(handler);
    };
  }

  private notifyChannelsRefresh(): void {
    for (const handler of this.channelsRefreshHandlers) {
      try {
        handler();
      } catch (error) {
        console.error('Chat channels refresh handler failed.', error);
      }
    }
  }

  // Idempotent: safe to call from every chat surface; only the first call starts the socket.
  public ensureConnected(): Promise<void> {
    return this.ensureStarted();
  }

  private async ensureStarted(): Promise<void> {
    if (this.connection && this.connection.state === HubConnectionState.Connected) {
      return;
    }
    if (this.startPromise) {
      return this.startPromise;
    }

    const { channelUrl } = getBrowserConfig();
    const connection = new HubConnectionBuilder()
	  .withUrl(`${channelUrl}/chatHub`, { accessTokenFactory: getEventingToken })
      .withAutomaticReconnect(new CappedRetryPolicy())
      .configureLogging(LogLevel.Warning)
      .build();

    this.registerHandlers(connection);
    connection.onreconnecting(() => {
      setConnectionStatus('reconnecting');
    });
    connection.onreconnected(() => {
      setConnectionStatus('connected');
      void this.onReconnected();
    });
    connection.onclose(() => {
      setConnectionStatus('offline');
    });

    this.connection = connection;

    this.startPromise = (async () => {
      await connection.start();
      setConnectionStatus('connected');
      await connection.invoke(CHAT_HUB_METHODS.Connect);
      for (const [channelId, asUnitId] of this.joinedChannels.entries()) {
        await this.invokeJoin(channelId, asUnitId);
      }
      this.reportActiveChannel();
      this.startHeartbeat();
    })();

    try {
      await this.startPromise;
    } catch (error) {
      setConnectionStatus('offline');
      throw error;
    } finally {
      this.startPromise = null;
    }
  }

  private registerHandlers(connection: HubConnection): void {
    connection.on(CHAT_HUB_EVENTS.MessageReceived, (arg: unknown) => {
      const payload = parsePayload<HubMessagePayload>(arg);
      if (!payload) {
        return;
      }
      // A bot (or other non-user) reply satisfies any outstanding "assistant is typing" row.
      if (!payload.SenderUserId) {
        setBotTyping(payload.ChatChannelId, false);
      }
      applyHubMessage(payload);
    });
    connection.on(CHAT_HUB_EVENTS.MessageEdited, (arg: unknown) => {
      const payload = parsePayload<HubMessagePayload>(arg);
      if (payload) {
        applyHubEdit(payload);
      }
    });
    connection.on(CHAT_HUB_EVENTS.MessageDeleted, (arg: unknown) => {
      const payload = parsePayload<HubDeletedPayload>(arg);
      if (payload) {
        applyHubDelete(payload);
      }
    });
    connection.on(CHAT_HUB_EVENTS.ReactionUpdated, (arg: unknown) => {
      const payload = parsePayload<HubReactionPayload>(arg);
      if (payload) {
        applyHubReaction(payload);
      }
    });
    connection.on(CHAT_HUB_EVENTS.ReceiptUpdated, (arg: unknown) => {
      const payload = parsePayload<HubReceiptPayload>(arg);
      if (payload) {
        applyHubReceipt(payload);
      }
    });
    connection.on(CHAT_HUB_EVENTS.ThreadUpdated, (arg: unknown) => {
      const payload = parsePayload<HubThreadUpdatedPayload>(arg);
      if (payload) {
        applyHubThreadUpdated(payload);
      }
    });
    connection.on(CHAT_HUB_EVENTS.AckRequired, (arg: unknown) => {
      const payload = parsePayload<HubAckRequiredPayload>(arg);
      // The sender never has to acknowledge their own urgent message.
      if (payload && payload.SenderUserId !== getCurrentUserId()) {
        addPendingAck(payload.ChatMessageId);
      }
    });
    connection.on(CHAT_HUB_EVENTS.ChannelUpdated, (arg: unknown) => {
      this.refreshChannelAuthorizationFromPayload(arg);
    });
    connection.on(CHAT_HUB_EVENTS.ChannelProvisioned, (arg: unknown) => {
      this.refreshChannelAuthorizationFromPayload(arg);
    });
    connection.on(CHAT_HUB_EVENTS.ModerationApplied, (arg: unknown) => {
      this.refreshChannelAuthorizationFromPayload(arg);
    });
    connection.on(CHAT_HUB_EVENTS.AccessRevoked, (arg: unknown) => {
      const payload = parsePayload<HubAccessRevokedPayload>(arg);
      if (!payload || !payload.ChannelId) {
        return;
      }
      if (payload.UserId === getCurrentUserId()) {
        this.joinedChannels.delete(payload.ChannelId);
        removeChannel(payload.ChannelId);
        setNotice('You were removed from a chat channel.');
      }
    });
    connection.on(CHAT_HUB_EVENTS.ChatbotTyping, (arg: unknown) => {
      const payload = parsePayload<HubChatbotTypingPayload>(arg);
      if (payload) {
        setBotTyping(payload.ChatChannelId, payload.IsTyping);
      }
    });
    connection.on(CHAT_HUB_EVENTS.Typing, (arg: unknown) => {
      const source = parsePayload<Record<string, unknown>>(arg);
      if (!source) {
        return;
      }
      const channelId = pick<string>(source, 'channelId', 'ChannelId');
      const userId = pick<string>(source, 'userId', 'UserId');
      const displayName = pick<string>(source, 'displayName', 'DisplayName') ?? 'Someone';
      const isTyping = pick<boolean>(source, 'isTyping', 'IsTyping') ?? false;
      if (!channelId || !userId || userId === getCurrentUserId()) {
        return;
      }
      setTyping(channelId, userId, displayName, isTyping);
    });
    connection.on(CHAT_HUB_EVENTS.PresenceChanged, (first: unknown, second: unknown) => {
      if (first && typeof first === 'object') {
        const source = first as Record<string, unknown>;
        const userId = pick<string>(source, 'userId', 'UserId');
        const isOnline = pick<boolean>(source, 'isOnline', 'IsOnline') ?? false;
        if (userId) {
          setPresence(userId, isOnline);
        }
        return;
      }
      if (typeof first === 'string') {
        setPresence(first, Boolean(second));
      }
    });
    connection.on(CHAT_HUB_EVENTS.Connected, () => {
      // Connection acknowledged by the hub.
    });
  }

  private async onReconnected(): Promise<void> {
    if (!this.connection) {
      return;
    }
    try {
      await this.connection.invoke(CHAT_HUB_METHODS.Connect);
      for (const [channelId, asUnitId] of this.joinedChannels.entries()) {
        await this.invokeJoin(channelId, asUnitId);
        await this.deltaSync(channelId);
      }
      this.reportActiveChannel();
      this.notifyChannelsRefresh();
    } catch (error) {
      console.error('Chat hub reconnect sync failed.', error);
    }
  }

  private refreshChannelAuthorizationFromPayload(arg: unknown): void {
    const source = parsePayload<Record<string, unknown>>(arg);
    const channelId = source ? pick<string>(source, 'chatChannelId', 'ChatChannelId')?.trim() : undefined;

    if (channelId) {
      void this.refreshChannelAuthorization(channelId);
      return;
    }

    void this.refreshChannelAuthorizations();
  }

  private async refreshChannelAuthorization(channelId: string): Promise<void> {
    if (this.joinedChannels.has(channelId)) {
      await this.joinChannel(channelId, this.joinedChannels.get(channelId));
    }
    this.notifyChannelsRefresh();
  }

  // Channel SignalR groups are authorization-epoch scoped. Membership/rule/moderation and
  // incident-board changes rotate the epoch server-side before the refresh hint is broadcast;
  // rejoining here performs a fresh server authorization check and moves eligible connections
  // into the new group. A forged/stale client that ignores the hint stays in an obsolete group.
  private async refreshChannelAuthorizations(): Promise<void> {
    if (this.authorizationRefreshPromise) {
      return this.authorizationRefreshPromise;
    }

    this.authorizationRefreshPromise = (async () => {
      if (this.connection && this.connection.state === HubConnectionState.Connected) {
        for (const [channelId, asUnitId] of this.joinedChannels.entries()) {
          try {
            await this.invokeJoin(channelId, asUnitId, true);
          } catch (err) {
            console.error('invokeJoin failed during authorization refresh', {
              op: 'refreshChannelAuthorizations',
              channelId,
              asUnitId,
              err,
            });
            throw err;
          }
        }
      }
      this.notifyChannelsRefresh();
    })();

    try {
      await this.authorizationRefreshPromise;
    } finally {
      this.authorizationRefreshPromise = null;
    }
  }

  // Page through missed messages (cap 200/page, max 5 pages) so long outages recover fully.
  private async deltaSync(channelId: string): Promise<void> {
    const lastRealSeq = () =>
      (chatStore.getState().messagesByChannel[channelId] ?? [])
        .filter((message) => !isPendingMessage(message))
        .reduce((max, message) => Math.max(max, message.MessageSeq), 0);

    let afterSeq = lastRealSeq();
    if (afterSeq <= 0) {
      return;
    }
    try {
      for (let page = 0; page < DELTA_SYNC_MAX_PAGES; page += 1) {
        const batch = await getMessagesAfter(channelId, afterSeq);
        if (batch.length === 0) {
          break;
        }
        upsertMessages(channelId, batch);
        afterSeq = batch.reduce((max, message) => Math.max(max, message.MessageSeq), afterSeq);
        if (batch.length < DELTA_SYNC_PAGE_CAP) {
          break;
        }
      }
    } catch (error) {
      console.error('Chat delta sync failed.', error);
    }
  }

  private startHeartbeat(): void {
    if (this.heartbeatTimer) {
      return;
    }
    this.heartbeatTimer = setInterval(() => {
      if (this.connection && this.connection.state === HubConnectionState.Connected) {
        this.connection.invoke(CHAT_HUB_METHODS.Heartbeat).catch(() => undefined);
      }
    }, HEARTBEAT_INTERVAL_MS);
  }

  private async invokeJoin(
    channelId: string,
    asUnitId: number | undefined,
    throwOnError = false,
  ): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) {
      return;
    }
    try {
      await this.connection.invoke(CHAT_HUB_METHODS.JoinChannel, channelId, asUnitId ?? null);
    } catch (error) {
      console.error('Chat join channel failed.', error);
      if (throwOnError) {
        throw error;
      }
    }
  }

  public async joinChannel(channelId: string, asUnitId?: number): Promise<void> {
    this.joinedChannels.set(channelId, asUnitId);
    try {
      await this.ensureStarted();
    } catch {
      // Start failed (offline); the join is retried by the reconnect flow.
      return;
    }
    await this.invokeJoin(channelId, asUnitId);
  }

  public async leaveChannel(channelId: string): Promise<void> {
    this.joinedChannels.delete(channelId);
    if (this.connection && this.connection.state === HubConnectionState.Connected) {
      try {
        await this.connection.invoke(CHAT_HUB_METHODS.LeaveChannel, channelId);
      } catch (error) {
        console.error('Chat leave channel failed.', error);
      }
    }
  }

  public typing(channelId: string, isTyping: boolean, displayName?: string, asUnitId?: number): void {
    if (this.connection && this.connection.state === HubConnectionState.Connected) {
      // Hub signature: Typing(channelId, displayName, isTyping, asUnitId).
      this.connection
        .invoke(CHAT_HUB_METHODS.Typing, channelId, displayName ?? null, isTyping, asUnitId ?? null)
        .catch(() => undefined);
    }
  }

  public markRead(channelId: string, seq: number, asUnitId?: number): void {
    if (this.connection && this.connection.state === HubConnectionState.Connected) {
      this.connection.invoke(CHAT_HUB_METHODS.MarkRead, channelId, seq, asUnitId ?? null).catch(() => undefined);
    }
  }

  // Tells the server which conversation is on screen so pushes for it are suppressed while
  // everything else still alerts. Remembered locally (with the acting unit, so unit push
  // suppression tracks the viewer too) and re-reported after reconnects.
  private activeChannelReported: string | null = null;
  private activeChannelUnitId: number | null = null;

  public setActiveChannel(channelId: string | null, asUnitId?: number): void {
    this.activeChannelReported = channelId;
    this.activeChannelUnitId = channelId ? asUnitId ?? null : null;
    this.reportActiveChannel();
  }

  private reportActiveChannel(): void {
    if (this.connection && this.connection.state === HubConnectionState.Connected) {
      this.connection
        .invoke(CHAT_HUB_METHODS.SetActiveChannel, this.activeChannelReported, this.activeChannelUnitId)
        .catch(() => undefined);
    }
  }
}

export const chatHub = new ChatHub();
