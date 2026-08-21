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
}

function trimTrailingSlash(value: string): string {
  return value.endsWith('/') ? value.slice(0, -1) : value;
}

export function getBrowserConfig(): BrowserConfig {
  return {
    apiBaseUrl: `${window.location.origin}/api/web-bff`,
    googleMapsKey: window.rgGoogleMapsKey?.trim() || '',
    channelUrl: trimTrailingSlash(window.rgChannelUrl?.trim() || 'https://events.resgrid.com'),
  };
}
