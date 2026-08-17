import { useEffect, useRef, useState } from 'react';
import 'mapbox-gl/dist/mapbox-gl.css';
import {
  getLayerColor,
  getMarkerIconUrl,
  getPoiMarkerShapePath,
  isPoiMarker,
  type MapMarkerInfo,
  type MapRendererProps,
} from './mapTypes';

type MapboxGl = typeof import('mapbox-gl')['default'];
type MapboxMap = import('mapbox-gl').Map;
type MapboxMarker = import('mapbox-gl').Marker;

interface MarkerState {
  marker: MapboxMarker;
  latitude: number;
  longitude: number;
  title: string;
  imagePath: string;
  markerShape: string;
  color: string;
  markerType: number;
  infoWindowContent: string;
  hideLabels: boolean;
}

function createMarkerElement(markerInfo: MapMarkerInfo, hideLabels: boolean): HTMLDivElement {
  const wrapper = document.createElement('div');
  wrapper.className = 'rg-map__marker';
  wrapper.title = hideLabels ? '' : markerInfo.Title;

  if (isPoiMarker(markerInfo)) {
    wrapper.classList.add('rg-map__marker--poi');
    wrapper.style.setProperty('--rg-map-poi-color', markerInfo.Color || '#2563eb');

    const markerShape = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    markerShape.setAttribute('viewBox', '-24 -48 48 48');
    markerShape.setAttribute('class', 'rg-map__poi-marker-shape');

    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('d', getPoiMarkerShapePath(markerInfo.Marker));
    markerShape.appendChild(path);

    const icon = document.createElement('span');
    icon.className = `map-icon ${markerInfo.ImagePath || 'map-icon-map-pin'} rg-map__poi-marker-icon`;
    icon.setAttribute('aria-hidden', 'true');

    wrapper.appendChild(markerShape);
    wrapper.appendChild(icon);
  } else {
    const icon = document.createElement('img');
    icon.className = 'rg-map__marker-icon';
    icon.src = getMarkerIconUrl(markerInfo);
    icon.alt = '';
    wrapper.appendChild(icon);
  }

  if (!hideLabels && markerInfo.Title) {
    const label = document.createElement('div');
    label.className = 'rg-map__marker-label';
    label.textContent = markerInfo.Title;
    wrapper.appendChild(label);
  }

  return wrapper;
}

function fitMapToMarkers(
  map: MapboxMap,
  mapboxgl: MapboxGl,
  markers: Map<string, MarkerState>,
  mapData: MapRendererProps['mapData'],
) {
  if (!mapData) {
    return;
  }

  if (markers.size > 0) {
    const bounds = new mapboxgl.LngLatBounds();

    markers.forEach(({ latitude, longitude }) => bounds.extend([longitude, latitude]));

    if (!bounds.isEmpty()) {
      map.fitBounds(bounds, {
        padding: 24,
        maxZoom: 16,
      });
      return;
    }
  }

  map.easeTo({
    center: [mapData.CenterLon, mapData.CenterLat],
    zoom: mapData.ZoomLevel,
    duration: 0,
  });
}

function clearLayerArtifacts(map: MapboxMap, layerIds: string[], sourceIds: string[]) {
  for (const layerId of layerIds) {
    if (map.getLayer(layerId)) {
      map.removeLayer(layerId);
    }
  }

  for (const sourceId of sourceIds) {
    if (map.getSource(sourceId)) {
      map.removeSource(sourceId);
    }
  }
}

function addGeoJsonLayer(map: MapboxMap, layerId: string, layer: MapRendererProps['layers'][number]) {
  const sourceId = `rg-layer-source-${layerId}`;
  const fillLayerId = `rg-layer-fill-${layerId}`;
  const lineLayerId = `rg-layer-line-${layerId}`;
  const pointLayerId = `rg-layer-point-${layerId}`;
  const color = getLayerColor(layer);

  map.addSource(sourceId, {
    type: 'geojson',
    data: layer.featureCollection,
  });

  map.addLayer({
    id: fillLayerId,
    type: 'fill',
    source: sourceId,
    filter: ['==', '$type', 'Polygon'],
    paint: {
      'fill-color': color,
      'fill-opacity': 0.12,
    },
  });

  map.addLayer({
    id: lineLayerId,
    type: 'line',
    source: sourceId,
    paint: {
      'line-color': color,
      'line-width': 2,
      'line-opacity': 0.8,
    },
  });

  map.addLayer({
    id: pointLayerId,
    type: 'circle',
    source: sourceId,
    filter: ['==', '$type', 'Point'],
    paint: {
      'circle-radius': 6,
      'circle-color': color,
      'circle-opacity': 0.45,
      'circle-stroke-color': color,
      'circle-stroke-width': 2,
    },
  });

  return {
    sourceId,
    layerIds: [fillLayerId, lineLayerId, pointLayerId],
  };
}

