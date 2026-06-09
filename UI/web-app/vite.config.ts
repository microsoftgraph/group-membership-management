// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { defineConfig } from 'vitest/config';
import { loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const reactAppEnv = Object.fromEntries(
    Object.entries(env).filter(([key]) => key.startsWith('REACT_APP_'))
  );

  return {
    plugins: [react()],
    server: {
      port: 3000,
      strictPort: true,
    },
    preview: {
      port: 3000,
      strictPort: true,
    },
    build: {
      outDir: 'build',
    },
    define: {
      'process.env': JSON.stringify({
        ...reactAppEnv,
        NODE_ENV: mode,
      }),
    },
    test: {
      globals: true,
      environment: 'jsdom',
      setupFiles: ['./src/setupTests.ts'],
      include: ['src/**/*.{test,spec}.{ts,tsx}'],
      exclude: ['tests/**'],
      coverage: {
        provider: 'v8',
        thresholds: {
          branches: 24,
          functions: 28,
          lines: 44,
          statements: 42,
        },
      },
    },
  };
});