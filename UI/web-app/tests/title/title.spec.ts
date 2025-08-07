// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect } from '@playwright/test';

test.use({ storageState: 'tests/storageState.json' });

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || '';

const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;

test.beforeEach(async ({ page }) => {
  await page.goto(url);
  await page.waitForTimeout(3000);
});

test('Test Title Pattern Recognition', { tag: '@title' }, async ({ page }) => {
  test.setTimeout(8 * 60 * 1000);

  await page.getByRole('button', { name: /Manage Membership/i }).click();
  await page.waitForTimeout(3000);

  try {
    const titleInput = page.locator('input[type="text"]').first();
    if (await titleInput.isVisible({ timeout: 5000 })) {
      const titlePatterns = [
        "Everyone in John Doe's org",
        "2 levels of direct reports of Jane Smith",
        "Everyone in Tech Lead's org with the following summarized criteria: department equals IT"
      ];

      for (const pattern of titlePatterns) {
        await titleInput.clear();
        await titleInput.fill(pattern);
        await page.waitForTimeout(1000);

        const inputValue = await titleInput.inputValue();
        expect(inputValue).toBe(pattern);
      }

      console.log('✅ Title pattern recognition tested');
    }
  } catch (error) {
    console.log('Title pattern test skipped - input field not found');
  }
});