export default function MapboxMapView({
  mapData,
  markers,
  layers,
  layerVisibility,
  hideLabels,
  resolvedMapConfig,
  fitBoundsKey,
}: MapRendererProps) {
  const mapContainerRef = useRef<HTMLDivElement | null>(null);
  const mapRef = useRef<MapboxMap | null>(null);
  const mapboxRef = useRef<MapboxGl | null>(null);
  const markerRefs = useRef<Map<string, MarkerState>>(new Map());
  const layerIdsRef = useRef<string[]>([]);
  const sourceIdsRef = useRef<string[]>([]);
  const [styleReady, setStyleReady] = useState(false);
  const [initError, setInitError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    setInitError(null);

    const initializeMapAsync = async () => {
      if (!mapContainerRef.current) {
        return;
      }

      const mapboxgl = (await import('mapbox-gl')).default;

      if (cancelled) {
        return;
      }

      mapboxgl.accessToken = resolvedMapConfig.accessToken;
      mapboxRef.current = mapboxgl;

      const map = new mapboxgl.Map({
        container: mapContainerRef.current,
        style: resolvedMapConfig.styleUrl,
        center: [mapData?.CenterLon ?? -98.5795, mapData?.CenterLat ?? 39.8283],
        zoom: mapData?.ZoomLevel ?? 4,
        doubleClickZoom: false,
        customAttribution: resolvedMapConfig.attribution || undefined,
      });

      map.addControl(new mapboxgl.NavigationControl(), 'top-left');
      mapRef.current = map;

      let styleLoaded = false;

      // mapbox-gl reports an unreachable style URL or a rejected access token on this event rather
      // than throwing, so without a listener a misconfigured department just gets a blank canvas.
      // Only pre-load failures are fatal: once the style is up, transient tile errors must not
      // replace a working map with an error overlay.
      const handleMapError = (event: { error?: { message?: string } }) => {
        console.error('Mapbox map error', event?.error ?? event);

        if (cancelled || styleLoaded) {
          return;
        }

        const message = event?.error?.message?.trim();
        setInitError(message && message.length > 0 ? message : 'Unable to load the map.');
      };

      map.on('error', handleMapError);

      const handleStyleReady = () => {
        styleLoaded = true;
        setStyleReady(true);
        map.resize();
      };

      map.on('load', handleStyleReady);
      map.on('style.load', handleStyleReady);

      const resizeMap = () => map.resize();
      window.addEventListener('resize', resizeMap);
      requestAnimationFrame(() => map.resize());

      if (cancelled) {
        window.removeEventListener('resize', resizeMap);
        map.off('error', handleMapError);
        map.remove();
        mapRef.current = null;
        return;
      }

      return () => {
        window.removeEventListener('resize', resizeMap);
        map.off('error', handleMapError);
        clearLayerArtifacts(map, layerIdsRef.current, sourceIdsRef.current);
        layerIdsRef.current = [];
        sourceIdsRef.current = [];
        markerRefs.current.forEach((markerState) => markerState.marker.remove());
        markerRefs.current.clear();
        map.remove();
        mapRef.current = null;
      };
    };

    let cleanupMap: (() => void) | undefined;

    void initializeMapAsync()
      .then((cleanup) => {
        cleanupMap = cleanup;
      })
      .catch((mapInitError: unknown) => {
        // mapbox-gl is the largest chunk in the bundle, so its dynamic import is the one most
        // likely to be in flight when the user navigates away or when a deploy rotates the
        // hashed chunk names. Without this catch the rejection escapes as an unhandled promise
        // rejection with no stack and no URL instead of a visible map error.
        console.error('Failed to initialize the Mapbox map', mapInitError);

        if (!cancelled) {
          setInitError(
            mapInitError instanceof Error && mapInitError.message.trim().length > 0
              ? mapInitError.message
              : 'Unable to load the map.',
          );
        }
      });

    return () => {
      cancelled = true;
      setStyleReady(false);
      cleanupMap?.();
    };
  }, [
    mapData?.CenterLat,
    mapData?.CenterLon,
    mapData?.ZoomLevel,
    resolvedMapConfig.accessToken,
    resolvedMapConfig.attribution,
    resolvedMapConfig.styleUrl,
  ]);

  useEffect(() => {
    const map = mapRef.current;
    const mapboxgl = mapboxRef.current;

    if (!map || !mapboxgl || !styleReady) {
      return;
    }

    const activeMarkerIds = new Set(markers.map((marker) => marker.Id));

    markerRefs.current.forEach((markerState, markerId) => {
      if (!activeMarkerIds.has(markerId)) {
        markerState.marker.remove();
        markerRefs.current.delete(markerId);
      }
    });

    for (const markerInfo of markers) {
      const existingMarkerState = markerRefs.current.get(markerInfo.Id);
      const appearanceChanged =
        !existingMarkerState ||
          existingMarkerState.title !== markerInfo.Title ||
          existingMarkerState.imagePath !== markerInfo.ImagePath ||
          existingMarkerState.markerShape !== (markerInfo.Marker ?? '') ||
          existingMarkerState.color !== (markerInfo.Color ?? '') ||
          existingMarkerState.markerType !== markerInfo.Type ||
          existingMarkerState.infoWindowContent !== markerInfo.InfoWindowContent ||
          existingMarkerState.hideLabels !== hideLabels;

      if (appearanceChanged) {
        existingMarkerState?.marker.remove();

        const nextMarker = new mapboxgl.Marker({
          element: createMarkerElement(markerInfo, hideLabels),
          anchor: 'bottom',
        })
          .setLngLat([markerInfo.Longitude, markerInfo.Latitude])
          .addTo(map);

        if (markerInfo.InfoWindowContent) {
          nextMarker.setPopup(new mapboxgl.Popup({ offset: 20 }).setHTML(markerInfo.InfoWindowContent));
        }

        markerRefs.current.set(markerInfo.Id, {
          marker: nextMarker,
          latitude: markerInfo.Latitude,
          longitude: markerInfo.Longitude,
          title: markerInfo.Title,
          imagePath: markerInfo.ImagePath,
          markerShape: markerInfo.Marker ?? '',
          color: markerInfo.Color ?? '',
          markerType: markerInfo.Type,
          infoWindowContent: markerInfo.InfoWindowContent,
          hideLabels,
        });

        continue;
      }

      if (
        existingMarkerState.latitude !== markerInfo.Latitude ||
        existingMarkerState.longitude !== markerInfo.Longitude
      ) {
        existingMarkerState.marker.setLngLat([markerInfo.Longitude, markerInfo.Latitude]);
        existingMarkerState.latitude = markerInfo.Latitude;
        existingMarkerState.longitude = markerInfo.Longitude;
      }
    }
  }, [hideLabels, markers, styleReady]);

  useEffect(() => {
    const map = mapRef.current;
    const mapboxgl = mapboxRef.current;

    if (!map || !mapboxgl || !styleReady) {
      return;
    }

    fitMapToMarkers(map, mapboxgl, markerRefs.current, mapData);
  }, [fitBoundsKey, mapData, styleReady]);

  useEffect(() => {
    const map = mapRef.current;

    if (!map || !styleReady) {
      return;
    }

    clearLayerArtifacts(map, layerIdsRef.current, sourceIdsRef.current);
    layerIdsRef.current = [];
    sourceIdsRef.current = [];

    for (const layer of layers) {
      if (!layerVisibility[layer.id]) {
        continue;
      }

      const layerArtifacts = addGeoJsonLayer(map, layer.id, layer);
      sourceIdsRef.current.push(layerArtifacts.sourceId);
      layerIdsRef.current.push(...layerArtifacts.layerIds);
    }
  }, [layerVisibility, layers, styleReady]);

  // The container has to stay mounted through a failure. Swapping it out for the error nulls
  // mapContainerRef, and the retry effect bails on its !mapContainerRef.current guard before
  // React has re-rendered the cleared error, leaving a blank map that never recovers.
  return (
    <>
      <div ref={mapContainerRef} className="rg-map__canvas" />
      {initError && (
        <div className="rg-map__overlay rg-map__overlay--message">
          <div className="rg-error">{initError}</div>
        </div>
      )}
    </>
  );
}
