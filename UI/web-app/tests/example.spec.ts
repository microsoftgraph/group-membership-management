// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect } from '@playwright/test';

test.use({ storageState: 'tests/storageState.json' });

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || '';

test('Home', async ({ page }) => {
  const url = DOMAIN.startsWith('http') ? DOMAIN : `https://${DOMAIN}`;
  await page.goto(url);
  await page.waitForTimeout(5000);
  await expect(page.locator('text="Membership Management"')).toBeVisible();
  console.log("✅ Home test completed successfully.");
});

test('Admin', async ({ page }) => {
  const url = DOMAIN.startsWith('http') ? DOMAIN : `https://${DOMAIN}`;
  await page.goto(url);
  await page.goto(`${url}/Admin`);
  await page.waitForTimeout(5000);
  await expect(page.locator('text="Admin Center"')).toBeVisible();
  console.log("✅ Admin test completed successfully.");
});
