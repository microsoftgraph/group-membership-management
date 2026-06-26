// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect, Page } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || 'http://localhost:3000';
const testTimeoutMs = Number(process.env.PLAYWRIGHT_TEST_TIMEOUT_MS ?? 60000);

/**
 * Types into the NormalPeoplePicker naturally to trigger the component's
 * onInputChange handler, which fetches suggestions from Graph.
 */
async function typeIntoPickerNaturally(page: Page, pickerInput: ReturnType<typeof page.locator>, text: string) {
  await pickerInput.click();
  await pickerInput.pressSequentially(text, { delay: 50 });
  // Wait for the resolveDelay (600ms) + Graph call + state update
  await page.waitForTimeout(2000);
}

test.beforeEach(async ({ page }) => {
  await setupMockPage(page);
});

async function openSyncHistoryTab(page: Page): Promise<ReturnType<typeof page.locator>> {
  const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
  await page.goto(url);

  const jobRow = page.getByTestId('job-row-mockjob002');
  await expect(jobRow).toBeVisible({ timeout: 15000 });
  await jobRow.click();

  await expect(page.getByText('Membership Details - Engineering-All')).toBeVisible({ timeout: 15000 });

  const historyButton = page.locator('#job-history-button');
  await expect(historyButton).toBeVisible({ timeout: 15000 });
  await historyButton.click();

  const historyPanel = page.locator('.ms-Panel').first();
  await expect(historyPanel).toBeVisible({ timeout: 10000 });

  const syncTab = page.getByRole('tab', { name: 'Sync' });
  await expect(syncTab).toBeVisible({ timeout: 10000 });
  await syncTab.click();

  const pickerInput = historyPanel.locator('.ms-BasePicker-input').first();
  await expect(pickerInput).toBeVisible({ timeout: 10000 });
  return pickerInput;
}

async function searchForMockUser(page: Page) {
  const pickerInput = await openSyncHistoryTab(page);

  // Type to trigger onInputChange -> Graph call -> suggestions
  await typeIntoPickerNaturally(page, pickerInput, 'Mock User');

  // Wait for the suggestion item to appear
  const suggestion = page.locator('.ms-Suggestions-itemButton').filter({ hasText: 'Mock User' }).first();
  await expect(suggestion).toBeVisible({ timeout: 10000 });

  // Set up response listener BEFORE clicking the suggestion
  const searchResponsePromise = page.waitForResponse(
    (resp) => resp.url().includes('/search-user/') && resp.request().method() === 'GET',
  );

  // Click the suggestion to select the user (triggers onChange -> searchHistoryForUser)
  await suggestion.click();

  // Wait for the search-user API call to complete
  await searchResponsePromise;
  await page.waitForTimeout(1000);
}

async function overrideSearchUserResponse(page: Page, payload: Record<string, unknown>) {
  await page.route(/\/api\/v1\/jobDetails\/history\/sync\/[^/]+\/search-user\/[^/?]+/, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(payload),
    });
  });
}

test.describe('Job History search user', () => {
  test('banner shows user is in group', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await searchForMockUser(page);

    await expect(page.getByText('This user is currently part of the membership.')).toBeVisible({ timeout: 5000 });
    await expect(page.getByText('Note:')).toBeVisible();
    await expect(page.getByText('Sync history is only retained for 30 days.')).toBeVisible();
  });

  test('banner shows user is NOT in group', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await overrideSearchUserResponse(page, {
      matchingRunIds: [],
      runMembershipChanges: [],
      userInCurrentGroup: false,
      checkedCurrentGroupMembership: true,
    });

    await searchForMockUser(page);

    await expect(page.getByText('This user is not currently part of the membership.')).toBeVisible({ timeout: 5000 });
  });

  test('manual note when user was manually added', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await overrideSearchUserResponse(page, {
      matchingRunIds: ['run-001'],
      runMembershipChanges: [{ runId: 'run-001', membershipChangeType: 'Removed' }],
      userInCurrentGroup: true,
      checkedCurrentGroupMembership: true,
    });

    await searchForMockUser(page);

    await expect(page.getByText('This user is currently part of the membership.')).toBeVisible({ timeout: 5000 });
    await expect(page.getByText('Someone must have manually added this user.')).toBeVisible();
  });

  test('manual note when user was manually removed', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await overrideSearchUserResponse(page, {
      matchingRunIds: ['run-001'],
      runMembershipChanges: [{ runId: 'run-001', membershipChangeType: 'Added' }],
      userInCurrentGroup: false,
      checkedCurrentGroupMembership: true,
    });

    await searchForMockUser(page);

    await expect(page.getByText('This user is not currently part of the membership.')).toBeVisible({ timeout: 5000 });
    await expect(page.getByText('Someone must have manually removed this user.')).toBeVisible();
  });

  test('highlights Added cell in matching run', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await searchForMockUser(page);

    await expect(page.getByLabel('2 (user was added in this sync)')).toBeVisible({ timeout: 5000 });
  });
});
