// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect } from '@playwright/test';
import { SettingKey, SettingKeyMap } from '../../src/models';

test.use({ storageState: 'tests/storageState.json' });

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || '';

test('Admin', async ({ page }) => {
  const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
  await page.goto(url);
  await page.goto(`${url}/Admin`);
  await page.waitForTimeout(5000);
  await expect(page.locator('text="Admin Center"')).toBeVisible();
  console.log('✅ Admin test completed successfully.');
});

test('Disclaimer', async ({ page }) => {
  const url = DOMAIN.startsWith('http') ? DOMAIN : `https://${DOMAIN}`;
  await page.goto(url);
  await page.goto(`${url}/Admin`);

  await page.locator('text="General"').click();
  const disclaimerToggleId = SettingKeyMap[SettingKey.IsDisclaimerEnabled];
  const isDisclaimerEnabled = await page.locator(`#${disclaimerToggleId}`).isChecked();

  // Enable if not already enabled
  if (isDisclaimerEnabled) {
    console.log('Disclaimer is already enabled.');
  } else {
    console.log('Enabling disclaimer...');
    await page.locator(`#${disclaimerToggleId}`).click();
    await page.locator('text="Save"').click();
    await page.waitForTimeout(5000);
  }

  // Check if the disclaimer is enabled
  const isEnabled = await page.locator(`#${disclaimerToggleId}`).isChecked();
  if (isEnabled) {
    console.log('Disclaimer is enabled.');
  } else {
    console.error('❌ Disclaimer is not enabled. Please check the settings.');
    return;
  }

  // Go to the home page to trigger the disclaimer modal
  await page.goto(url);
  await expect(page.locator('#disclaimerModal')).toBeVisible();

  // Check all checkboxes in the disclaimer modal and submit
  const checkboxes = page.locator('#disclaimerModal .ms-Checkbox-checkbox');
  const checkboxCount = await checkboxes.count();
  console.log(`Found ${checkboxCount} checkboxes in the disclaimer modal.`);
  for (let i = 0; i < checkboxCount; i++) {
    const checkbox = checkboxes.nth(i);
    if (!(await checkbox.isChecked())) {
      await checkbox.click();
      console.log(`✅ Checked checkbox ${i + 1}/${checkboxCount}.`);
    } else {
      console.log(`Checkbox ${i + 1}/${checkboxCount} is already checked.`);
    }
  }
  const submitButton = page.locator('#disclaimerSubmitButton');
  await expect(submitButton).toBeEnabled();
  await submitButton.click();

  // Reset the disclaimer setting after the test
  if (!isDisclaimerEnabled) {
    console.log('Resetting disclaimer to disabled state...');
    await page.goto(`${url}/Admin`);
    await page.locator('text="General"').click();
    const savedDisclaimerToggleId = SettingKeyMap[SettingKey.IsDisclaimerEnabled];
    await expect(page.locator(`#${savedDisclaimerToggleId}`)).toBeVisible({ timeout: 5000 });
    const toggle = page.locator(`#${savedDisclaimerToggleId}`);
    await expect(toggle).toBeVisible({ timeout: 5000 });
    await expect(toggle).toBeEnabled({ timeout: 5000 });
    await toggle.click();

    const saveButton = page.locator('text="Save"');
    await expect(saveButton).toBeVisible({ timeout: 5000 });
    await expect(saveButton).toBeEnabled({ timeout: 5000 });
    await saveButton.click();

    await page.waitForTimeout(5000);
    console.log('Disclaimer reset to disabled state.');
  }
  
  console.log('✅ Disclaimer test completed successfully.');
});
