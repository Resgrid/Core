// Typed fetch wrappers for the v4 Chat + Chatbot APIs. Bearer + base URL follow runtime/api.ts.
import { ApiError, apiAuthHeaders, apiFetchJson, buildApiUrl, type ApiQuery } from '../../runtime/api';
import { setAuthError } from './chatStore';
import type {
  ApiItemResult,
  ApiListResult,
  ChatChannelDto,
  ChatMemberDto,
  ChatMessageDto,
  ChatbotChannelInfo,
  GifDto,
  RecipientDto,
  SendMessageOptions,
} from './types';

export { ApiError as ChatApiError };

export function isApiStatus(error: unknown, status: number): boolean {
  return error instanceof ApiError && error.status === status;
}

// Shared wrapper: surfaces 401s to the store so chat surfaces can show a session notice.
export async function chatRequest<T>(path: string, init?: RequestInit, query?: ApiQuery): Promise<T> {
  try {
    return await apiFetchJson<T>(path, init, query);
  } catch (error) {
    if (isApiStatus(error, 401)) {
      setAuthError(true);
    }
    throw error;
  }
}

async function getJson<T>(path: string, query?: ApiQuery): Promise<T> {
  return chatRequest<T>(path, undefined, query);
}

async function sendJson<T>(method: string, path: string, body?: unknown, query?: ApiQuery): Promise<T> {
  const headers: Record<string, string> = {};
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }
  return chatRequest<T>(
    path,
    {
      method,
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    },
    query,
  );
}

// ---- Channels ----

export interface GetChannelsOutcome {
  available: boolean;
  channels: ChatChannelDto[];
}

export async function getChannels(activeUnitId?: number): Promise<GetChannelsOutcome> {
  try {
    const result = await getJson<ApiListResult<ChatChannelDto>>('api/v4/Chat/GetChannels', { activeUnitId });
    return { available: true, channels: result.Data ?? [] };
  } catch (error) {
    if (isApiStatus(error, 404)) {
      return { available: false, channels: [] };
    }
    throw error;
  }
}

export async function getChannel(channelId: string): Promise<ChatChannelDto | null> {
  const result = await getJson<ApiItemResult<ChatChannelDto>>('api/v4/Chat/GetChannel', { channelId });
  return result.Data ?? null;
}

export async function createDirectMessage(targetUserId?: string, targetUnitId?: number): Promise<ChatChannelDto | null> {
  const result = await sendJson<ApiItemResult<ChatChannelDto>>('POST', 'api/v4/Chat/CreateDirectMessage', {
    TargetUserId: targetUserId ?? null,
    TargetUnitId: targetUnitId ?? null,
  });
  return result.Data ?? null;
}

export async function createAdHocChannel(name: string, memberUserIds: string[]): Promise<ChatChannelDto | null> {
  const result = await sendJson<ApiItemResult<ChatChannelDto>>('POST', 'api/v4/Chat/CreateAdHocChannel', {
    Name: name,
    MemberUserIds: memberUserIds,
  });
  return result.Data ?? null;
}

export async function updateChannel(channelId: string, name: string, topic: string): Promise<ChatChannelDto | null> {
  const result = await sendJson<ApiItemResult<ChatChannelDto>>('PUT', 'api/v4/Chat/UpdateChannel', { Name: name, Topic: topic }, { channelId });
  return result.Data ?? null;
}

export async function setNotificationPreference(channelId: string, preference: number): Promise<void> {
  await sendJson<unknown>('PUT', 'api/v4/Chat/SetNotificationPreference', { Preference: preference }, { channelId });
}

// ---- Members ----

export async function getMembers(channelId: string): Promise<ChatMemberDto[]> {
  const result = await getJson<ApiListResult<ChatMemberDto>>('api/v4/Chat/GetMembers', { channelId });
  return result.Data ?? [];
}

export async function addMembers(channelId: string, userIds: string[]): Promise<ChatMemberDto[]> {
  const result = await sendJson<ApiListResult<ChatMemberDto>>('POST', 'api/v4/Chat/AddMembers', { UserIds: userIds }, { channelId });
  return result.Data ?? [];
}

