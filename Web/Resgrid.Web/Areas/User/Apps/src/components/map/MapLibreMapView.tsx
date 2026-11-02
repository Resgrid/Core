import { GlMapView, type GlMapLibrary } from './MapboxMapView';
import type { MapRendererProps } from './mapTypes';

type GlLibrary = Awaited<ReturnType<GlMapLibrary['load']>>;

/**
 * MapLibre GL renderer for rg-map and rg-unit-map (maplibre-mapping-routing-navigation-plan.md §7.1). Renders the
 * department's style from the API (Mapping/GetStyle, signed: Mapbox styles with mapbox:// URLs rewritten, self-hosted
 * vector styles, or a raster style), so it works on hosted and self-hosted installs alike, with no client token.
 */
const mapLibreLibrary: GlMapLibrary = {
  name: 'MapLibre',
  load: async () => {
    await import('maplibre-gl/dist/maplibre-gl.css');
    // maplibre-gl 6 is ESM with named exports (Map, Marker, Popup, ...), mirroring the mapbox-gl API surface the
    // shared view uses.
    const maplibre = await (await import('../../maps/maplibreLoader')).loadMapLibre();
    return maplibre as unknown as GlLibrary;
  },
  styleUrl: (config) => config.mapLibreStyleUrl,
};

export default function MapLibreMapView(props: MapRendererProps) {
  return <GlMapView {...props} library={mapLibreLibrary} />;
}
