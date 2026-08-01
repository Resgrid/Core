// Typed fetch wrappers for the v4 ChatModeration API (admin-only surfaces).
import { ApiError, apiAuthHeaders, buildApiUrl, type ApiQuery } from '../../runtime/api';
import { chatRequest, isApiStatus } from './chatApi';
import type {
  ApiItemResult,
  ApiListResult,
  ChatExportDto,
  ChatFlagDto,
  ChatModerationActionDto,
  ChatSettingsDto,
} from './types';

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

async function listOrEmpty<T>(path: string, query?: ApiQuery): Promise<T[]> {
  try {
    const result = await chatRequest<ApiListResult<T>>(path, undefined, query);
    return result.Data ?? [];
  } catch (error) {
    if (isApiStatus(error, 404)) {
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
  await chatRequest<unknown>('api/v4/ChatModeration/DeleteMessage', { method: 'DELETE' }, { messageId, reason });
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
    const result = await chatRequest<ApiItemResult<ChatSettingsDto>>('api/v4/ChatModeration/GetSettings');
    return result.Data ?? null;
  } catch (error) {
    if (isApiStatus(error, 404)) {
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
  const response = await fetch(buildApiUrl('api/v4/ChatModeration/DownloadExport', { exportId }), { headers: apiAuthHeaders() });
  if (!response.ok) {
    throw new ApiError(response.status, `${response.status} ${response.statusText}`);
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