export async function removeMember(channelId: string, userId: string): Promise<void> {
  await chatRequest<unknown>('api/v4/Chat/RemoveMember', { method: 'DELETE' }, { channelId, userId });
}

// ---- Messages ----

const PAGE_SIZE = 40;

export interface MessagePage {
  messages: ChatMessageDto[];
  hasMore: boolean;
}

export async function getMessages(channelId: string, beforeSeq?: number): Promise<MessagePage> {
  const result = await getJson<ApiListResult<ChatMessageDto>>('api/v4/Chat/GetMessages', {
    channelId,
    beforeSeq,
    limit: PAGE_SIZE,
  });
  const messages = result.Data ?? [];
  return { messages, hasMore: messages.length >= PAGE_SIZE };
}

export async function getMessagesAfter(channelId: string, afterSeq: number): Promise<ChatMessageDto[]> {
  const result = await getJson<ApiListResult<ChatMessageDto>>('api/v4/Chat/GetMessagesAfter', {
    channelId,
    afterSeq,
    limit: 200,
  });
  return result.Data ?? [];
}

export async function getThread(messageId: string, beforeSeq?: number): Promise<ChatMessageDto[]> {
  const result = await getJson<ApiListResult<ChatMessageDto>>('api/v4/Chat/GetThread', { messageId, beforeSeq, limit: PAGE_SIZE });
  return result.Data ?? [];
}

export async function sendMessage(channelId: string, options: SendMessageOptions): Promise<ChatMessageDto | null> {
  const result = await sendJson<ApiItemResult<ChatMessageDto>>('POST', 'api/v4/Chat/SendMessage', options, { channelId });
  return result.Data ?? null;
}

export async function editMessage(messageId: string, body: string): Promise<ChatMessageDto | null> {
  const result = await sendJson<ApiItemResult<ChatMessageDto>>('PUT', 'api/v4/Chat/EditMessage', { Body: body }, { messageId });
  return result.Data ?? null;
}

export async function deleteMessage(messageId: string): Promise<void> {
  await chatRequest<unknown>('api/v4/Chat/DeleteMessage', { method: 'DELETE' }, { messageId });
}

// ---- Reactions / acks / read / pins ----

export async function addReaction(messageId: string, emoji: string): Promise<void> {
  await sendJson<unknown>('POST', 'api/v4/Chat/AddReaction', { Emoji: emoji }, { messageId });
}

export async function removeReaction(messageId: string, emoji: string): Promise<void> {
  await chatRequest<unknown>('api/v4/Chat/RemoveReaction', { method: 'DELETE' }, { messageId, emoji });
}

export async function ackMessage(messageId: string): Promise<void> {
  await sendJson<unknown>('POST', 'api/v4/Chat/Ack', undefined, { messageId });
}

export interface PendingAck {
  ChatMessageAckId: string;
  ChatMessageId: string;
  ChatChannelId: string;
}

export async function getMyPendingAcks(): Promise<PendingAck[]> {
  try {
    const result = await getJson<ApiListResult<PendingAck>>('api/v4/Chat/GetMyPendingAcks');
    return result.Data ?? [];
  } catch (error) {
    if (isApiStatus(error, 404)) {
      return [];
    }
    throw error;
  }
}

export async function pinMessage(messageId: string): Promise<void> {
  await sendJson<unknown>('POST', 'api/v4/Chat/PinMessage', undefined, { messageId });
}

export async function unpinMessage(messageId: string): Promise<void> {
  await chatRequest<unknown>('api/v4/Chat/UnpinMessage', { method: 'DELETE' }, { messageId });
}

export async function getPins(channelId: string): Promise<ChatMessageDto[]> {
  try {
    const result = await getJson<ApiListResult<ChatMessageDto>>('api/v4/Chat/GetPins', { channelId });
    return result.Data ?? [];
  } catch (error) {
    if (isApiStatus(error, 404)) {
      return [];
    }
    throw error;
  }
}

