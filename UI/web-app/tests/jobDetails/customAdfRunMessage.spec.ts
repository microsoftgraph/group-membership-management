// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || 'http://localhost:3000';

test.beforeEach(async ({ page }) => {
  await setupMockPage(page);
});

test.describe('Custom ADF run messages', () => {
  test.describe.configure({ retries: 2 });

  const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;

  test('shows the custom message and ADF Run ID when an ADF-linked sync row is expanded', async ({ page }) => {
    test.setTimeout(60_000);

    await page.goto(url);

    // Open a job's details.
    const jobRow = page.getByTestId('job-row-mockjob002');
    await expect(jobRow).toBeVisible({ timeout: 15000 });
    await jobRow.click();

    await expect(page.getByText('Membership Details')).toBeVisible({ timeout: 15000 });

    // Open the history panel.
    const historyButton = page.locator('#job-history-button');
    await expect(historyButton).toBeVisible({ timeout: 15000 });
    await historyButton.click();

    const panel = page.locator('.ms-Panel').first();
    await expect(panel).toBeVisible({ timeout: 10000 });

    // Switch to the Sync tab.
    const syncTab = page.getByRole('tab', { name: /sync/i });
    if (await syncTab.isVisible({ timeout: 5000 }).catch(() => false)) {
      await syncTab.click();
      await page.waitForTimeout(1000);
    }

    // Expand each sync row until the ADF-linked one reveals its custom message.
    const expandButtons = panel.getByRole('button', { name: 'Expand row' });
    const buttonCount = await expandButtons.count();
    expect(buttonCount).toBeGreaterThan(0);

    const expectedMessage = 'This run was identified as problematic and is being investigated.';
    const messageLocator = panel.getByText(expectedMessage);

    // Expanding a row replaces its "Expand row" button with a "Collapse row" one,
    // so always click the first remaining "Expand row" button.
    for (let i = 0; i < buttonCount; i++) {
      const remaining = panel.getByRole('button', { name: 'Expand row' });
      if ((await remaining.count()) === 0) {
        break;
      }
      await remaining.first().click();
      await page.waitForTimeout(500);
      if (await messageLocator.isVisible({ timeout: 1000 }).catch(() => false)) {
        break;
      }
    }

    // The custom message should be rendered for the ADF-linked run.
    await expect(messageLocator).toBeVisible({ timeout: 5000 });

    // The mock user is a general settings administrator, so the ADF Run ID is shown.
    await expect(panel.getByText('ADF Run ID:')).toBeVisible({ timeout: 5000 });
    await expect(panel.getByText('32318507-19ee-43b8-92ca-3a69f0ac116b')).toBeVisible({ timeout: 5000 });
  });
});
