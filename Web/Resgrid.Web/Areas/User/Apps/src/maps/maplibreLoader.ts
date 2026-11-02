import workerUrl from 'virtual:maplibre-worker-url';

type MapLibreModule = typeof import('maplibre-gl');

let loading: Promise<MapLibreModule> | null = null;

/**
 * maplibre-gl 6, loaded once with its web worker pointed at the copy the build emits (vite.config.ts). The library
 * otherwise starts the worker from maplibre-gl-worker.mjs beside its own module, a file a library build never
 * writes, and every MapLibre map then fails with "Worker failed to load" and falls back to the raster map. Every
 * MapLibre map loads the library through here so the URL is set before the first map is created.
 */
export function loadMapLibre(): Promise<MapLibreModule> {
  loading ??= import('maplibre-gl').then((maplibre) => {
    maplibre.setWorkerUrl(workerUrl);
    return maplibre;
  });
  return loading;
}
