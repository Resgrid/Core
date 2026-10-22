import type { VizBootstrap, VizDefinition, VizLayer, VizSource } from './vizTypes';

export function defaultDistanceUnit(): 'km' | 'mi' {
  const language = typeof navigator !== 'undefined' ? navigator.language || '' : '';
  return /-(US|GB|LR|MM)$/i.test(language) ? 'mi' : 'km';
}

export function newLayerId(definition: VizDefinition): string {
  let index = definition.Layers.length + 1;
  const used = new Set(definition.Layers.map((layer) => layer.Id));
  while (used.has(`layer-${index}`)) {
    index += 1;
  }
  return `layer-${index}`;
}

export function newLayer(id: string, source: VizSource = 'calls'): VizLayer {
  const isTrack = source === 'unitTracks' || source === 'personnelTracks';
  return {
    Id: id,
    Name: null,
    Source: source,
    Display: isTrack ? 'tracks' : 'heat',
    ColorBy: 'none',
    Metric: 'count',
    Aggregate: 'count',
    Visible: true,
    Style: {
      Palette: 'heat',
      Opacity: 0.75,
      Radius: 22,
      Intensity: 1,
      CellSizeMeters: 800,
      PointSize: 6,
      SizeByMetric: false,
    },
  };
}

export function defaultDefinition(): VizDefinition {
  return {
    Version: 1,
    Filters: {
      LastDays: 365,
      Start: null,
      End: null,
      CallTypes: [],
      Priorities: [],
      Outcomes: [],
      GroupIds: [],
      UnitIds: [],
      Hours: [],
      DaysOfWeek: [],
      CallId: null,
      MinCallsAtLocation: 0,
    },
    Layers: [newLayer('layer-1')],
    Options: {
      ShowStations: true,
      StationRings: [],
      DistanceUnit: defaultDistanceUnit(),
      DistanceBands: [],
      MinuteBands: [],
    },
  };
}

export function cloneDefinition(definition: VizDefinition): VizDefinition {
  return JSON.parse(JSON.stringify(definition)) as VizDefinition;
}

/**
 * Keeps a layer inside what its source supports (after the source changes in the editor): an unsupported display, color
 * or metric falls back to the source's first choice. The server applies the same rules; this keeps the editor honest.
 */
export function fitLayerToSource(layer: VizLayer, bootstrap: VizBootstrap): VizLayer {
  const options = bootstrap.SourceOptions[layer.Source];
  if (!options) {
    return layer;
  }

  const next = { ...layer, Style: { ...layer.Style } };
  if (!options.Displays.includes(next.Display)) {
    next.Display = options.Displays[0];
  }
  if (!options.ColorBys.includes(next.ColorBy) || next.Display === 'heat') {
    next.ColorBy = 'none';
  }
  if (!options.Metrics.includes(next.Metric)) {
    next.Metric = 'count';
  }
  if (next.Metric === 'count') {
    next.Aggregate = 'count';
  } else if (next.Aggregate === 'count') {
    next.Aggregate = 'median';
  }
  return next;
}

/**
 * The parts of a definition the server's answer depends on. Style, visibility and layer names are drawn in the browser,
 * so changing them re-renders without a new request.
 */
export function dataSignature(definition: VizDefinition): string {
  return JSON.stringify({
    Filters: definition.Filters,
    Options: {
      DistanceUnit: definition.Options.DistanceUnit,
      DistanceBands: definition.Options.DistanceBands,
      MinuteBands: definition.Options.MinuteBands,
      ShowStations: definition.Options.ShowStations,
      StationRings: definition.Options.StationRings.length > 0,
    },
    Layers: definition.Layers.map((layer) => [layer.Id, layer.Source, layer.Display, layer.ColorBy, layer.Metric]),
  });
}

export function parseNumberList(text: string): number[] {
  return text
    .split(/[,;\s]+/)
    .map((part) => Number.parseFloat(part.replace(',', '.')))
    .filter((value) => Number.isFinite(value) && value > 0)
    .sort((a, b) => a - b);
}

export const windowChoices: { days: number | null; key: string }[] = [
  { days: 1, key: 'Window_1' },
  { days: 7, key: 'Window_7' },
  { days: 30, key: 'Window_30' },
  { days: 90, key: 'Window_90' },
  { days: 365, key: 'Window_365' },
  { days: 730, key: 'Window_730' },
  { days: 1825, key: 'Window_1825' },
  { days: null, key: 'Window_Custom' },
];
