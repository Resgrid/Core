// Singleton SignalR client for the chat hub. Owns connection lifecycle, channel membership,
// reconnect delta-sync and heartbeat, and fans hub events into the chat store.
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import { getAccessToken } from '../../runtime/auth';
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
  setBotTyping,
  setPresence,
  setTyping,
  upsertMessages,
  chatStore,
} from './chatStore';
import {
  getCurrentUserId,
  type HubAckRequiredPayload,
  type HubChatbotTypingPayload,
  type HubDeletedPayload,
  type HubMessagePayload,
  type HubReactionPayload,
  type HubReceiptPayload,
  type HubThreadUpdatedPayload,
} from './types';

const HEARTBEAT_INTERVAL_MS = 45000;

function parsePayload<T>(arg: unknown): T {
  if (typeof arg === 'string') {
    return JSON.parse(arg) as T;
  }
  return arg as T;
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
  private channelsRefreshHandler: (() => void) | null = null;
  private refCount = 0;

  public setChannelsRefreshHandler(handler: (() => void) | null): void {
    this.channelsRefreshHandler = handler;
  }

  public async acquire(): Promise<void> {
    this.refCount += 1;
    await this.ensureStarted();
  }

  public release(): void {
    this.refCount = Math.max(0, this.refCount - 1);
    if (this.refCount === 0) {
      void this.stop();
    }
  }

  private async ensureStarted(): Promise<void> {
    if (this.connection && this.connection.state === HubConnectionState.Connected) {
      return;
    }
    if (this.startPromise) {
      return this.startPromise;
    }

    const token = getAccessToken();
    if (token.length === 0) {
      return;
    }

    const { channelUrl } = getBrowserConfig();
    const connection = new HubConnectionBuilder()
      .withUrl(`${channelUrl}/chatHub?access_token=${encodeURIComponent(token)}`)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.registerHandlers(connection);
    connection.onreconnected(() => {
      void this.onReconnected();
    });

    this.connection = connection;

    this.startPromise = (async () => {
      await connection.start();
      await connection.invoke('Connect');
      for (const [channelId, asUnitId] of this.joinedChannels.entries()) {
        await this.invokeJoin(channelId, asUnitId);
      }
      this.startHeartbeat();
    })();

    try {
      await this.startPromise;
    } finally {
      this.startPromise = null;
    }
  }

  private registerHandlers(connection: HubConnection): void {
    connection.on('chatMessageReceived', (arg: unknown) => {
      applyHubMessage(parsePayload<HubMessagePayload>(arg));
    });
    connection.on('chatbotMessageReceived', (arg: unknown) => {
      const payload = parsePayload<HubMessagePayload>(arg);
      setBotTyping(payload.ChatChannelId, false);
      applyHubMessage(payload);
    });
    connection.on('chatMessageEdited', (arg: unknown) => {
      applyHubEdit(parsePayload<HubMessagePayload>(arg));
    });
    connection.on('chatMessageDeleted', (arg: unknown) => {
      applyHubDelete(parsePayload<HubDeletedPayload>(arg));
    });
    connection.on('chatReactionUpdated', (arg: unknown) => {
      applyHubReaction(parsePayload<HubReactionPayload>(arg));
    });
    connection.on('chatReceiptUpdated', (arg: unknown) => {
      applyHubReceipt(parsePayload<HubReceiptPayload>(arg));
    });
    connection.on('chatThreadUpdated', (arg: unknown) => {
      applyHubThreadUpdated(parsePayload<HubThreadUpdatedPayload>(arg));
    });
    connection.on('chatMessageAckRequired', (arg: unknown) => {
      const payload = parsePayload<HubAckRequiredPayload>(arg);
      addPendingAck(payload.ChatMessageId);
    });
    connection.on('chatChannelUpdated', () => {
      this.channelsRefreshHandler?.();
    });
    connection.on('chatChannelProvisioned', () => {
      this.channelsRefreshHandler?.();
    });
    connection.on('chatModerationApplied', () => {
      this.channelsRefreshHandler?.();
    });
    connection.on('chatbotTyping', (arg: unknown) => {
      const payload = parsePayload<HubChatbotTypingPayload>(arg);
      setBotTyping(payload.ChatChannelId, payload.IsTyping);
    });
    connection.on('chatTyping', (arg: unknown) => {
      const source = (typeof arg === 'string' ? JSON.parse(arg) : arg) as Record<string, unknown>;
      const channelId = pick<string>(source, 'channelId', 'ChannelId');
      const userId = pick<string>(source, 'userId', 'UserId');
      const displayName = pick<string>(source, 'displayName', 'DisplayName') ?? 'Someone';
      const isTyping = pick<boolean>(source, 'isTyping', 'IsTyping') ?? false;
      if (!channelId || !userId || userId === getCurrentUserId()) {
        return;
      }
      setTyping(channelId, userId, displayName, isTyping);
    });
    connection.on('chatPresenceChanged', (first: unknown, second: unknown) => {
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
    connection.on('onChatConnected', () => {
      // Connection acknowledged by the hub.
    });
  }

  private async onReconnected(): Promise<void> {
    if (!this.connection) {
      return;
    }
    try {
      await this.connection.invoke('Connect');
      for (const [channelId, asUnitId] of this.joinedChannels.entries()) {
        await this.invokeJoin(channelId, asUnitId);
        await this.deltaSync(channelId);
      }
      this.channelsRefreshHandler?.();
    } catch (error) {
      console.error('Chat hub reconnect sync failed.', error);
    }
  }

  private async deltaSync(channelId: string): Promise<void> {
    const messages = chatStore.getState().messagesByChannel[channelId] ?? [];
    const lastSeq = messages
      .filter((message) => !isPendingMessage(message))
      .reduce((max, message) => Math.max(max, message.MessageSeq), 0);
    if (lastSeq <= 0) {
      return;
    }
    try {
      const after = await getMessagesAfter(channelId, lastSeq);
      if (after.length > 0) {
        upsertMessages(channelId, after);
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
        this.connection.invoke('Heartbeat').catch(() => undefined);
      }
    }, HEARTBEAT_INTERVAL_MS);
  }

  private async invokeJoin(channelId: string, asUnitId: number | undefined): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) {
      return;
    }
    try {
      await this.connection.invoke('JoinChannel', channelId, asUnitId ?? null);
    } catch (error) {
      console.error('Chat join channel failed.', error);
    }
  }

  public async joinChannel(channelId: string, asUnitId?: number): Promise<void> {
    this.joinedChannels.set(channelId, asUnitId);
    await this.ensureStarted();
    await this.invokeJoin(channelId, asUnitId);
  }

  public async leaveChannel(channelId: string): Promise<void> {
    this.joinedChannels.delete(channelId);
    if (this.connection && this.connection.state === HubConnectionState.Connected) {
      try {
        await this.connection.invoke('LeaveChannel', channelId);
      } catch (error) {
        console.error('Chat leave channel failed.', error);
      }
    }
  }

  public typing(channelId: string, isTyping: boolean, displayName?: string, asUnitId?: number): void {
    if (this.connection && this.connection.state === HubConnectionState.Connected) {
      this.connection.invoke('Typing', channelId, isTyping, asUnitId ?? null, displayName ?? null).catch(() => undefined);
    }
  }

  public markRead(channelId: string, seq: number, asUnitId?: number): void {
    if (this.connection && this.connection.state === HubConnectionState.Connected) {
      this.connection.invoke('MarkRead', channelId, seq, asUnitId ?? null).catch(() => undefined);
    }
  }

  private async stop(): Promise<void> {
    if (this.heartbeatTimer) {
      clearInterval(this.heartbeatTimer);
      this.heartbeatTimer = null;
    }
    this.joinedChannels.clear();
    const connection = this.connection;
    this.connection = null;
    if (connection) {
      try {
        await connection.stop();
      } catch {
        // ignore stop failures
      }
    }
  }
}

export const chatHub = new ChatHub();
