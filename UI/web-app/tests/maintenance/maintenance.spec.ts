// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect, Page } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || 'http://localhost:3000';
const isMockMode = process.env.PLAYWRIGHT_USE_MOCK_API !== 'false';

test.beforeEach(async ({ page }) => {
  await setupMockPage(page);
});

async function clickFirstVisible(page: Page, selectors: Array<{ role: 'button' | 'tab'; name: string | RegExp }>) {
  for (const selector of selectors) {
    const locator = page.getByRole(selector.role, { name: selector.name });
    if (await locator.count()) {
      await locator.first().click();
      return;
    }
  }
  throw new Error(`Could not find any selector: ${selectors.map((s) => `${s.role}:${String(s.name)}`).join(', ')}`);
}

test('Maintenance - Reset GMM (WARNING: Disables API)', { tag: '@maintenance' }, async ({ page }) => {
  test.setTimeout(isMockMode ? 2 * 60 * 1000 : 10 * 60 * 1000);
  
  console.log('🚨 WARNING: Starting maintenance reset - this will disable the API for other tests');
  console.log('🚨 This test should run LAST to avoid interfering with other tests');

  const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
  await page.goto(url);
  await page.waitForTimeout(5000);

  await expect(page.locator('#manage-membership-button')).toBeVisible();
  await clickFirstVisible(page, [{ role: 'button', name: 'Settings' }]);
  await clickFirstVisible(page, [{ role: 'tab', name: /General/i }]);
  await clickFirstVisible(page, [{ role: 'tab', name: /Operations/i }]);
  await page.getByRole('button', { name: 'Reset GMM' }).click();
  await page.getByRole('button', { name: 'Back' }).click();
  await expect(page.getByText('This application is currently')).toBeVisible();
  await clickFirstVisible(page, [{ role: 'button', name: 'Settings' }]);
  await expect(page.locator('div').filter({ hasText: /^Admin Center$/ }).first()).toBeVisible();
  await clickFirstVisible(page, [{ role: 'tab', name: /Operations/i }]);

  let attempts = 0;
  const maxAttempts = isMockMode ? 3 : 10;
  const refreshInterval = isMockMode ? 2000 : 60000;
  let isButtonEnabled = false;
  
  while (!isButtonEnabled && attempts < maxAttempts) {    
    try {
      isButtonEnabled = await page.getByRole('button', { name: 'Reset GMM' }).isEnabled({ timeout: isMockMode ? 1000 : 2000 });
    } catch (error) {
      // If the button is not found or not enabled within the timeout, continue with the loop
      isButtonEnabled = false;
    }

    console.log(`Button enabled status: ${isButtonEnabled}`);
    
    if (isButtonEnabled) {
      console.log(`Button enabled after ${attempts + 1} attempts`);
      break;
    }
    
    attempts++;
    console.log(`Attempt ${attempts}/${maxAttempts}: Button not enabled, waiting ${refreshInterval} ms...`);
    
    if (attempts < maxAttempts) {
      await page.waitForTimeout(refreshInterval);
      await page.reload();
      
      // Navigate back to the operations tab after refresh
      await clickFirstVisible(page, [{ role: 'button', name: 'Settings' }]);
      await clickFirstVisible(page, [{ role: 'tab', name: /Operations/i }]);
      await page.waitForTimeout(5000);
    }
  }

  await page.goto(url);
  await page.waitForTimeout(5000);

  await expect(page.locator('#manage-membership-button')).toBeVisible();
  
  console.log('✅ Maintenance reset completed - API should be restored');
  console.log('🔄 All tests in this session should be complete');
});