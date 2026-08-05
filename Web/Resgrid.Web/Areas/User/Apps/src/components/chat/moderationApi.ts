import { ApiError, apiAuthHeaders, buildApiUrl, apiFetchJson, type ApiQuery } from '../../runtime/api';
import { moderationText } from './moderationI18n';

export interface ModerationReportDto {
  ModerationReportId: string;
  ReportedByUserId: string;
  ReporterGroupId?: number | null;
  Reason: number;
  Note?: string | null;
  ReportedOn: string;
}

export interface ModerationActionDto {
  ModerationActionId: string;
  ActionType: number;
  PerformedByUserId: string;
  PerformedOn: string;
  Note?: string | null;
  PreviousStatus?: number | null;
  NewStatus?: number | null;
  ActorRole?: string | null;
  IpAddress?: string | null;
  UserAgent?: string | null;
  TraceId?: string | null;
  ServerName?: string | null;
  DetailsJson?: string | null;
  HasEvidence: boolean;
}

export interface ModerationRequestDto {
  ModerationRequestId: string;
  ItemType: number;
  ItemId: string;
  CallId?: number | null;
  ChatChannelId?: string | null;
  ContentAuthorUserId?: string | null;
  ContentAuthorUnitId?: number | null;
  ContentCreatedOn?: string | null;
  OriginalSubject?: string | null;
  OriginalText?: string | null;
  OriginalFileName?: string | null;
  OriginalContentType?: string | null;
  HasOriginalContent: boolean;
  OriginalMetadataJson?: string | null;
  Status: number;
  Disposition: number;
  CreatedOn: string;
  ModifiedOn: string;
  CompletedByUserId?: string | null;
  CompletedOn?: string | null;
  AdminNote?: string | null;
  Reports: ModerationReportDto[];
  Actions: ModerationActionDto[];
}

interface ModerationListResult {
  Data?: ModerationRequestDto[];
}

interface ModerationItemResult {
  Data?: ModerationRequestDto | null;
}

export interface ModerationSearch {
  status?: number;
  itemType?: number;
  contentAuthorUserId?: string;
  reportedByUserId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export async function getModerationRequests(search: ModerationSearch): Promise<ModerationRequestDto[]> {
  try {
    const result = await apiFetchJson<ModerationListResult>('api/v4/Moderation/GetRequests', undefined, search as ApiQuery);
    return result.Data ?? [];
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      return [];
    }
    throw error;
  }
}

export async function getMyModerationRequest(itemType: number, itemId: string): Promise<ModerationRequestDto | null> {
  try {
    const result = await apiFetchJson<ModerationItemResult>(
      'api/v4/Moderation/GetMyStatus',
      undefined,
      { itemType, itemId },
    );
    return result.Data ?? null;
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      return null;
    }
    throw error;
  }
}

export async function completeModerationRequest(requestId: string, disposition: 1 | 2, adminNote: string): Promise<ModerationRequestDto | null> {
  const result = await apiFetchJson<ModerationItemResult>(
    'api/v4/Moderation/Complete',
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ Disposition: disposition, AdminNote: adminNote }),
    },
    { requestId },
  );
  return result.Data ?? null;
}

export async function downloadModerationEvidence(request: ModerationRequestDto): Promise<void> {
  const response = await fetch(
    buildApiUrl('api/v4/Moderation/DownloadEvidence', { requestId: request.ModerationRequestId }),
    { headers: apiAuthHeaders() },
  );
  if (!response.ok) {
    throw new ApiError(response.status, `${response.status} ${response.statusText}`);
  }

  const blob = await response.blob();
  const objectUrl = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = objectUrl;
  anchor.download = request.OriginalFileName || moderationText('EvidenceFileNameFormat', request.ModerationRequestId);
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(objectUrl), 4000);
}
