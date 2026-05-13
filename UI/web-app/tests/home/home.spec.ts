// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';

test.beforeEach(async ({ page }) => {
  await setupMockPage(page);
  await page.goto('/');
  await page.waitForTimeout(1000);
});

test('Home', { tag: '@main' }, async ({ page }) => {
  await expect(page.locator('text="Membership Management"')).toBeVisible();
  console.log('✅ Home test completed successfully.');
});

test('Download button is visible', { tag: '@main' }, async ({ page }) => {
  const downloadButton = page.getByRole('button', { name: 'Download' });
  await expect(downloadButton).toBeVisible();
  console.log('✅ Download button is visible');
});

test('Bulk Approve menu item is visible for submission reviewers', async ({ page }) => {
  // Click the split button's chevron to open the dropdown menu
  await page.locator('#manage-membership-button').locator('..').locator('button[aria-haspopup="true"]').click();

  const bulkApproveItem = page.getByRole('menuitem', { name: /Bulk Approve/i });
  await expect(bulkApproveItem).toBeVisible();

  const checkMarkIcon = bulkApproveItem.locator('i[data-icon-name="CheckMark"]');
  await expect(checkMarkIcon).toBeVisible();
  await expect(bulkApproveItem).toHaveText(/Bulk Approve/i);

  console.log('✅ Bulk Approve menu item is visible with CheckMark icon for submission reviewers');
});

test('Row click navigates to JobDetails if targetGroupName is null and status is DestinationGroupNotFound', async ({ page }) => {
  const row = page.locator('[data-testid="job-row-mockjob001"]');
  await expect(row).toBeVisible();
  await row.click();

  await expect(page).toHaveURL(/\/JobDetails\/\w+/);
  console.log('✅ Row click navigates to JobDetails as expected');
});

test('Last Modified column is visible in the jobs list', async ({ page }) => {
  const lastModifiedHeader = page.getByRole('columnheader', { name: /last modified/i });
  await expect(lastModifiedHeader).toBeVisible();

  console.log('✅ Last Modified column is visible in the jobs list');
});
