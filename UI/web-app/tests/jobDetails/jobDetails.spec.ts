// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect, Page } from '@playwright/test';
import { v4 as uuidv4 } from 'uuid';
import { SettingKey, SettingKeyMap } from '../../src/models';
import { SourcePartType } from '../../src/models/SourcePartType';

test.use({ storageState: 'tests/storageState.json' });

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || '';
const EMAIL = process.env.INTEGRATION_TEST_EMAIL || '';


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

  // Test: Verify dropdown for RequestedOnBehalfOf displays current user since they are the creator/owner of the group
  const requestedOnBehalfOfDropdown = page.locator('#groupOwnersDropdown');
  await expect(requestedOnBehalfOfDropdown).toBeVisible();
  await requestedOnBehalfOfDropdown.click();
  const dropdownContent = page.locator('.ms-Dropdown-callout');
  await expect(dropdownContent).toBeVisible();
  await expect(dropdownContent).toContainText(EMAIL);
  console.log('✅ RequestedOnBehalfOf dropdown test completed successfully.');
});

test('Render textfield if query has unsupported operator', async ({ page }) => {
  const AUTHORIZED_SENDERS_LABEL = 'Authorized Senders';
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
  
  // Fill in the advanced view textfield with unsupported IS operator
  await page.locator(`#advancedViewToggle`).click();
  await page.locator(`#advancedQueryTextField`).fill('[{"type":"SqlMembership","source":{"filter":"LocationAreaDetail_Code IS NULL"}}]');
  
  // Tab out to trigger validation
  await page.keyboard.press('Tab');
  await page.waitForTimeout(1000);
  await page.locator(`#advancedViewToggle`).click();
  await page.locator(`#expandCollapseAllButton`).click();
  await expect(page.locator('#filterTextField')).toBeVisible();

  console.log('✅ Render textfield if query has unsupported operator test completed successfully.');
});

test('Verify inclusionary logic correctly updates the query', { tag: '@main' }, async ({ page }) => {
  const AUTHORIZED_SENDERS_LABEL = 'Authorized Senders';
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
  
  // Verify it's inclusionary by default
  await page.getByRole('button', { name: 'Add Source Part' }).click();
  await expect(page.getByLabel('Include Source Part').getByLabel('Yes')).toBeChecked();
  
  // Verify updating the value updates the query correctly
  await page.getByLabel('Include Source Part').locator('label').filter({ hasText: 'No' }).click();
  await page.getByLabel('Advanced View').click();
  await expect(page.locator('#advancedQueryTextField')).toContainText('"exclusionary":true');
  await page.getByLabel('Advanced View').click();
  await page.getByLabel('Include Source Part').getByText('Yes').click();
  await page.getByLabel('Advanced View').click();
  await expect(page.locator('#advancedQueryTextField')).toContainText('"exclusionary":false');
  console.log('✅ Inclusionary logic correctly updates the query test completed successfully.');
});

test('Test HR Source part functionality', async ({ page }) => {
  const AUTHORIZED_SENDERS_LABEL = 'Authorized Senders';
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
  await page.waitForSelector('button:has-text("Next")');
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
  
  // Create an HR source part
  await page.getByRole('button', { name: 'Add Source Part' }).click();

  // Add attributes, groupings, and operators
  await page.getByTestId('hr-include-org-choice').locator('label').filter({ hasText: 'Yes' }).click();

  // Use resilient selection for Org leader suggestions and capture its numeric id if present
  const selectedManagerId = await selectComboOptionByLabel(page, 'Provide Org. leader', 'user 10', 'user10');
  await page.getByTestId('hr-include-filter-choice').locator('label').filter({ hasText: 'Yes' }).click();
  await page.getByTestId('hr-attribute-combobox').first().click();
  await page.getByRole('option', { name: 'EmployeeType' }).click();
  await page.getByTestId('hr-equality-operator-dropdown').first().click();
  await page.getByRole('option', { name: 'IN', exact: true }).click();
  await page.getByTestId('hr-value-virtualized-combobox').first().click();
  await page.locator('label').filter({ hasText: 'FTE' }).locator('i').click();
  await page.locator('label').filter({ hasText: 'Intern' }).locator('i').click();
  await page.getByPlaceholder('FTE, Intern').click();
  await page.getByTestId('hr-andor-dropdown').first().click();
  await page.getByRole('option', { name: 'And' }).click();
  await page.getByTestId('hr-add-attribute-button').click();
  await page.getByTestId('hr-attribute-combobox').nth(1).click();
  await page.getByRole('option', { name: 'SupervisorInd' }).click();
  await page.getByTestId('hr-equality-operator-dropdown').nth(1).click();
  await page.getByRole('option', { name: '=' }).click();
  await page.getByTestId('hr-value-virtualized-combobox').nth(1).click();
  await page.getByRole('option', { name: 'Yes' }).click();
  await page.getByTestId('hr-andor-dropdown').nth(1).click();
  await page.getByRole('option', { name: 'Or' }).click();
  await page.getByTestId('hr-add-attribute-button').click();
  await page.getByTestId('hr-attribute-combobox').nth(2).click();
  await page.getByRole('option', { name: 'PayScaleStockLevelNbr' }).click();
  await page.getByTestId('hr-equality-operator-dropdown').nth(2).click();
  await page.getByRole('option', { name: '>=' }).click();
  await page.getByTestId('hr-value-textfield').click();
  await page.getByTestId('hr-value-textfield').fill('65');

  // Select the 2nd and 3rd attribute rows, then group
  await selectHrAttributeRows(page, [1, 2]);
  await page.getByTestId('hr-group-button').click();

  // Validate query in confirmation step
  await page.getByRole('button', { name: 'Next' }).click();

  // Read the displayed JSON query from the read-only textarea in the "Source Parts" section
  const queryText = await page.locator('textarea[readonly]').first().inputValue();
  const parsed = JSON.parse(queryText) as unknown;

  type SqlMembershipPart = { type: 'SqlMembership'; source?: { manager?: { id?: number }, filter?: string } };
  const isRecord = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null;
  const isSqlMembershipPart = (v: unknown): v is SqlMembershipPart => isRecord(v) && v['type'] === 'SqlMembership';

  const getSqlPart = (v: unknown): SqlMembershipPart | null => {
    if (Array.isArray(v)) {
      const found = v.find((p): p is SqlMembershipPart => isSqlMembershipPart(p));
      return found ?? null;
    }
    return isSqlMembershipPart(v) ? v : null;
  };

  const sqlPart = getSqlPart(parsed);
  const managerIdInQuery = sqlPart?.source?.manager?.id;
  const filterInQuery: string = sqlPart?.source?.filter || '';

  // Assert manager id presence and match when we derived one from the picker
  expect(typeof managerIdInQuery).toBe('number');
  if (selectedManagerId != null) {
    expect(managerIdInQuery).toBe(selectedManagerId);
  }

  // Assert critical parts of the filter without relying on exact formatting
  expect(filterInQuery).toContain("EmployeeType_Code IN ('FTE', 'Intern')");
  expect(filterInQuery).toMatch(/SupervisorInd\s*=\s*1/);
  expect(filterInQuery).toMatch(/PayScaleStockLevelNbr\s*>=\s*65/);
  expect(filterInQuery).toMatch(/\)\s*And\s*\(/);
  expect(filterInQuery).toMatch(/\sOr\s/);

  console.log('✅ HR Source part test completed successfully.');
});


