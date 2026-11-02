import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

/**
 * maplibre-gl 6 starts its web worker from maplibre-gl-worker.mjs beside its own module, a file this library build never
 * writes. Emit the worker as an asset and expose its URL as a virtual module: Rollup turns the reference into a URL
 * relative to whichever chunk imports it, and src/maps/maplibreLoader.ts hands it to maplibre.setWorkerUrl.
 */
function maplibreWorker() {
  const id = 'virtual:maplibre-worker-url';
  const resolvedId = '\0' + id;

  return {
    name: 'resgrid-maplibre-worker',
    resolveId: (source: string) => (source === id ? resolvedId : null),
    load(this: { emitFile: (file: { type: 'asset'; name: string; source: Uint8Array }) => string }, source: string) {
      if (source !== resolvedId) {
        return null;
      }

      const reference = this.emitFile({
        type: 'asset',
        name: 'maplibre-gl-worker.js',
        source: readFileSync(resolve(__dirname, 'node_modules/maplibre-gl/dist/maplibre-gl-worker.mjs')),
      });
      return `export default import.meta.ROLLUP_FILE_URL_${reference};`;
    },
  };
}

export default defineConfig(({ mode }) => {
  const nodeEnv = mode === 'production' ? 'production' : 'development';

  return {
    plugins: [react(), maplibreWorker()],
    define: {
      'process.env.NODE_ENV': JSON.stringify(nodeEnv),
      'process.platform': JSON.stringify('browser'),
      'process.release': JSON.stringify({ name: 'browser' }),
      'process.versions.node': JSON.stringify(''),
    },
    build: {
      target: 'es2022',
      outDir: 'dist/core',
      emptyOutDir: true,
      minify: false,
      sourcemap: mode !== 'production',
      cssCodeSplit: false,
      modulePreload: {
        polyfill: false,
      },
      lib: {
        entry: resolve(__dirname, 'src/elements.ts'),
        formats: ['es'],
        fileName: () => 'react-elements.js',
      },
      rollupOptions: {
        output: {
          entryFileNames: 'react-elements.js',
          chunkFileNames: 'chunks/[name]-[hash].min.js',
          assetFileNames: (assetInfo) => {
            if (assetInfo.name?.endsWith('.css')) {
              return 'react-elements.css';
            }

            return 'assets/[name]-[hash][extname]';
          },
        },
      },
    },
  };
});
