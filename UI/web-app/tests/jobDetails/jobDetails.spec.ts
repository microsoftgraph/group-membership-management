// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect } from '@playwright/test';
import { v4 as uuidv4 } from 'uuid';
import { SettingKey, SettingKeyMap } from '../../src/models';

test.use({ storageState: 'tests/storageState.json' });

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || '';

// Track the original state of the group creation setting
let originalGroupCreationState: boolean | null = null;

// Ensure group creation is enabled before running tests
test.beforeAll(async ({ browser }) => {
  const context = await browser.newContext({ storageState: 'tests/storageState.json' });
  const page = await context.newPage();
  
  try {
    const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
    await page.goto(`${url}/Admin`);
    await page.locator('text="General"').click();
    
    // Wait for the page to fully load and any async validations to complete
    await page.waitForTimeout(3000);
    
    const groupCreationToggleId = SettingKeyMap[SettingKey.CreateGroupFeatureEnabled];
    const isGroupCreationEnabled = await page.locator(`#${groupCreationToggleId}`).isChecked();
    
    // Store the original state
    originalGroupCreationState = isGroupCreationEnabled;
    
    if (!isGroupCreationEnabled) {
      console.log('⚙️ Enabling group creation feature...');
      await page.locator(`#${groupCreationToggleId}`).click();
      
      // Wait for the Save button to become enabled (URL validations might need to complete)
      // Use role selector for better reliability than text selector
      const saveButton = page.locator('text="Save"');
      await expect(saveButton).toBeVisible({ timeout: 10000 });
      await expect(saveButton).toBeEnabled({ timeout: 10000 });
      
      await saveButton.click();
      await page.waitForTimeout(5000);
      console.log('✅ Group creation feature enabled.');
    } else {
      console.log('✅ Group creation feature is already enabled.');
    }
  } catch (error) {
    console.error('❌ Failed to enable group creation feature:', error);
    throw error;
  } finally {
    await context.close();
  }
});

test('Create a group with AuthorizedSenders', { tag: '@main' }, async ({ page }) => {
  const AUTHORIZED_SENDERS_LABEL = 'Authorized Senders';
  const EXPECTED_SENDER_TEXT = 'adele';
  const GROUP_NAME = 'contoso';
  const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
  await page.goto(url);
  await page.waitForTimeout(10000);

  await page.getByRole('button', { name: 'Manage Membership' }).click();
  await page.getByRole('menuitem', { name: 'Add Sync', exact: true }).click();
  await page.getByText('Create a new group').click();
  await page.getByPlaceholder('Enter the name of the group').click();

  const groupName = `pw-test-${uuidv4().replace(/-/g, '').slice(0, 10)}`;
  console.log(`Group name: ${groupName}`);

  // Fill group name
  await page.getByPlaceholder('Enter the name of the group').fill(groupName);

  // Select authorized senders
  await page.getByLabel(AUTHORIZED_SENDERS_LABEL).click();
  await page.getByLabel(AUTHORIZED_SENDERS_LABEL).fill('adele');
  await page.getByRole('option', { name: 'Adele Vance' }).click();
  await page.getByRole('combobox', { name: AUTHORIZED_SENDERS_LABEL }).fill('alex');
  await page.getByRole('option', { name: 'Alex Wilber' }).first().click();

  // Create group
  await page.getByRole('button', { name: 'Create group' }).click();
  await page.waitForSelector('button:has-text("Next")'); // Wait for the "Next" button to appear

  // Wait for the "Next" button to become enabled (up to 30 seconds)
  await page.waitForFunction(
    () => {
      const nextButton = Array.from(document.querySelectorAll('button')).find(
        (button) => button.textContent?.trim() === 'Next'
      );
      return nextButton && !nextButton.disabled;
    },
    { timeout: 30000 }
  );

  // Alternatively, using expect with locator
  const nextButton = page.getByRole('button', { name: 'Next' });
  await expect(nextButton).toBeEnabled({ timeout: 30000 });

  // Navigate through steps
  await page.getByRole('button', { name: 'Next' }).click();
  await page.getByRole('button', { name: 'Next' }).click();
  await page.getByRole('button', { name: 'Add Source Part' }).click();

  // Select group membership - use dropdown directly to avoid depending on source part label
  await page.locator('.ms-Dropdown').first().click();
  await page.getByRole('option', { name: 'Group Membership' }).click();

  // Search and select group name
  await page.getByLabel('Search group name').click();
  await page.getByLabel('Search group name').fill(GROUP_NAME);
  await page.getByRole('option', { name: GROUP_NAME }).first().click();
  await page.getByRole('button', { name: 'Next' }).click();

  // Verify authorized sender
  const siblingSpan = page.locator(`text=${AUTHORIZED_SENDERS_LABEL}`).locator('xpath=following-sibling::span');
  await expect(siblingSpan).toHaveText(new RegExp(`${EXPECTED_SENDER_TEXT}`, 'i'));

  console.log('✅ Authorized senders test completed successfully.');
});

// Reset group creation setting to original state if it was originally disabled
test.afterAll(async ({ browser }) => {
  if (originalGroupCreationState === false) {
    const context = await browser.newContext({ storageState: 'tests/storageState.json' });
    const page = await context.newPage();
    
    try {
      const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
      await page.goto(`${url}/Admin`);
      await page.locator('text="General"').click();
      
      // Wait for the page to fully load and any async validations to complete
      await page.waitForTimeout(3000);
      
      const groupCreationToggleId = SettingKeyMap[SettingKey.CreateGroupFeatureEnabled];
      console.log('🔄 Resetting group creation feature to disabled state...');
      await page.locator(`#${groupCreationToggleId}`).click();
      
      // Wait for the Save button to become enabled
      const saveButton = page.locator('text="Save"');
      await expect(saveButton).toBeVisible({ timeout: 10000 });
      await expect(saveButton).toBeEnabled({ timeout: 10000 });
      
      await saveButton.click();
      await page.waitForTimeout(5000);
      console.log('✅ Group creation feature reset to disabled state.');
    } catch (error) {
      console.error('❌ Failed to reset group creation feature:', error);
    } finally {
      await context.close();
    }
  } else if (originalGroupCreationState === true) {
    console.log('ℹ️ Group creation feature was originally enabled, leaving it enabled.');
  } else {
    console.log('⚠️ Could not determine original group creation state, leaving current state unchanged.');
  }
});
