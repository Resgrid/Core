/// <reference types="vite/client" />

/** URL of the maplibre-gl web worker the build emits (vite.config.ts, maplibreWorker plugin). */
declare module 'virtual:maplibre-worker-url' {
  const url: string;
  export default url;
}
