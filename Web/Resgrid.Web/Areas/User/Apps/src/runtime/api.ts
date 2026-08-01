import { getAccessToken } from './auth';
import { getBrowserConfig } from './browserConfig';

export class ApiError extends Error {
  public readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

export type ApiQuery = Record<string, string | number | boolean | null | undefined>;

export function buildApiUrl(path: string, query?: ApiQuery): string {
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

export function apiAuthHeaders(extra?: HeadersInit): Headers {
  const headers = new Headers(extra);
  headers.set('Accept', 'application/json');
  const accessToken = getAccessToken();
  if (accessToken.length > 0) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }
  return headers;
}

export async function apiFetchJson<TResponse>(path: string, init?: RequestInit, query?: ApiQuery): Promise<TResponse> {
  const response = await fetch(buildApiUrl(path, query), {
    ...init,
    headers: apiAuthHeaders(init?.headers),
  });

  if (!response.ok) {
    throw new ApiError(response.status, `${response.status} ${response.statusText}`);
  }

  const text = await response.text();
  return (text.length > 0 ? JSON.parse(text) : {}) as TResponse;
}

export async function siteFetchJson<TResponse>(path: string, init?: RequestInit): Promise<TResponse> {
  const headers = new Headers(init?.headers);
  headers.set('Accept', 'application/json');

  const response = await fetch(path, {
    ...init,
    credentials: 'same-origin',
    headers,
  });

  if (!response.ok) {
    throw new ApiError(response.status, `${response.status} ${response.statusText}`);
  }

  const text = await response.text();
  return (text.length > 0 ? JSON.parse(text) : {}) as TResponse;
}
