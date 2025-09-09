// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { defineConfig, devices } from '@playwright/test';
import dotenv from 'dotenv';
import path from 'path';
import fs from 'fs';

dotenv.config({ path: path.resolve(__dirname, '.env') });

const useStorage = fs.existsSync('tests/storageState.json');
/**
 * See https://playwright.dev/docs/test-configuration.
 */
export default defineConfig({
  testDir: './tests',
  globalSetup: require.resolve('./tests/auth/auth-setup.ts'),
  globalTeardown: require.resolve('./tests/global-teardown.ts'),
  fullyParallel: false,
  /* Fail the build on CI if you accidentally left test.only in the source code. */
  forbidOnly: !!process.env.CI,
  /* Retry on CI only */
  retries: process.env.CI ? 2 : 0,
  /* Opt out of parallel tests on CI. */
  workers: process.env.CI ? 1 : undefined,
  /* Reporter to use. See https://playwright.dev/docs/test-reporters */
  reporter: 'html',
  /* Shared settings for all the projects below. See https://playwright.dev/docs/api/class-testoptions. */
  use: {
    /* Base URL to use in actions like `await page.goto('/')`. */
    baseURL: 'http://localhost:3000',

    /* Collect trace when retrying the failed test. See https://playwright.dev/docs/trace-viewer */
    trace: 'on-first-retry',
    headless: true, // Set headless to false for visual debugging
  },
  /* Configure projects for major browsers */
  projects: [
    // Setup project - runs admin tests first
    {
      name: 'setup',
      testMatch: '**/admin/**/*.spec.ts',
      use: { 
        storageState: useStorage ? 'tests/storageState.json' : undefined,
        ...devices['Desktop Chrome'] 
      },
    },
    // Main tests that depend on setup
    {
      name: 'chromium',
      testIgnore: ['**/admin/**/*.spec.ts', '**/maintenance/**/*.spec.ts'],
      dependencies: ['setup'],
      use: { 
        storageState: useStorage ? 'tests/storageState.json' : undefined,
        ...devices['Desktop Chrome'] 
      },
    },
    // Maintenance project - runs maintenance tests last
    {
      name: 'maintenance',
      testMatch: '**/maintenance/**/*.spec.ts',
      dependencies: ['chromium'], // Runs after main tests complete
      use: { 
        storageState: useStorage ? 'tests/storageState.json' : undefined,
        ...devices['Desktop Chrome'] 
      },
    },
  ],

  /* Run your local dev server before starting the tests */
  webServer: {
    command: 'pnpm start',
    url: 'http://localhost:3000',
    reuseExistingServer: !process.env.CI,
    timeout: 120 * 1000
  },
});
