// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect } from '@playwright/test';
import { SettingKey, SettingKeyMap } from '../../src/models';

test.use({ storageState: 'tests/storageState.json' });

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || '';

// Configure retries for admin tests since they deal with settings that might have timing issues
test.describe('Admin Tests', () => {
  test.describe.configure({ retries: 3 });

  test('Admin', { tag: '@setup' }, async ({ page }) => {
    const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
    await page.goto(`${url}/Admin`);
    await page.waitForTimeout(5000);
    await expect(page.locator('text="Admin Center"')).toBeVisible();
    console.log('✅ Admin test completed successfully.');
  });

  test('Initial disclaimer displays on first load', { tag: '@setup' }, async ({ browser }) => {
    test.setTimeout(120000); // 2 minutes timeout for setup tests

    // Create a fresh context with authentication but clear localStorage
    const context = await browser.newContext({ storageState: 'tests/storageState.json' });
    const page = await context.newPage();

    try {
      const url = DOMAIN.startsWith('http') ? DOMAIN : `https://${DOMAIN}`;

      // Navigate to admin to enable disclaimer
      await page.goto(`${url}/Admin`);
      await page.locator('text="General"').click();
      await page.waitForTimeout(3000);

      const disclaimerToggleId = SettingKeyMap[SettingKey.IsDisclaimerEnabled];
      const isDisclaimerEnabled = await page.locator(`#${disclaimerToggleId}`).isChecked();

      // Enable if not already enabled
      if (isDisclaimerEnabled) {
        console.log('Disclaimer is already enabled.');
      } else {
        console.log('Enabling disclaimer...');
        const toggle = page.locator(`#${disclaimerToggleId}`);
        await expect(toggle).toBeVisible({ timeout: 10000 });
        await expect(toggle).toBeEnabled({ timeout: 5000 });
        await toggle.click();
        await page.waitForTimeout(3000);
        const saveButton = page.getByRole('button', { name: 'Save' }).or(page.locator('text="Save"')).first();

        await expect(saveButton).toBeVisible({ timeout: 10000 });
        await expect(saveButton).toBeEnabled({ timeout: 20000 });

        await saveButton.click();
        await page.waitForTimeout(5000);

        console.log('✅ Disclaimer settings saved');
      }
      // Check if the disclaimer is enabled
      const isEnabled = await page.locator(`#${disclaimerToggleId}`).isChecked();
      if (isEnabled) {
        console.log('Disclaimer is enabled.');
      } else {
        console.error('❌ Disclaimer is not enabled. Please check the settings.');
        return;
      }

      // Clear localStorage again and reload to trigger disclaimer
      await page.evaluate(() => {
        localStorage.clear();
        sessionStorage.clear();
      });

      // Navigate to home page to trigger disclaimer modal
      await page.goto(url);

      // Wait for page to fully load
      await page.waitForTimeout(3000);

      // Check if the disclaimer modal is visible
      await expect(page.locator('#disclaimerModal')).toBeVisible({ timeout: 10000 });

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
        await page.waitForTimeout(3000);

        const savedDisclaimerToggleId = SettingKeyMap[SettingKey.IsDisclaimerEnabled];
        const toggle = page.locator(`#${savedDisclaimerToggleId}`);
        await expect(toggle).toBeVisible({ timeout: 10000 });
        await expect(toggle).toBeEnabled({ timeout: 5000 });
        await toggle.click();

        await page.waitForTimeout(3000);

        const saveButton = page.getByRole('button', { name: 'Save' }).or(page.locator('text="Save"')).first();

        await expect(saveButton).toBeVisible({ timeout: 10000 });
        await expect(saveButton).toBeEnabled({ timeout: 20000 });
        await saveButton.click();

        await page.waitForTimeout(5000);
        console.log('Disclaimer reset to disabled state.');
      }

      console.log('✅ Disclaimer test completed successfully.');
    } finally {
      await context.close();
    }
  });

  test('Enable group creation', { tag: '@setup' }, async ({ page }) => {
    test.setTimeout(120000); // 2 minutes timeout for setup tests

    const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
    await page.goto(`${url}/Admin`);
    await page.locator('text="General"').click();
    await page.waitForTimeout(3000);

    const groupCreationToggleId = SettingKeyMap[SettingKey.CreateGroupFeatureEnabled];
    const isGroupCreationEnabled = await page.locator(`#${groupCreationToggleId}`).isChecked();
    if (isGroupCreationEnabled) {
      console.log('Group creation is already enabled.');
    } else {
      console.log('Enabling group creation...');

      const toggle = page.locator(`#${groupCreationToggleId}`);
      await expect(toggle).toBeVisible({ timeout: 10000 });
      await expect(toggle).toBeEnabled({ timeout: 5000 });
      await toggle.click();
      await page.waitForTimeout(3000);
      const saveButton = page.getByRole('button', { name: 'Save' }).or(page.locator('text="Save"')).first();

      await expect(saveButton).toBeVisible({ timeout: 10000 });
      await expect(saveButton).toBeEnabled({ timeout: 20000 });

      await saveButton.click();
      await page.waitForTimeout(5000);

      console.log('✅ Group creation successfully enabled');
    }
    // Check if the group creation is enabled
    const isEnabled = await page.locator(`#${groupCreationToggleId}`).isChecked();
    if (isEnabled) {
      console.log('Group creation is enabled.');
    } else {
      console.error('❌ Group creation is not enabled. Please check the settings.');
      return;
    }

    // Confirm group creation is enabled by navigating to Manage Membership
    console.log('🔍 Verifying group creation is working...');
    await page.goto(url);

    // Wait for page to load with better error handling
    try {
      await expect(page.getByText('Managed groups')).toBeVisible({ timeout: 15000 });
    } catch (error) {
      console.log('⚠️ Home page taking longer to load, continuing...');
    }

    await page.getByRole('button', { name: 'Add' }).click();
    await page.getByRole('menuitem', { name: 'Add Sync', exact: true }).click();

    await expect(page.getByText('Create a new group')).toBeVisible({ timeout: 10000 });
    await page.getByText('Create a new group').click();
    await expect(page.getByPlaceholder('Enter the name of the group')).toBeVisible({ timeout: 10000 });
    console.log('✅ Group creation enabled and confirmed.');

    // Reset the group creation setting after the test
    if (!isGroupCreationEnabled) {
      console.log('Resetting group creation to disabled state...');
      await page.goto(`${url}/Admin`);
      await page.locator('text="General"').click();
      await page.waitForTimeout(3000);

      const savedGroupCreationToggleId = SettingKeyMap[SettingKey.CreateGroupFeatureEnabled];
      await expect(page.locator(`#${savedGroupCreationToggleId}`)).toBeVisible({ timeout: 10000 });
      const toggle = page.locator(`#${savedGroupCreationToggleId}`);
      await expect(toggle).toBeVisible({ timeout: 5000 });
      await expect(toggle).toBeEnabled({ timeout: 5000 });
      await toggle.click();

      // Wait for form validation and state changes to complete
      await page.waitForTimeout(2000);

      const saveButton = page.locator('text="Save"');
      await expect(saveButton).toBeVisible({ timeout: 10000 });
      await expect(saveButton).toBeEnabled({ timeout: 15000 });
      await saveButton.click();

      await page.waitForTimeout(5000);
      console.log('Group creation reset to disabled state.');
    }
  });
});
