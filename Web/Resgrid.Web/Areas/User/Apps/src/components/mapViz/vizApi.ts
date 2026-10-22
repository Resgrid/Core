import { ApiError, siteFetchJson } from '../../runtime/api';
import type { VizBootstrap, VizDefinition, VizResult, VizSaved } from './vizTypes';

// MVC endpoints on MapVisualizationsController (same origin, cookie auth). Writes carry the page's anti-forgery token.
const base = '/User/MapVisualizations';

function verificationToken(): string {
  return document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? '';
}

async function postJson<TResponse>(path: string, body: unknown, signal?: AbortSignal): Promise<TResponse> {
  return siteFetchJson<TResponse>(`${base}/${path}`, {
    method: 'POST',
    signal,
    headers: {
      'Content-Type': 'application/json',
      RequestVerificationToken: verificationToken(),
    },
    body: JSON.stringify(body),
  });
}

export function getBootstrap(signal?: AbortSignal): Promise<VizBootstrap> {
  return siteFetchJson<VizBootstrap>(`${base}/Bootstrap`, { signal });
}

/** Runs a definition. A POST because a definition with its filter lists does not fit a query string. */
export function runVisualization(definition: VizDefinition, signal?: AbortSignal): Promise<VizResult> {
  return postJson<VizResult>('Data', definition, signal);
}

export interface SaveVisualizationRequest {
  MapVisualizationId: string | null;
  Name: string;
  Description: string | null;
  IsShared: boolean;
  TemplateKey: string | null;
  Definition: VizDefinition;
}

export interface SaveVisualizationResponse {
  Saved: VizSaved | null;
  Error: string | null;
}

export function saveVisualization(request: SaveVisualizationRequest): Promise<SaveVisualizationResponse> {
  return postJson<SaveVisualizationResponse>('Save', request);
}

export function deleteVisualization(mapVisualizationId: string): Promise<{ Deleted: boolean }> {
  return postJson<{ Deleted: boolean }>('Delete', { MapVisualizationId: mapVisualizationId });
}

export interface PointDetail {
  CallId: number;
  Number: string;
  Name: string;
  Type: string;
  Priority: string;
  State: string;
  Address: string;
  LoggedOn: string;
  Url: string;
}

export function getPointDetail(callId: number, signal?: AbortSignal): Promise<PointDetail> {
  return siteFetchJson<PointDetail>(`${base}/CallDetail?callId=${encodeURIComponent(String(callId))}`, { signal });
}

export interface CallSearchHit {
  CallId: number;
  Label: string;
}

export function searchCalls(term: string, signal?: AbortSignal): Promise<CallSearchHit[]> {
  return siteFetchJson<CallSearchHit[]>(`${base}/SearchCalls?term=${encodeURIComponent(term)}`, { signal });
}

export function isForbidden(error: unknown): boolean {
  return error instanceof ApiError && (error.status === 401 || error.status === 403);
}
