// Mirrors Resgrid.Model MapVisualizationDefinition / MapVisualizationResult (PascalCase, as the MVC endpoints send it).

export type VizSource =
  | 'calls'
  | 'unitResponses'
  | 'personnelResponses'
  | 'responderOrigins'
  | 'unitStatusLocations'
  | 'unitTracks'
  | 'personnelTracks';

export type VizDisplay = 'heat' | 'points' | 'hexbins' | 'tracks';

export type VizColorBy =
  | 'none'
  | 'callType'
  | 'priority'
  | 'outcome'
  | 'group'
  | 'timeOfDay'
  | 'hourOfDay'
  | 'dayOfWeek'
  | 'weekpart'
  | 'month'
  | 'year'
  | 'unit'
  | 'unitType'
  | 'station'
  | 'person'
  | 'status'
  | 'responseBand'
  | 'distanceBand'
  | 'repeatBand';

export type VizMetric =
  | 'count'
  | 'responseMinutes'
  | 'turnoutMinutes'
  | 'durationMinutes'
  | 'unitsDispatched'
  | 'personnelResponded'
  | 'stationDistance'
  | 'callsAtLocation';

export type VizAggregate = 'count' | 'sum' | 'mean' | 'median' | 'p90' | 'max';

export type VizPalette = 'heat' | 'fire' | 'viridis' | 'blues' | 'traffic';

export interface VizFilters {
  LastDays: number | null;
  Start: string | null;
  End: string | null;
  CallTypes: string[];
  Priorities: number[];
  Outcomes: number[];
  GroupIds: number[];
  UnitIds: number[];
  Hours: number[];
  DaysOfWeek: number[];
  CallId: number | null;
  MinCallsAtLocation: number;
}

export interface VizLayerStyle {
  Palette: VizPalette;
  Opacity: number;
  Radius: number;
  Intensity: number;
  CellSizeMeters: number;
  PointSize: number;
  SizeByMetric: boolean;
}

export interface VizLayer {
  Id: string;
  Name: string | null;
  Source: VizSource;
  Display: VizDisplay;
  ColorBy: VizColorBy;
  Metric: VizMetric;
  Aggregate: VizAggregate;
  Visible: boolean;
  Style: VizLayerStyle;
}

export interface VizOptions {
  ShowStations: boolean;
  StationRings: number[];
  DistanceUnit: 'km' | 'mi';
  DistanceBands: number[];
  MinuteBands: number[];
}

export interface VizDefinition {
  Version: number;
  Filters: VizFilters;
  Layers: VizLayer[];
  Options: VizOptions;
}

export interface VizCategory {
  Key: string;
  Label: string;
  Color: string;
  Count: number;
  Order: number;
}

export interface VizTrack {
  Id: string;
  Label: string;
  CategoryIndex: number;
  /** [lat, lon, unixSeconds] */
  Coordinates: [number, number, number][];
}

/** [lat, lon, categoryIndex, metric, referenceId] */
export type VizPoint = [number, number, number, number | null, number];

export interface VizLayerResult {
  LayerId: string;
  Source: VizSource;
  Display: VizDisplay;
  ColorBy: VizColorBy;
  Metric: VizMetric;
  Categories: VizCategory[];
  Points: VizPoint[];
  Tracks: VizTrack[];
  Total: number;
  WithoutLocation: number;
  Truncated: boolean;
  Notices: string[];
}

export interface VizStation {
  GroupId: number;
  Name: string;
  Latitude: number;
  Longitude: number;
}

export interface VizSummary {
  Calls: number;
  CallsWithLocation: number;
  MedianResponseMinutes: number | null;
  P90ResponseMinutes: number | null;
  ByWeekdayHour: number[][];
  TopCallTypes: VizCategory[];
}

export interface VizCallFocus {
  CallId: number;
  Number: string;
  Name: string;
  Type: string;
  Address: string;
  LoggedOnUtc: string;
  ClosedOnUtc: string | null;
  Latitude: number | null;
  Longitude: number | null;
}

export interface VizResult {
  StartUtc: string;
  EndUtc: string;
  /** Department-local window bounds (no zone suffix), for display. */
  StartLocal: string;
  EndLocal: string;
  Definition: VizDefinition;
  Layers: VizLayerResult[];
  Stations: VizStation[];
  Summary: VizSummary;
  Call: VizCallFocus | null;
  Truncated: boolean;
  CenterLatitude: number | null;
  CenterLongitude: number | null;
}

export interface VizTemplate {
  Key: string;
  Name: string;
  Description: string;
  Category: string;
  CategoryName: string;
  Recommended: boolean;
  NeedsCall: boolean;
  Definition: VizDefinition;
}

export interface VizSaved {
  MapVisualizationId: string;
  Name: string;
  Description: string | null;
  IsShared: boolean;
  IsOwner: boolean;
  CanEdit: boolean;
  CreatedByName: string | null;
  UpdatedOnUtc: string;
  TemplateKey: string | null;
  Definition: VizDefinition;
}

export interface VizOption {
  Value: string;
  Label: string;
}

/** Everything the page needs to build the editor: allowed keys per source, filter choices, limits. */
export interface VizBootstrap {
  Templates: VizTemplate[];
  Saved: VizSaved[];
  CanShare: boolean;
  SourceOptions: Record<string, { Displays: VizDisplay[]; ColorBys: VizColorBy[]; Metrics: VizMetric[] }>;
  CallTypes: VizOption[];
  Priorities: VizOption[];
  Outcomes: VizOption[];
  Groups: VizOption[];
  Units: VizOption[];
  Limits: {
    MaxWindowDays: number;
    MaxTrackWindowDays: number;
    MaxLayers: number;
    MaxNameLength: number;
    MaxDescriptionLength: number;
  };
}
