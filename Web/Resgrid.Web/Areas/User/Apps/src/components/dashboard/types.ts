// Wire shapes of the /User/Dashboard endpoints (Areas/User/Models/Dashboard/DashboardModels.cs). The web app's JSON
// naming policy is null, so every property is PascalCase exactly as the C# classes declare it.

export type SizeToken = 'xs' | 's' | 'm' | 'l' | 'xl' | 'full';
export type Density = 'comfortable' | 'compact';
export type DashboardMode = 'board' | 'template';

export interface LayoutWidget {
  /** Client-generated instance id, unique within the board. */
  Id: string;
  Key: string;
  Size: string;
  Settings: Record<string, unknown>;
  Collapsed?: boolean;
}

export interface DashboardLayout {
  SchemaVersion: number;
  Density: string;
  Widgets: LayoutWidget[];
}

export interface BoardResult {
  /** UserDashboards id; null while the member has no saved row for this board (resolved template/default). */
  BoardId: string | null;
  Name: string | null;
  RowVersion: number;
  /** "user", "template" or "default". */
  Source: string;
  Layout: DashboardLayout;
}

export interface BoardSummary {
  BoardId: string | null;
  Name: string;
  IsHome: boolean;
}

export interface SettingOption {
  Value: string;
  Label: string;
}

export type SettingType = 'number' | 'toggle' | 'select' | 'multiSelect' | 'text';

export interface WidgetSetting {
  Name: string;
  Type: SettingType;
  Label: string;
  Min?: number | null;
  Max?: number | null;
  Default?: unknown;
  Options?: SettingOption[] | null;
  OptionsSource?: string | null;
}

export interface WidgetDefinition {
  Key: string;
  Version: number;
  Category: string;
  Title: string;
  Description: string;
  /** Font Awesome 4.7 class, e.g. "fa-bullhorn". */
  Icon: string;
  ModuleUrl?: string | null;
  AllowedSizes: string[];
  DefaultSize: string;
  Settings: WidgetSetting[];
  RealtimeTopics: string[];
  DefaultRefreshSeconds: number;
  MinRefreshSeconds: number;
  MultipleInstances: boolean;
  HasProtectedFields: boolean;
  /** ADP is enforced and the viewer's ADP permission covers at least one of this widget's protected fields. */
  CanRevealProtected?: boolean;
}

export interface LockedWidget {
  Key: string;
  Category: string;
  Title: string;
  Description: string;
  Icon: string;
  AddonName: string;
  AddonUrl: string;
}

export interface CategoryInfo {
  Key: string;
  Name: string;
  Color: string;
}

export interface HeaderInfo {
  FirstName?: string | null;
  DepartmentId?: number;
  DepartmentName?: string | null;
  DepartmentCode?: string | null;
  /** IANA zone for Intl. */
  TimeZone?: string | null;
  Audience?: string | null;
  IsDepartmentAdmin: boolean;
  CanCreateCall: boolean;
  CanSendMessage: boolean;
  CanEditTemplates: boolean;
  NewCallUrl?: string | null;
  MessageUrl?: string | null;
  ClassicUrl?: string | null;
  TemplatesUrl?: string | null;
}

export interface AdpInfo {
  Enforced: boolean;
  CanRevealCall: boolean;
  CanRevealOperational: boolean;
  CanRevealPersonnel: boolean;
  CanRevealContact: boolean;
}

export interface MapConfig {
  TileUrl?: string | null;
  Attribution?: string | null;
  MapProvider?: string | null;
  StyleUrl?: string | null;
  AccessToken?: string | null;
  /** "maplibre" when the department renders web maps with MapLibre GL (Maps.MapLibreWeb). */
  Renderer?: string | null;
  MapLibreStyleUrl?: string | null;
  MapLibreStyleUrlNight?: string | null;
}

export interface TemplateInfo {
  TemplateId?: string | null;
  Name: string;
  Description?: string | null;
  /** 0 everyone, 1 personnel role, 2 group. */
  Audience: number;
  TargetId?: number | null;
  Priority: number;
  IsDefault: boolean;
}

export interface Bootstrap {
  Board: BoardResult;
  Boards: BoardSummary[];
  UseClassic: boolean;
  Widgets: WidgetDefinition[];
  Locked: LockedWidget[];
  Categories: CategoryInfo[];
  Header: HeaderInfo | null;
  Adp: AdpInfo | null;
  Map: MapConfig | null;
  Options: Record<string, SettingOption[]>;
  DepartmentId: number;
  MaxBoards: number;
  MaxWidgets: number;
  Template?: TemplateInfo | null;
}

export interface WidgetError {
  /** "failed", "forbidden", "protected" or "notFound". */
  Code: string;
  MessageKey?: string | null;
}

export interface WidgetDataResult {
  Data: unknown;
  GeneratedUtc?: string | null;
  Error?: WidgetError | null;
}

export interface SaveResponse {
  /** "Conflict", "Invalid", "TooLarge", "NotFound", "LimitReached", "NotAllowed"; null when saved. */
  Error?: string | null;
  Board?: BoardResult | null;
  Current?: BoardResult | null;
}

export interface PulseItem {
  Key: string;
  Icon: string;
  Value: number;
  Label: string;
  Url?: string | null;
  /** "", "warn" or "bad". */
  Tone?: string | null;
}

export interface TemplateTargets {
  Roles: SettingOption[];
  Groups: SettingOption[];
}

export interface TemplateSaveInput {
  TemplateId: string | null;
  Name: string;
  Description: string | null;
  Audience: number;
  TargetId: number | null;
  Priority: number;
  IsDefault: boolean;
  Layout: DashboardLayout;
}

export interface TemplateSaveResponse {
  TemplateId?: string | null;
  Error?: string | null;
}

/** A DTO field that may carry an ADP-catalogued value (Core/Resgrid.Model/Dashboard/DashboardWidgetContext.cs). */
export interface DashboardProtectedText {
  Value?: string | null;
  IsProtected: boolean;
  CanReveal: boolean;
}

// The ADP reveal module (wwwroot/js/app/internal/dataprotection/resgrid.adp.reveal.js) and the host-page hooks it calls.
export interface AdpRevealModule {
  applyGrantHeader?: (target: Headers) => void;
  hasLiveGrant?: () => boolean;
}

export interface AdpWindow {
  resgridAdpReveal?: AdpRevealModule;
  resgridAdpPageRevealed?: (values?: unknown) => void;
  resgridAdpPageConcealed?: () => void;
  resgridAdpPageRenewed?: () => void;
}

export function adpWindow(): AdpWindow {
  return window as unknown as AdpWindow;
}
