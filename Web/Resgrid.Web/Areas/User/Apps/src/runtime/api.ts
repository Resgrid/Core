import { getAccessToken } from './auth';
import { getBrowserConfig } from './browserConfig';

export async function apiFetchJson<TResponse>(path: string, init?: RequestInit): Promise<TResponse> {
  const { apiBaseUrl } = getBrowserConfig();
  const requestUrl = new URL(path, `${apiBaseUrl}/`);
  const headers = new Headers(init?.headers);
  const accessToken = getAccessToken();

  headers.set('Accept', 'application/json');

  if (accessToken.length > 0) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }

  const response = await fetch(requestUrl, {
    ...init,
    headers,
  });

  if (!response.ok) {
    throw new Error(`API request failed with ${response.status}: ${response.statusText}`);
  }

  return (await response.json()) as TResponse;
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
    throw new Error(`Request failed with ${response.status}: ${response.statusText}`);
  }

  return (await response.json()) as TResponse;
}
