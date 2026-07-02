// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect, Page } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';

type AlertConfig = {
  message: string;
  isEnabled: boolean;
  startDate: string;
  endDate: string;
  linkUrl: string | null;
  linkText: string | null;
};

// Register a test-specific override for the alert banner GET. Registered AFTER
// setupMockPage so it takes precedence over the default mock route.
async function overrideAlertBanner(page: Page, config: AlertConfig): Promise<void> {
  await page.route('**/api/v1/settings/alertBanner', async (route) => {
    if (route.request().method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(config),
      });
      return;
    }
    await route.fallback();
  });
}

const iso = (offsetDays: number): string => {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + offsetDays);
  return d.toISOString();
};

test('AlertBanner is visible when enabled and within the date window', async ({ page }) => {
  await setupMockPage(page);
  await overrideAlertBanner(page, {
    message: 'Scheduled maintenance in progress.',
    isEnabled: true,
    startDate: iso(-1),
    endDate: iso(1),
    linkUrl: 'https://status.example.com',
    linkText: 'View details',
  });

  await page.goto('/');
  await page.waitForTimeout(1000);

  const banner = page.getByTestId('alert-banner');
  await expect(banner).toBeVisible();
  await expect(banner).toContainText('Scheduled maintenance in progress.');

  const link = banner.getByRole('link', { name: 'View details' });
  await expect(link).toBeVisible();
  await expect(link).toHaveAttribute('href', 'https://status.example.com');
  await expect(link).toHaveAttribute('target', '_blank');

  console.log('✅ AlertBanner visible with message and https link when enabled + in-window');
});

test('AlertBanner is hidden when disabled', async ({ page }) => {
  await setupMockPage(page);
  await overrideAlertBanner(page, {
    message: 'This should not show.',
    isEnabled: false,
    startDate: iso(-1),
    endDate: iso(1),
    linkUrl: null,
    linkText: null,
  });

  await page.goto('/');
  await page.waitForTimeout(1000);

  await expect(page.getByTestId('alert-banner')).toHaveCount(0);
  console.log('✅ AlertBanner hidden when disabled');
});

test('AlertBanner is hidden when the date window is in the past', async ({ page }) => {
  await setupMockPage(page);
  await overrideAlertBanner(page, {
    message: 'Expired alert.',
    isEnabled: true,
    startDate: iso(-10),
    endDate: iso(-1),
    linkUrl: null,
    linkText: null,
  });

  await page.goto('/');
  await page.waitForTimeout(1000);

  await expect(page.getByTestId('alert-banner')).toHaveCount(0);
  console.log('✅ AlertBanner hidden when out of date window');
});
