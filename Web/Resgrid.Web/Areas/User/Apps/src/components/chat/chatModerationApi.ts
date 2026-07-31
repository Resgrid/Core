// Typed fetch wrappers for the v4 ChatModeration API (admin-only surfaces).
import { getAccessToken } from '../../runtime/auth';
import { getBrowserConfig } from '../../runtime/browserConfig';
import { ChatApiError } from './chatApi';
import type {
  ApiItemResult,
  ApiListResult,
  ChatExportDto,
  ChatFlagDto,
  ChatModerationActionDto,
  ChatSettingsDto,
} from './types';

function buildUrl(path: string, query?: Record<string, string | number | boolean | null | undefined>): string {
  const { apiBaseUrl } = getBrowserConfig();
  const url = new URL(path, `${apiBaseUrl}/`);
  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value !== null && value !== undefined && value !== '') {
        url.searchParams.set(key, String(value));
      }
    }
  }
  return url.toString();
}

function authHeaders(extra?: HeadersInit): Headers {
  const headers = new Headers(extra);
  headers.set('Accept', 'application/json');
  const token = getAccessToken();
  if (token.length > 0) {
    headers.set('Authorization', `Bearer ${token}`);
  }
  return headers;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, { ...init, headers: authHeaders(init?.headers) });
  if (!response.ok) {
    throw new ChatApiError(response.status, `${response.status} ${response.statusText}`);
  }
  const text = await response.text();
  return (text.length > 0 ? JSON.parse(text) : {}) as T;
}

async function sendJson<T>(method: string, path: string, body?: unknown, query?: Record<string, string | number | boolean | null | undefined>): Promise<T> {
  const headers: Record<string, string> = {};
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }
  return request<T>(buildUrl(path, query), {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
}

async function listOrEmpty<T>(path: string, query?: Record<string, string | number | boolean | null | undefined>): Promise<T[]> {
  try {
    const result = await request<ApiListResult<T>>(buildUrl(path, query));
    return result.Data ?? [];
  } catch (error) {
    if (error instanceof ChatApiError && error.status === 404) {
      return [];
    }
    throw error;
  }
}

// ---- Flags ----

export async function getFlags(status: number, page = 0): Promise<ChatFlagDto[]> {
  return listOrEmpty<ChatFlagDto>('api/v4/ChatModeration/GetFlags', { status, page, pageSize: 50 });
}

export async function resolveFlag(flagId: string, resolution: number, resolutionNote: string): Promise<void> {
  await sendJson<unknown>('PUT', 'api/v4/ChatModeration/ResolveFlag', { Resolution: resolution, ResolutionNote: resolutionNote }, { flagId });
}

// ---- Actions ----

export async function moderatorDeleteMessage(messageId: string, reason: string): Promise<void> {
  await request<unknown>(buildUrl('api/v4/ChatModeration/DeleteMessage', { messageId, reason }), { method: 'DELETE' });
}

export async function muteUser(channelId: string, targetUserId: string, mutedUntil: string | null): Promise<void> {
  await sendJson<unknown>('POST', 'api/v4/ChatModeration/MuteUser', { TargetUserId: targetUserId, MutedUntil: mutedUntil }, { channelId });
}

export async function banUser(channelId: string, targetUserId: string, banned: boolean): Promise<void> {
  await sendJson<unknown>('POST', 'api/v4/ChatModeration/BanUser', { TargetUserId: targetUserId, Banned: banned }, { channelId });
}

export async function lockChannel(channelId: string, locked: boolean, reason: string): Promise<void> {
  await sendJson<unknown>('POST', 'api/v4/ChatModeration/LockChannel', { Locked: locked, Reason: reason }, { channelId });
}

export async function getActions(channelId: string | undefined, page = 0): Promise<ChatModerationActionDto[]> {
  return listOrEmpty<ChatModerationActionDto>('api/v4/ChatModeration/GetActions', { channelId, page, pageSize: 50 });
}

// ---- Settings ----

export async function getSettings(): Promise<ChatSettingsDto | null> {
  try {
    const result = await request<ApiItemResult<ChatSettingsDto>>(buildUrl('api/v4/ChatModeration/GetSettings'));
    return result.Data ?? null;
  } catch (error) {
    if (error instanceof ChatApiError && error.status === 404) {
      return null;
    }
    throw error;
  }
}

export async function updateSettings(input: ChatSettingsDto): Promise<ChatSettingsDto | null> {
  const result = await sendJson<ApiItemResult<ChatSettingsDto>>('PUT', 'api/v4/ChatModeration/UpdateSettings', {
    RetentionDays: input.RetentionDays,
    AllowImages: input.AllowImages,
    AllowGifs: input.AllowGifs,
    AllowLocationSharing: input.AllowLocationSharing,
    UrgentOverridesMute: input.UrgentOverridesMute,
    MaxAttachmentSizeMb: input.MaxAttachmentSizeMb,
    ChatbotEnabled: input.ChatbotEnabled,
  });
  return result.Data ?? null;
}

// ---- Exports ----

export async function requestExport(channelId: string | null, startDate: string | null, endDate: string | null, format: number): Promise<ChatExportDto[]> {
  const result = await sendJson<ApiListResult<ChatExportDto>>('POST', 'api/v4/ChatModeration/RequestExport', {
    ChatChannelId: channelId,
    StartDate: startDate,
    EndDate: endDate,
    Format: format,
  });
  return result.Data ?? [];
}

export async function getExports(): Promise<ChatExportDto[]> {
  return listOrEmpty<ChatExportDto>('api/v4/ChatModeration/GetExports');
}

export async function downloadExport(exportId: string): Promise<void> {
  const response = await fetch(buildUrl('api/v4/ChatModeration/DownloadExport', { exportId }), { headers: authHeaders() });
  if (!response.ok) {
    throw new ChatApiError(response.status, `${response.status} ${response.statusText}`);
  }
  const blob = await response.blob();
  const objectUrl = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = objectUrl;
  anchor.download = `chat-export-${exportId}`;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(objectUrl), 4000);
}
