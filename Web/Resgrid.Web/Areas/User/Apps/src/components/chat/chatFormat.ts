// Small presentation helpers shared across the chat components.
import { ChatChannelType, ChatMessageType, type ChatChannelDto, type ChatMessageDto } from './types';

export interface LocationMetadata {
  latitude: number;
  longitude: number;
  label?: string;
}

export interface GifMetadata {
  url: string;
  previewUrl?: string;
  width?: number;
  height?: number;
}

export interface LinkMetadata {
  url: string;
  title?: string;
  description?: string;
  imageUrl?: string;
}

export interface ChatMessageMetadata {
  location?: LocationMetadata;
  gif?: GifMetadata;
  link?: LinkMetadata;
}

function readNumber(value: unknown): number | undefined {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === 'string') {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : undefined;
  }
  return undefined;
}

function readString(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

// Only https:// URLs may be rendered as href/src targets (blocks javascript:, data:, etc).
export function isSafeUrl(url: string | null | undefined): boolean {
  return typeof url === 'string' && /^https:\/\//i.test(url);
}

// Parses MetadataJson defensively; tolerates both camelCase and PascalCase keys.
export function parseMetadata(metadataJson: string | null | undefined): ChatMessageMetadata {
  if (!metadataJson) {
    return {};
  }

  let raw: Record<string, unknown>;
  try {
    raw = JSON.parse(metadataJson) as Record<string, unknown>;
  } catch {
    return {};
  }

  const result: ChatMessageMetadata = {};
  const location = (raw.location ?? raw.Location) as Record<string, unknown> | undefined;
  if (location) {
    const latitude = readNumber(location.latitude ?? location.Latitude);
    const longitude = readNumber(location.longitude ?? location.Longitude);
    if (latitude !== undefined && longitude !== undefined) {
      result.location = {
        latitude,
        longitude,
        label: readString(location.label ?? location.Label),
      };
    }
  }

  const gif = (raw.gif ?? raw.Gif) as Record<string, unknown> | undefined;
  if (gif) {
    const url = readString(gif.url ?? gif.Url ?? gif.gifUrl ?? gif.GifUrl);
    if (url && isSafeUrl(url)) {
      const previewUrl = readString(gif.previewUrl ?? gif.PreviewUrl);
      result.gif = {
        url,
        previewUrl: previewUrl && isSafeUrl(previewUrl) ? previewUrl : undefined,
        width: readNumber(gif.width ?? gif.Width),
        height: readNumber(gif.height ?? gif.Height),
      };
    }
  }

  const link = (raw.link ?? raw.Link) as Record<string, unknown> | undefined;
  if (link) {
    const url = readString(link.url ?? link.Url);
    if (url && isSafeUrl(url)) {
      const imageUrl = readString(link.imageUrl ?? link.ImageUrl);
      result.link = {
        url,
        title: readString(link.title ?? link.Title),
        description: readString(link.description ?? link.Description),
        imageUrl: imageUrl && isSafeUrl(imageUrl) ? imageUrl : undefined,
      };
    }
  }

  return result;
}

export function stringifyMetadata(metadata: ChatMessageMetadata): string | null {
  if (!metadata.location && !metadata.gif && !metadata.link) {
    return null;
  }
  return JSON.stringify(metadata);
}

const URL_PATTERN = /(https?:\/\/[^\s<]+)/gi;

export interface TextSegment {
  text: string;
  href?: string;
}

export function linkifySegments(text: string): TextSegment[] {
  const segments: TextSegment[] = [];
  let lastIndex = 0;
  let match: RegExpExecArray | null;
  URL_PATTERN.lastIndex = 0;

  while ((match = URL_PATTERN.exec(text)) !== null) {
    if (match.index > lastIndex) {
      segments.push({ text: text.slice(lastIndex, match.index) });
    }
    segments.push({ text: match[0], href: match[0] });
    lastIndex = match.index + match[0].length;
  }

  if (lastIndex < text.length) {
    segments.push({ text: text.slice(lastIndex) });
  }

  return segments;
}

export function initialsFor(name: string | null | undefined): string {
  const trimmed = (name ?? '').trim();
  if (trimmed.length === 0) {
    return '?';
  }
  const parts = trimmed.split(/\s+/).slice(0, 2);
  return parts.map((part) => part.charAt(0).toUpperCase()).join('');
}

// Deterministic accent color from an identifier for avatars.
const AVATAR_COLORS = ['#1ab394', '#2f4050', '#23c6c8', '#f8ac59', '#ed5565', '#5b6bc0', '#8e44ad', '#16a085'];

export function colorFor(seed: string | null | undefined): string {
  const value = seed ?? '';
  let hash = 0;
  for (let index = 0; index < value.length; index += 1) {
    hash = (hash * 31 + value.charCodeAt(index)) & 0xffffffff;
  }
  return AVATAR_COLORS[Math.abs(hash) % AVATAR_COLORS.length];
}

export function formatClockTime(iso: string | null | undefined): string {
  if (!iso) {
    return '';
  }
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
}

export function formatRelativeDay(iso: string | null | undefined): string {
  if (!iso) {
    return '';
  }
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  const now = new Date();
  const oneDay = 24 * 60 * 60 * 1000;
  const startOfDay = (value: Date) => new Date(value.getFullYear(), value.getMonth(), value.getDate()).getTime();
  const diffDays = Math.round((startOfDay(now) - startOfDay(date)) / oneDay);

  if (diffDays === 0) {
    return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  }
  if (diffDays === 1) {
    return new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' }).format(-1, 'day');
  }
  if (diffDays < 7) {
    return date.toLocaleDateString([], { weekday: 'short' });
  }
  return date.toLocaleDateString([], { month: 'short', day: 'numeric' });
}

export function channelDisplayName(channel: ChatChannelDto): string {
  if (channel.Name && channel.Name.trim().length > 0) {
    return channel.Name;
  }
  switch (channel.ChannelType) {
    case ChatChannelType.DirectMessage:
      return 'Direct Message';
    case ChatChannelType.Chatbot:
      return 'Assistant';
    default:
      return 'Channel';
  }
}

export interface ChannelGroup {
  key: string;
  label: string;
  channels: ChatChannelDto[];
}

export function groupChannels(channels: ChatChannelDto[]): ChannelGroup[] {
  const dms: ChatChannelDto[] = [];
  const incidents: ChatChannelDto[] = [];
  const assistant: ChatChannelDto[] = [];
  const rooms: ChatChannelDto[] = [];

  for (const channel of channels) {
    if (channel.IsArchived) {
      continue;
    }
    switch (channel.ChannelType) {
      case ChatChannelType.DirectMessage:
        dms.push(channel);
        break;
      case ChatChannelType.Chatbot:
        assistant.push(channel);
        break;
      case ChatChannelType.Incident:
      case ChatChannelType.IncidentLane:
      case ChatChannelType.IncidentCommand:
      case ChatChannelType.IncidentLeads:
      case ChatChannelType.IncidentDispatch:
        incidents.push(channel);
        break;
      default:
        rooms.push(channel);
        break;
    }
  }

  const sortByRecent = (a: ChatChannelDto, b: ChatChannelDto) => {
    const aTime = a.LastMessageOn ? new Date(a.LastMessageOn).getTime() : 0;
    const bTime = b.LastMessageOn ? new Date(b.LastMessageOn).getTime() : 0;
    return bTime - aTime;
  };

  const groups: ChannelGroup[] = [
    { key: 'dms', label: 'Direct Messages', channels: dms.sort(sortByRecent) },
    { key: 'channels', label: 'Channels', channels: rooms.sort(sortByRecent) },
    { key: 'incidents', label: 'Incidents', channels: incidents.sort(sortByRecent) },
    { key: 'assistant', label: 'Assistant', channels: assistant.sort(sortByRecent) },
  ];

  return groups.filter((group) => group.channels.length > 0);
}

export function messagePreview(message: ChatMessageDto | undefined): string {
  if (!message) {
    return '';
  }
  if (message.DeletedOn) {
    return 'Message deleted';
  }
  switch (message.MessageType) {
    case ChatMessageType.Image:
      return '📷 Photo';
    case ChatMessageType.Gif:
      return 'GIF';
    case ChatMessageType.Location:
      return '📍 Location';
    default:
      return message.Body ?? '';
  }
}

export function newClientMessageId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `cmid-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}
