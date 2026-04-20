import { resolve } from 'node:path';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
  const nodeEnv = mode === 'production' ? 'production' : 'development';

  return {
    plugins: [react()],
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
          chunkFileNames: 'chunks/[name]-[hash].js',
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