// ---- Attachments ----

export async function uploadAttachment(channelId: string, messageId: string, file: File): Promise<string | null> {
  const form = new FormData();
  form.append('file', file, file.name);
  const result = await chatRequest<{ ChatAttachmentId?: string }>(
    'api/v4/Chat/UploadAttachment',
    { method: 'POST', body: form },
    { channelId, messageId },
  );
  return result.ChatAttachmentId ?? null;
}

// Attachments require a bearer header, so <img> cannot load them directly. Fetch a blob URL.
export async function fetchAttachmentObjectUrl(attachmentId: string, thumbnail = false): Promise<string> {
  const path = thumbnail ? 'api/v4/Chat/GetAttachmentThumbnail' : 'api/v4/Chat/GetAttachment';
  const response = await fetch(buildApiUrl(path, { attachmentId }), { headers: apiAuthHeaders() });
  if (!response.ok) {
    if (response.status === 401) {
      setAuthError(true);
    }
    throw new ApiError(response.status, `${response.status} ${response.statusText}`);
  }
  const blob = await response.blob();
  return URL.createObjectURL(blob);
}

// ---- Search / GIFs / presence / flags ----

export async function searchMessages(q: string, channelId?: string): Promise<ChatMessageDto[]> {
  const result = await getJson<ApiListResult<ChatMessageDto>>('api/v4/Chat/Search', { q, channelId });
  return result.Data ?? [];
}

export async function searchGifs(q: string): Promise<GifDto[]> {
  const result = await getJson<ApiListResult<GifDto>>('api/v4/Chat/SearchGifs', { q, limit: 24 });
  return result.Data ?? [];
}

export async function getPresence(userIds: string[]): Promise<string[]> {
  if (userIds.length === 0) {
    return [];
  }
  const result = await getJson<{ OnlineUserIds: string[] }>('api/v4/Chat/GetPresence', { userIds: userIds.join(',') });
  return result.OnlineUserIds ?? [];
}

export async function flagMessage(messageId: string, reason: number, note: string): Promise<void> {
  await sendJson<unknown>('POST', 'api/v4/Chat/FlagMessage', { Reason: reason, Note: note }, { messageId });
}

// ---- Chatbot ----

export async function getChatbotChannel(): Promise<ChatbotChannelInfo | null> {
  try {
    const raw = await getJson<Record<string, unknown>>('api/v4/Chatbot/GetChatChannel');
    const channelId = (raw.ChatChannelId ?? raw.chatChannelId) as string | undefined;
    if (!channelId) {
      return null;
    }
    return {
      ChatChannelId: channelId,
      Name: (raw.Name ?? raw.name ?? 'Assistant') as string,
      LastMessageSeq: Number(raw.LastMessageSeq ?? raw.lastMessageSeq ?? 0),
      LastMessageOn: (raw.LastMessageOn ?? raw.lastMessageOn ?? null) as string | null,
    };
  } catch (error) {
    if (isApiStatus(error, 404)) {
      return null;
    }
    throw error;
  }
}

export async function sendChatbotMessage(text: string, clientMessageId: string): Promise<void> {
  await sendJson<unknown>('POST', 'api/v4/Chatbot/SendChatMessage', { Text: text, ClientMessageId: clientMessageId });
}

export async function newChatbotSession(): Promise<void> {
  await sendJson<unknown>('POST', 'api/v4/Chatbot/NewChatSession', undefined);
}

// ---- Recipients (for new DM / group creation) ----

export interface PersonRecipient {
  userId: string;
  name: string;
}

export async function getPersonnelRecipients(): Promise<PersonRecipient[]> {
  const result = await getJson<ApiListResult<RecipientDto>>('api/v4/Messages/GetRecipients', {
    disallowNoone: true,
    includeUnits: false,
  });
  const data = result.Data ?? [];
  return data
    .filter((recipient) => recipient.Type === 'Personnel' && recipient.Id.startsWith('P:'))
    .map((recipient) => ({ userId: recipient.Id.slice(2), name: recipient.Name }));
}
