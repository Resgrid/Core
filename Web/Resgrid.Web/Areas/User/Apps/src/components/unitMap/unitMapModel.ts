import { mapMarkerTypes, type MapMarkerInfo } from '../map/mapTypes';

export interface UnitMapCrewMember {
  UserId: string;
  Name: string;
  RoleName: string;
  /** Null when the member has no current position or the viewer may not see it. */
  Marker: MapMarkerInfo | null;
  LocationTimestamp: string | null;
}

export interface UnitMapData {
  UnitId: string;
  Name: string;
  Type: string | null;
  GroupName: string | null;
  CenterLat: number;
  CenterLon: number;
  ZoomLevel: number;
  /** Null when the unit has no current position or the viewer may not see its location. */
  UnitMarker: MapMarkerInfo | null;
  UnitLocationTimestamp: string | null;
  Crew: UnitMapCrewMember[];
}

export interface GetUnitMapDataResult {
  Status?: string;
  Data: UnitMapData | null;
}

export interface LivePosition {
  latitude: number;
  longitude: number;
  /** Fix time in ms, or null when the server did not send one. */
  timestamp: number | null;
  receivedAt: number;
}

export function unitMarkerKey(unitId: string | number): string {
  return `u${unitId}`.toLowerCase();
}

export function personMarkerKey(userId: string): string {
  return `p${userId}`.toLowerCase();
}

export function parseTimestamp(value: string | null | undefined): number | null {
  if (!value) {
    return null;
  }

  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? null : parsed;
}

export function isValidCoordinate(latitude: number, longitude: number): boolean {
  return (
    Number.isFinite(latitude) &&
    Number.isFinite(longitude) &&
    Math.abs(latitude) <= 90 &&
    Math.abs(longitude) <= 180 &&
    !(latitude === 0 && longitude === 0)
  );
}

/**
 * Applies a pushed fix to the live positions, or returns the same object when it changes nothing: a fix older than
 * the one already held is dropped, because trackers replay buffered fixes and queue consumers can reorder them.
 */
export function applyLivePosition(
  positions: Record<string, LivePosition>,
  key: string,
  latitude: number,
  longitude: number,
  fixTime: number | null,
  receivedAt: number,
): Record<string, LivePosition> {
  const existing = positions[key];

  if (existing && fixTime !== null && existing.timestamp !== null && fixTime < existing.timestamp) {
    return positions;
  }

  if (existing && existing.latitude === latitude && existing.longitude === longitude && existing.timestamp === fixTime) {
    return positions;
  }

  return { ...positions, [key]: { latitude, longitude, timestamp: fixTime, receivedAt } };
}

/** A reload's snapshot can be older than a push that arrived while it was in flight, so those pushes survive it. */
export function keepPositionsReceivedSince(
  positions: Record<string, LivePosition>,
  since: number,
): Record<string, LivePosition> {
  return Object.fromEntries(Object.entries(positions).filter(([, position]) => position.receivedAt >= since));
}

export interface UnitMapEntry {
  key: string;
  marker: MapMarkerInfo | null;
  /** Fix or status time in ms; a live fix wins over the loaded one. */
  locatedAt: number | null;
}

export interface UnitMapView {
  unit: UnitMapEntry;
  crew: Array<UnitMapEntry & { member: UnitMapCrewMember }>;
  /** Every marker to draw: the unit first, then its crew. */
  markers: MapMarkerInfo[];
}

function placeMarker(
  key: string,
  loaded: MapMarkerInfo | null,
  loadedAt: string | null,
  live: LivePosition | undefined,
  fallback: () => MapMarkerInfo,
): UnitMapEntry {
  if (!live) {
    return { key, marker: loaded, locatedAt: loaded ? parseTimestamp(loadedAt) : null };
  }

  // A member with no loaded position who starts reporting gets a plain pin at once; the reload this triggers brings
  // the real icon.
  const base = loaded ?? fallback();

  return {
    key,
    marker: { ...base, Latitude: live.latitude, Longitude: live.longitude },
    locatedAt: live.timestamp ?? live.receivedAt,
  };
}

/** The unit and crew markers with the live fixes applied. */
export function buildUnitMapView(data: UnitMapData, livePositions: Record<string, LivePosition>): UnitMapView {
  const unitKey = unitMarkerKey(data.UnitId);
  const unit = placeMarker(unitKey, data.UnitMarker, data.UnitLocationTimestamp, livePositions[unitKey], () => ({
    Id: `u${data.UnitId}`,
    Latitude: 0,
    Longitude: 0,
    Title: data.Name,
    zIndex: 0,
    ImagePath: 'Engine_Responding',
    InfoWindowContent: '',
    Color: '',
    Type: mapMarkerTypes.unit,
  }));

  const crew = data.Crew.map((member) => {
    const key = personMarkerKey(member.UserId);
    const entry = placeMarker(key, member.Marker, member.LocationTimestamp, livePositions[key], () => ({
      Id: `p${member.UserId}`,
      Latitude: 0,
      Longitude: 0,
      Title: member.RoleName ? `${member.Name} (${member.RoleName})` : member.Name,
      zIndex: 0,
      ImagePath: 'Person_RespondingCall',
      InfoWindowContent: '',
      Color: '',
      Type: mapMarkerTypes.personnel,
    }));

    return { ...entry, member };
  });

  const markers = [unit.marker, ...crew.map((entry) => entry.marker)].filter(
    (marker): marker is MapMarkerInfo => marker !== null && isValidCoordinate(marker.Latitude, marker.Longitude),
  );

  return { unit, crew, markers };
}

/** True when a pushed fix belongs to this unit or one of its seated members. */
export function isTrackedKey(data: UnitMapData | null, key: string): boolean {
  if (!data) {
    return false;
  }

  return key === unitMarkerKey(data.UnitId) || data.Crew.some((member) => personMarkerKey(member.UserId) === key);
}

/** True when the tracked entity behind the key has no loaded marker, so its pin is a stand-in until a reload. */
export function isMissingLoadedMarker(data: UnitMapData | null, key: string): boolean {
  if (!data) {
    return false;
  }

  if (key === unitMarkerKey(data.UnitId)) {
    return data.UnitMarker === null;
  }

  return data.Crew.some((member) => personMarkerKey(member.UserId) === key && member.Marker === null);
}
