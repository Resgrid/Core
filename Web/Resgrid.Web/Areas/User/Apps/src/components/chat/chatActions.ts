// High-level message actions shared by the conversation views. Each keeps the store optimistic and
// reconciles with the API response; the hub echo dedupes by ClientMessageId / ChatMessageId.
import {
  addReaction,
  deleteMessage,
  editMessage,
  getMessages,
  getPresence,
  pinMessage,
  removeReaction,
  sendMessage,
  unpinMessage,
  uploadAttachment,
  ackMessage,
  flagMessage,
} from './chatApi';
import { chatHub } from './chatHub';
import {
  clearPendingAck,
  createOptimisticMessage,
  markChannelRead,
  markMessageFailed,
  markMessagePending,
  prependChannelMessages,
  removeMessageLocally,
  seedPresence,
  setChannelMessages,
  upsertMessage,
  upsertMessages,
  chatStore,
} from './chatStore';
import { newClientMessageId } from './chatFormat';
import type { ComposerSendPayload } from './atoms/Composer';
import { ChatMessageType, type ChatChannelDto, type ChatMessageDto } from './types';

export async function loadInitialMessages(channelId: string): Promise<void> {
  const page = await getMessages(channelId);
  setChannelMessages(channelId, page.messages, page.hasMore);
}

export async function loadOlderMessages(channelId: string): Promise<void> {
  const existing = chatStore.getState().messagesByChannel[channelId] ?? [];
  const oldest = existing.reduce<number | undefined>((min, message) => {
    if (message.MessageSeq >= Number.MAX_SAFE_INTEGER - 100000) {
      return min;
    }
    return min === undefined ? message.MessageSeq : Math.min(min, message.MessageSeq);
  }, undefined);
  if (oldest === undefined) {
    return;
  }
  const page = await getMessages(channelId, oldest);
  prependChannelMessages(channelId, page.messages, page.hasMore);
}

// Files of failed image sends are kept in-memory so Retry can re-upload without re-picking.
const pendingFiles = new Map<string, File>();

async function deliverMessage(
  channelId: string,
  clientMessageId: string,
  payload: ComposerSendPayload,
  threadRootMessageId: string | null,
): Promise<void> {
  const created = await sendMessage(channelId, {
    Body: payload.body,
    MessageType: payload.messageType,
    Priority: payload.priority,
    MetadataJson: payload.metadataJson,
    ClientMessageId: clientMessageId,
    ThreadRootMessageId: threadRootMessageId,
  });

  if (created) {
    upsertMessage(created);

    if (payload.messageType === ChatMessageType.Image && payload.file) {
      const attachmentId = await uploadAttachment(channelId, created.ChatMessageId, payload.file);
      if (attachmentId) {
        upsertMessages(channelId, [
          {
            ...created,
            Attachments: [
              {
                ChatAttachmentId: attachmentId,
                FileName: payload.file.name,
                ContentType: payload.file.type,
                Size: payload.file.size,
              },
            ],
          },
        ]);
      }
    }
  }
  pendingFiles.delete(clientMessageId);
}

export async function sendComposerMessage(
  channel: ChatChannelDto,
  currentUserId: string,
  payload: ComposerSendPayload,
  threadRootMessageId: string | null = null,
): Promise<void> {
  const clientMessageId = newClientMessageId();
  const optimistic = createOptimisticMessage(
    channel.ChatChannelId,
    0,
    currentUserId,
    'You',
    payload.body,
    clientMessageId,
    payload.messageType,
    payload.priority,
    threadRootMessageId,
    payload.metadataJson,
  );
  upsertMessage(optimistic);
  if (payload.file) {
    pendingFiles.set(clientMessageId, payload.file);
  }

  try {
    await deliverMessage(channel.ChatChannelId, clientMessageId, payload, threadRootMessageId);
  } catch (error) {
    console.error('Failed to send chat message.', error);
    markMessageFailed(channel.ChatChannelId, clientMessageId);
  }
}

// Re-send a failed optimistic message, reusing its ClientMessageId so the echo dedupes.
export async function retryFailedMessage(channel: ChatChannelDto, message: ChatMessageDto): Promise<void> {
  if (!message.ClientMessageId) {
    return;
  }
  const clientMessageId = message.ClientMessageId;
  markMessagePending(channel.ChatChannelId, clientMessageId);
  try {
    await deliverMessage(
      channel.ChatChannelId,
      clientMessageId,
      {
        body: message.Body ?? '',
        messageType: message.MessageType,
        priority: message.Priority,
        metadataJson: message.MetadataJson,
        file: pendingFiles.get(clientMessageId),
      },
      message.ThreadRootMessageId,
    );
  } catch (error) {
    console.error('Failed to resend chat message.', error);
    markMessageFailed(channel.ChatChannelId, clientMessageId);
  }
}

