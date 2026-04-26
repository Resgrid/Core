import { useEffect, useRef } from 'react';
import type { GeoJsonObject } from 'geojson';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import {
  getLayerColor,
  getMarkerIconUrl,
  getPoiMarkerShapePath,
  isPoiMarker,
  type MapMarkerInfo,
  type MapRendererProps,
} from './mapTypes';

interface MarkerState {
  marker: L.Marker;
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

function createMarkerIcon(marker: MapMarkerInfo): L.Icon | L.DivIcon {
  if (isPoiMarker(marker)) {
    const iconClass = typeof marker.ImagePath === 'string' && marker.ImagePath.length > 0
      ? marker.ImagePath
      : 'map-icon-map-pin';
    const color = marker.Color || '#2563eb';

    return L.divIcon({
      className: 'rg-map__poi-marker-wrapper',
      html: `<div class="rg-map__poi-marker" style="--rg-map-poi-color:${color};">
        <svg viewBox="-24 -48 48 48" class="rg-map__poi-marker-shape" aria-hidden="true">
          <path d="${getPoiMarkerShapePath(marker.Marker)}"></path>
        </svg>
        <span class="map-icon ${iconClass} rg-map__poi-marker-icon" aria-hidden="true"></span>
      </div>`,
      iconSize: [36, 48],
      iconAnchor: [18, 48],
      popupAnchor: [0, -42],
      tooltipAnchor: [0, 20],
    });
  }

  return L.icon({
    iconUrl: getMarkerIconUrl(marker),
    iconSize: [32, 37],
    iconAnchor: [16, 37],
  });
}

function createMarker(markerInfo: MapMarkerInfo, hideLabels: boolean): L.Marker {
  const marker = L.marker([markerInfo.Latitude, markerInfo.Longitude], {
    icon: createMarkerIcon(markerInfo),
    draggable: false,
    title: hideLabels ? '' : markerInfo.Title,
  });

  if (!hideLabels && markerInfo.Title) {
    marker.bindTooltip(markerInfo.Title, {
      permanent: true,
      direction: 'bottom',
      className: 'rg-map__tooltip',
    });
  }

  if (markerInfo.InfoWindowContent) {
    marker.bindPopup(markerInfo.InfoWindowContent);
  }

  return marker;
}

function fitMapToMarkers(
  map: L.Map,
  markers: Map<string, MarkerState>,
  mapData: MapRendererProps['mapData'],
) {
  if (!mapData) {
    return;
  }

  const visiblePositions = Array.from(markers.values()).map(({ latitude, longitude }) => [
    latitude,
    longitude,
  ] as L.LatLngTuple);

  if (visiblePositions.length > 0) {
    const bounds = L.latLngBounds(visiblePositions);

    if (bounds.isValid()) {
      map.fitBounds(bounds, {
        padding: [24, 24],
      });
      return;
    }
  }

  map.setView([mapData.CenterLat, mapData.CenterLon], mapData.ZoomLevel);
}

export default function LeafletMapView({
  mapData,
  markers,
  layers,
  layerVisibility,
  hideLabels,
  resolvedMapConfig,
  fitBoundsKey,
}: MapRendererProps) {
  const mapContainerRef = useRef<HTMLDivElement | null>(null);
  const mapRef = useRef<L.Map | null>(null);
  const markerRefs = useRef<Map<string, MarkerState>>(new Map());
  const layerRefs = useRef<Map<string, L.GeoJSON>>(new Map());

  useEffect(() => {
    if (!mapContainerRef.current || resolvedMapConfig.tileUrl.length === 0) {
      return;
    }

    const map = L.map(mapContainerRef.current, {
      doubleClickZoom: false,
      zoomControl: true,
    });

    L.tileLayer(resolvedMapConfig.tileUrl, {
      maxZoom: 19,
      attribution: resolvedMapConfig.attribution,
    }).addTo(map);

    mapRef.current = map;

    const resizeMap = () => map.invalidateSize();
    window.addEventListener('resize', resizeMap);
    requestAnimationFrame(() => map.invalidateSize());

    return () => {
      window.removeEventListener('resize', resizeMap);
      markerRefs.current.forEach((markerState) => markerState.marker.remove());
      markerRefs.current.clear();
      layerRefs.current.forEach((layer) => layer.remove());
      layerRefs.current.clear();
      map.remove();
      mapRef.current = null;
    };
  }, [resolvedMapConfig.attribution, resolvedMapConfig.tileUrl]);

  useEffect(() => {
    const map = mapRef.current;

    if (!map) {
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

        const nextMarker = createMarker(markerInfo, hideLabels);
        nextMarker.addTo(map);

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
        existingMarkerState.marker.setLatLng([markerInfo.Latitude, markerInfo.Longitude]);
        existingMarkerState.latitude = markerInfo.Latitude;
        existingMarkerState.longitude = markerInfo.Longitude;
      }
    }
  }, [hideLabels, markers]);

  useEffect(() => {
    if (!mapRef.current) {
      return;
    }

    fitMapToMarkers(mapRef.current, markerRefs.current, mapData);
  }, [fitBoundsKey, mapData]);

  useEffect(() => {
    const map = mapRef.current;

    if (!map) {
      return;
    }

    layerRefs.current.forEach((layer) => layer.remove());
    layerRefs.current.clear();

    for (const layer of layers) {
      const color = getLayerColor(layer);
      const geoJsonLayer = L.geoJSON(layer.featureCollection as GeoJsonObject, {
        style: () => ({
          color,
          opacity: 0.7,
          fillColor: color,
          fillOpacity: 0.1,
          weight: 2,
        }),
        pointToLayer: (_, latLng) =>
          L.circleMarker(latLng, {
            color,
            fillColor: color,
            fillOpacity: 0.45,
            radius: 6,
            weight: 2,
          }),
        onEachFeature: (_, mapLayer) => {
          const pathLayer = mapLayer as L.Path;

          mapLayer.on('mouseover', () => {
            pathLayer.setStyle({
              fillOpacity: 0.35,
            });
          });

          mapLayer.on('mouseout', () => {
            pathLayer.setStyle({
              fillOpacity: 0.1,
            });
          });
        },
      });

      if (layerVisibility[layer.id]) {
        geoJsonLayer.addTo(map);
      }

      layerRefs.current.set(layer.id, geoJsonLayer);
    }
  }, [layerVisibility, layers]);

  return <div ref={mapContainerRef} className="rg-map__canvas" />;
}
