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

test('Destination search debounces rapid typing into a single jobs request', async ({ page }) => {
  // Count GET /api/v1/jobs requests fired AFTER the initial page load settles.
  const jobsRequests: string[] = [];
  let tracking = false;
  page.on('request', (req) => {
    if (!tracking) return;
    const url = req.url();
    if (req.method() === 'GET' && /\/api\/v1\/jobs(\?|$)/.test(url)) {
      jobsRequests.push(url);
    }
  });

  // Let the page settle so the initial jobs fetch doesn't pollute the count.
  await page.waitForLoadState('networkidle');
  tracking = true;

  const searchBox = page.getByPlaceholder('Search by Name, Email, or Object ID');
  await expect(searchBox).toBeVisible();

  // Type "LoadTesting" character-by-character with no delay. Without the debounce
  // fix this fires one GET /jobs per keystroke (11). With the 350ms debounce
  // only the final value should produce a single GET /jobs.
  await searchBox.pressSequentially('LoadTesting', { delay: 20 });

  // Wait past the 350ms debounce window for the trailing-edge fetch to fire.
  await page.waitForTimeout(1500);

  // The fix means at most 1 jobs request (the debounced trailing-edge fetch).
  // Without the fix we'd see ~11 (one per keystroke).
  expect(jobsRequests.length, `expected <=2 jobs fetches, got ${jobsRequests.length}: ${jobsRequests.join('\n')}`).toBeLessThanOrEqual(2);

  console.log(`✅ Debounced destination search fired ${jobsRequests.length} jobs request(s) for 11 keystrokes`);
});
