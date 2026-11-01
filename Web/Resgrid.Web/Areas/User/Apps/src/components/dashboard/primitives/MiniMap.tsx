import { lazy, Suspense } from 'react';
import type { MiniMapProps } from './MiniMapView';

export type { MiniMapGeoJsonLayer, MiniMapMarker, MiniMapProps } from './MiniMapView';

// Leaflet loads only when a widget actually renders a map, so widgets that import other primitives from the index do
// not pull the map library into their chunk.
const MiniMapView = lazy(() => import('./MiniMapView'));

export function MiniMap(props: MiniMapProps) {
  return (
    <Suspense fallback={<div className="rg-minimap rg-minimap-loading" role="region" aria-label={props.ariaLabel} aria-busy="true" />}>
      <MiniMapView {...props} />
    </Suspense>
  );
}
