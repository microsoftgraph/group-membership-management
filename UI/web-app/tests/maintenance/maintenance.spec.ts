// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect } from '@playwright/test';

test.use({ storageState: 'tests/storageState.json' });

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || '';

test('Maintenance - Reset GMM (WARNING: Disables API)', { tag: '@maintenance' }, async ({ page }) => {
  test.setTimeout(10 * 60 * 1000); // Increased test timeout to 10 minutes
  
  console.log('🚨 WARNING: Starting maintenance reset - this will disable the API for other tests');
  console.log('🚨 This test should run LAST to avoid interfering with other tests');

  const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
  await page.goto(url);
  await page.waitForTimeout(5000);

  await expect(page.locator('#manage-membership-button')).toBeVisible();
  await page.getByRole('button', { name: 'Settings' }).click();
  await page.getByRole('tab', { name: 'General General' }).click();
  await page.getByRole('tab', { name: 'Operations Operations' }).click();
  await page.getByRole('button', { name: 'Reset GMM' }).click();
  await page.getByRole('button', { name: 'Back' }).click();
  await expect(page.getByText('This application is currently')).toBeVisible();
  await page.getByRole('button', { name: 'Settings' }).click();
  await expect(page.locator('div').filter({ hasText: /^Admin Center$/ }).first()).toBeVisible();  await page.getByRole('tab', { name: 'Operations Operations' }).click();  // wait for the reset operation to complete, at which point the reset gmm button will be enabled, refreshing every 30 seconds up to 10 times

  let attempts = 0;
  const maxAttempts = 10;
  const refreshInterval = 60000; // 1 minute in milliseconds
  let isButtonEnabled = false;
  
  while (!isButtonEnabled && attempts < maxAttempts) {    
    try {
      // Check if button is enabled with a shorter timeout to avoid hanging
      isButtonEnabled = await page.getByRole('button', { name: 'Reset GMM' }).isEnabled({ timeout: 2000 });
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
    console.log(`Attempt ${attempts}/${maxAttempts}: Button not enabled, waiting 1 minute...`);
    
    if (attempts < maxAttempts) {
      await page.waitForTimeout(refreshInterval);
      await page.reload();
      
      // Navigate back to the operations tab after refresh
      await page.getByRole('button', { name: 'Settings' }).click();
      await page.getByRole('tab', { name: 'Operations Operations' }).click();
      await page.waitForTimeout(5000);
    }
  }

  await page.goto(url);
  await page.waitForTimeout(5000);

  await expect(page.locator('#manage-membership-button')).toBeVisible();
  
  console.log('✅ Maintenance reset completed - API should be restored');
  console.log('🔄 All tests in this session should be complete');
});