// Helper: robustly select the first option from a labeled combobox/people picker
// Returns a numeric id parsed from the option text if present (e.g., "User 22360" -> 22360)
const selectComboOptionByLabel = async (page: Page, label: string, query: string, fallbackQuery?: string): Promise<number | null> => {
  const input = page.getByLabel(label);
  await input.click();
  await input.fill('');
  await input.type(query, { delay: 50 });

  const listbox = page.locator('[role="listbox"]');
  const option = listbox.locator('[role="option"]');

  const waitForOptions = async (timeout = 30000) => {
    await option.first().waitFor({ state: 'visible', timeout });
  };

  // Attempt 1: wait after typing
  try {
    await waitForOptions(10000);
  } catch {
    // Attempt 2: open dropdown via keyboard
    try {
      await input.press('ArrowDown');
      await waitForOptions(10000);
    } catch {
      // Attempt 3: use fallback query if provided
      if (fallbackQuery) {
        await input.fill('');
        await input.type(fallbackQuery, { delay: 50 });
        try {
          await waitForOptions(10000);
        } catch {
          // Attempt 4: blur/refocus and open
          await input.press('Tab');
          await input.click();
          await input.press('ArrowDown');
          await waitForOptions(10000);
        }
      } else {
        // Attempt 4 without fallback
        await input.press('Tab');
        await input.click();
        await input.press('ArrowDown');
        await waitForOptions(10000);
      }
    }
  }

  // Capture the first option's text to derive a numeric id if present
  let derivedId: number | null = null;
  try {
    const text = await option.first().innerText();
    const m = text.match(/(\d{2,})/); // pick 2+ digits to avoid initials etc.
    derivedId = m ? parseInt(m[1], 10) : null;
  } catch { /* ignore */ }

  // Select the first option
  try {
    await option.first().click();
  } catch {
    await input.press('Enter');
  }

  return derivedId;
};

// Helper: select multiple attribute rows by index (data rows, 0-based) and verify selection
const selectHrAttributeRows = async (page: Page, indices: number[]) => {
  for (const dataIndex of indices) {
    const attrCombo = page.getByTestId('hr-attribute-combobox').nth(dataIndex);
    await expect(attrCombo).toBeVisible({ timeout: 30000 });

    // Ascend to the DetailsList row that contains this combobox
    const row = attrCombo.locator('xpath=ancestor::div[@role="row"][1]');

    // Helper to check selection state
    const isSelected = async () => (
      (await row.getAttribute('aria-selected')) === 'true' ||
      (await row.locator('[aria-checked="true"]').count()) > 0 ||
      (await row.locator('.is-selected').count()) > 0
    );

    // Skip if already selected
    if (await isSelected()) continue;

    // Prefer clicking the selection checkbox/toggle to avoid deselecting others
    const toggle = row.locator('[data-selection-toggle]');
    const checkbox = row.locator('[role="checkbox"]');

    if (await toggle.count()) {
      await toggle.first().click();
    } else if (await checkbox.count()) {
      await checkbox.first().click();
    } else {
      // Fall back to additive selection: Ctrl+Click (or Ctrl+Space)
      try {
        await row.click({ modifiers: ['Control'] });
      } catch {
        try {
          await row.focus();
          await page.keyboard.down('Control');
          await page.keyboard.press(' ');
          await page.keyboard.up('Control');
        } catch {
          // Last resort: plain Space (may toggle current row)
          await row.focus();
          await page.keyboard.press(' ');
        }
      }
    }

    // Verify selection via multiple heuristics
    await expect.poll(async () => await isSelected()).toBe(true);
  }
};

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
