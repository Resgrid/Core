// High-level message actions shared by the conversation views. Each keeps the store optimistic and
// reconciles with the API response; the hub echo dedupes by ClientMessageId / ChatMessageId.
import {
  addReaction,
  deleteMessage,
  editMessage,
  getMessages,
  markRead,
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
  prependChannelMessages,
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

  try {
    const created = await sendMessage(channel.ChatChannelId, {
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
        const attachmentId = await uploadAttachment(channel.ChatChannelId, created.ChatMessageId, payload.file);
        if (attachmentId) {
          upsertMessages(channel.ChatChannelId, [
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
  } catch (error) {
    console.error('Failed to send chat message.', error);
  }
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

export function markConversationRead(channel: ChatChannelDto, seq: number): void {
  if (seq <= channel.MyLastReadSeq) {
    return;
  }
  markChannelRead(channel.ChatChannelId, seq);
  chatHub.markRead(channel.ChatChannelId, seq);
  markRead(channel.ChatChannelId, seq).catch(() => undefined);
}
