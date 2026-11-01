import { useMemo } from 'react';
import { EmptyState, Legend, MiniMap, type MiniMapMarker } from '../../primitives';
import type { WidgetProps } from '../types';
import './awarenessWidgets.css';

interface VizCategory {
  Key: string;
  Label?: string | null;
  Color?: string | null;
  Count: number;
  Order: number;
}

interface VizLayer {
  LayerId: string;
  Display: string;
  Categories: VizCategory[];
  /** [lat, lon, categoryIndex, metric, referenceId] */
  Points: Array<Array<number | null>>;
  Total: number;
  Truncated: boolean;
}

/** Core/Resgrid.Model/Dashboard/Widgets/Awareness/MapVisualizationWidgetData.cs (Result = MapVisualizationResult, trimmed to what the widget reads). */
export interface MapVisualizationWidgetData {
  Configured: boolean;
  Found: boolean;
  MapVisualizationId?: string | null;
  Name?: string | null;
  Description?: string | null;
  TemplateKey?: string | null;
  IsShared: boolean;
  UpdatedOnUtc?: string | null;
  Url?: string | null;
  Result?: {
    Layers: VizLayer[];
    Stations?: Array<{ GroupId: number; Name: string; Latitude: number; Longitude: number }>;
    Truncated?: boolean;
    CenterLatitude?: number | null;
    CenterLongitude?: number | null;
  } | null;
}

/** The most dots drawn; a heat-map-sized result is sampled evenly so the widget stays light. */
const MAX_DOTS = 1500;

/**
 * map.visualization (plan §7.4): a saved visualization drawn compactly — the result's points as dots colored by their
 * category (colors from the result), stations, and the top categories as a key. The full heat/hex rendering stays on
 * the Visualizations page (the title links there).
 */
export default function MapVisualizationWidget({ data, size, t, ctx }: WidgetProps<MapVisualizationWidgetData>) {
  const layers = data.Result?.Layers ?? [];

  const markers = useMemo<MiniMapMarker[]>(() => {
    const result: MiniMapMarker[] = [];
    const total = layers.reduce((sum, layer) => sum + (layer.Points?.length ?? 0), 0);
    const stride = Math.max(1, Math.ceil(total / MAX_DOTS));
    let index = 0;
    for (const layer of layers) {
      for (const point of layer.Points ?? []) {
        index += 1;
        if (index % stride !== 0) continue;
        const [lat, lng, category] = point;
        if (typeof lat !== 'number' || typeof lng !== 'number') continue;
        const entry = typeof category === 'number' && category >= 0 ? layer.Categories?.[category] : undefined;
        result.push({ id: `${layer.LayerId}:${index}`, lat, lng, radius: 4, color: entry?.Color || undefined, tooltip: entry?.Label || entry?.Key || undefined, layer: layer.LayerId });
      }
    }
    for (const station of data.Result?.Stations ?? []) {
      if (Number.isFinite(station.Latitude) && Number.isFinite(station.Longitude)) {
        result.push({ id: `station:${station.GroupId}`, lat: station.Latitude, lng: station.Longitude, kind: 'station', tooltip: station.Name });
      }
    }
    return result;
  }, [layers, data.Result?.Stations]);

  if (!data.Configured) {
    return <EmptyState icon="fa-map-o" text={t('W.map.visualization.NotConfigured')} linkText={t('W.map.visualization.Choose')} onAction={ctx.openSettings} />;
  }
  if (!data.Found) {
    return <EmptyState icon="fa-map-o" text={t('W.map.visualization.NotFound')} linkText={t('W.map.visualization.Choose')} onAction={ctx.openSettings} />;
  }

  const categories = layers
    .flatMap((layer) => layer.Categories ?? [])
    .filter((category) => category.Count > 0 && category.Color)
    .sort((a, b) => b.Count - a.Count)
    .slice(0, size === 'xl' ? 6 : 3);
  const rows = layers.reduce((sum, layer) => sum + (layer.Total ?? 0), 0);
  const truncated = data.Result?.Truncated || layers.some((layer) => layer.Truncated);
  const center =
    typeof data.Result?.CenterLatitude === 'number' && typeof data.Result?.CenterLongitude === 'number'
      ? ([data.Result.CenterLatitude, data.Result.CenterLongitude] as [number, number])
      : undefined;

  return (
    <div className="rg-mapviz-wrap">
      <div className="rg-mapviz-head">
        <a href={data.Url ?? '#'} className="rg-mapviz-name">
          {data.Name}
        </a>
        {data.IsShared ? <span className="rg-tag">{t('W.map.visualization.Shared')}</span> : null}
        <span className="rg-mu rg-mapviz-meta">
          {t('W.map.visualization.Rows', ctx.format.number(rows, 0))}
          {truncated ? ` · ${t('W.map.visualization.Partial')}` : ''}
        </span>
      </div>
      {categories.length > 0 ? (
        <Legend
          className="rg-mapviz-key"
          items={categories.map((category) => ({ label: category.Label || category.Key, value: ctx.format.number(category.Count, 0), color: category.Color as string }))}
        />
      ) : null}
      <div className="rg-mapviz-map">
        <MiniMap
          markers={markers}
          center={center}
          zoom={11}
          ariaLabel={data.Name || t('W.map.visualization.Title')}
        />
      </div>
    </div>
  );
}
