// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect } from '@playwright/test';
import { v4 as uuidv4 } from 'uuid';

test.use({ storageState: 'tests/storageState.json' });

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || '';

test('AuthorizedSenders', async ({ page }) => {
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

  // Select group membership
  await page.getByText('SqlMembership').click();
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
