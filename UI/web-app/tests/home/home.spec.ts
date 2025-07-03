// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect } from '@playwright/test';

test.use({ storageState: 'tests/storageState.json' });

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || '';

test('Home', { tag: '@main' }, async ({ page }) => {
  const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
  await page.goto(url);
  await page.waitForTimeout(5000);
  await expect(page.locator('text="Membership Management"')).toBeVisible();
  console.log('✅ Home test completed successfully.');
});

test('Download button is visible', { tag: '@main' }, async ({ page }) => {
  const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
  await page.goto(url);
  await page.waitForTimeout(5000);
  const downloadButton = page.getByRole('button', { name: 'Download' });
  await expect(downloadButton).toBeVisible();
  console.log('✅ Download button is visible');
});

test('Bulk Approve menu item is visible for submission reviewers', async ({ page }) => {
  const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
  await page.goto(url);
  await page.waitForTimeout(5000);

  await page.getByRole('button', { name: /Manage Membership/i }).click();

  const bulkApproveItem = page.getByRole('menuitem', { name: /Bulk Approve/i });
  await expect(bulkApproveItem).toBeVisible();

  const checkMarkIcon = bulkApproveItem.locator('i[data-icon-name="CheckMark"]');
  await expect(checkMarkIcon).toBeVisible();
  await expect(bulkApproveItem).toHaveText(/Bulk Approve/i);

  console.log('✅ Bulk Approve menu item is visible with CheckMark icon for submission reviewers');
});

test('Row click navigates to JobDetails if targetGroupName is null and status is DestinationGroupNotFound', async ({ page }) => {
  const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
  await page.goto(url);
  await page.waitForTimeout(5000);

  const row = page.locator('.ms-DetailsRow').first();
  await row.click();

  await expect(page).toHaveURL(/\/JobDetails\/\w+/);
  console.log('✅ Row click navigates to JobDetails as expected');
});