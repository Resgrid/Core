declare global {
  interface Window {
    rgApiBaseUrl?: string;
    rgGoogleMapsKey?: string;
    rgChannelUrl?: string;
  }
}

export interface BrowserConfig {
  apiBaseUrl: string;
  googleMapsKey: string;
  channelUrl: string;
  tokenStorageKey: string;
}

function trimTrailingSlash(value: string): string {
  return value.endsWith('/') ? value.slice(0, -1) : value;
}

export function getBrowserConfig(): BrowserConfig {
  return {
    apiBaseUrl: trimTrailingSlash(window.rgApiBaseUrl?.trim() || 'https://api.resgrid.com'),
    googleMapsKey: window.rgGoogleMapsKey?.trim() || '',
    channelUrl: trimTrailingSlash(window.rgChannelUrl?.trim() || 'https://events.resgrid.com'),
    tokenStorageKey: 'RgWebApp.auth-tokens',
  };
}
