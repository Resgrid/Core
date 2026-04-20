import { getBrowserConfig } from './browserConfig';

export interface StoredTokens {
  access_token: string;
  refresh_token?: string;
  id_token?: string;
  expiration_date?: string;
  [key: string]: unknown;
}

export function getStoredTokens(): StoredTokens | null {
  const storageKey = getBrowserConfig().tokenStorageKey;
  const rawValue = window.localStorage.getItem(storageKey);

  if (!rawValue) {
    return null;
  }

  try {
    const parsedValue = JSON.parse(rawValue) as StoredTokens;

    if (typeof parsedValue?.access_token === 'string' && parsedValue.access_token.length > 0) {
      return parsedValue;
    }

    return null;
  } catch {
    if (rawValue.trim().length > 0) {
      return {
        access_token: rawValue.trim(),
      };
    }

    return null;
  }
}

export function getAccessToken(): string {
  return getStoredTokens()?.access_token ?? '';
}