export function discardFailedMessage(message: ChatMessageDto): void {
  if (message.ClientMessageId) {
    pendingFiles.delete(message.ClientMessageId);
  }
  removeMessageLocally(message.ChatChannelId, message.ChatMessageId);
}

export async function toggleReaction(message: ChatMessageDto, emoji: string, mine: boolean): Promise<void> {
  try {
    if (mine) {
      await removeReaction(message.ChatMessageId, emoji);
    } else {
      await addReaction(message.ChatMessageId, emoji);
    }
  } catch (error) {
    console.error('Failed to toggle reaction.', error);
  }
}

export async function saveMessageEdit(message: ChatMessageDto, body: string): Promise<void> {
  try {
    const updated = await editMessage(message.ChatMessageId, body);
    if (updated) {
      upsertMessage(updated);
    }
  } catch (error) {
    console.error('Failed to edit message.', error);
  }
}

export async function removeMessage(message: ChatMessageDto): Promise<void> {
  try {
    await deleteMessage(message.ChatMessageId);
    upsertMessages(message.ChatChannelId, [{ ...message, DeletedOn: new Date().toISOString(), Body: null }]);
  } catch (error) {
    console.error('Failed to delete message.', error);
  }
}

export async function setPinned(message: ChatMessageDto, pinned: boolean): Promise<void> {
  try {
    if (pinned) {
      await pinMessage(message.ChatMessageId);
    } else {
      await unpinMessage(message.ChatMessageId);
    }
    upsertMessages(message.ChatChannelId, [
      { ...message, PinnedOn: pinned ? new Date().toISOString() : null },
    ]);
  } catch (error) {
    console.error('Failed to pin message.', error);
  }
}

export async function flagChatMessage(message: ChatMessageDto, reason: number, note: string): Promise<void> {
  try {
    await flagMessage(message.ChatMessageId, reason, note);
  } catch (error) {
    console.error('Failed to flag message.', error);
  }
}

export async function acknowledgeMessage(message: ChatMessageDto): Promise<void> {
  try {
    await ackMessage(message.ChatMessageId);
    clearPendingAck(message.ChatMessageId);
  } catch (error) {
    console.error('Failed to acknowledge message.', error);
  }
}

// ---- Read pointer (hub invoke only, throttled to 1/sec/channel with a trailing call) ----

const MARK_READ_THROTTLE_MS = 1000;
const lastMarkReadAt = new Map<string, number>();
const markReadTimers = new Map<string, ReturnType<typeof setTimeout>>();

export function markConversationRead(channel: ChatChannelDto, seq: number): void {
  if (seq <= channel.MyLastReadSeq) {
    return;
  }
  markChannelRead(channel.ChatChannelId, seq);

  const now = Date.now();
  const last = lastMarkReadAt.get(channel.ChatChannelId) ?? 0;
  if (now - last >= MARK_READ_THROTTLE_MS) {
    lastMarkReadAt.set(channel.ChatChannelId, now);
    chatHub.markRead(channel.ChatChannelId, seq);
    return;
  }
  const pending = markReadTimers.get(channel.ChatChannelId);
  if (pending) {
    clearTimeout(pending);
  }
  markReadTimers.set(
    channel.ChatChannelId,
    setTimeout(() => {
      markReadTimers.delete(channel.ChatChannelId);
      lastMarkReadAt.set(channel.ChatChannelId, Date.now());
      chatHub.markRead(channel.ChatChannelId, seq);
    }, MARK_READ_THROTTLE_MS - (now - last)),
  );
}

// ---- Presence seeding ----

const PRESENCE_SEED_CAP = 200;

export async function seedPresenceFor(userIds: (string | null | undefined)[]): Promise<void> {
  const ids = Array.from(new Set(userIds.filter((id): id is string => !!id))).slice(0, PRESENCE_SEED_CAP);
  if (ids.length === 0) {
    return;
  }
  try {
    const online = await getPresence(ids);
    seedPresence(online);
  } catch {
    // Presence is best-effort.
  }
}
