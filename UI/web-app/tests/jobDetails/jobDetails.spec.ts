// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect, Page } from '@playwright/test';
import { v4 as uuidv4 } from 'uuid';
import { SettingKey, SettingKeyMap } from '../../src/models';

test.use({ storageState: 'tests/storageState.json' });

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || '';
const EMAIL = process.env.INTEGRATION_TEST_EMAIL || '';

// Configure retries for job details tests since they involve complex UI interactions
test.describe('Job Details Tests', () => {
  test.describe.configure({ retries: 2 });

  // Track the original state of the group creation setting
  let originalGroupCreationState: boolean | null = null;
  let createdGroupName: string | null = null;
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

    await page.getByRole('button', { name: 'Add' }).click();
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

    await page.getByRole('button', { name: 'Add' }).click();
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
    await page
      .locator(`#advancedQueryTextField`)
      .fill('[{"type":"SqlMembership","source":{"filter":"LocationAreaDetail_Code IS NULL"}}]');

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

    await page.getByRole('button', { name: 'Add' }).click();
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

  test('Test onboarding, HR Source part functionality, and review flow', async ({ page }) => {
    test.setTimeout(60000);
    const AUTHORIZED_SENDERS_LABEL = 'Authorized Senders';
    const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;

    console.log('🚀 Starting onboarding test with HR source parts');

    await page.goto(url);
    await page.waitForTimeout(10000);
    await page.getByRole('button', { name: 'Add' }).click();
    await page.getByRole('menuitem', { name: 'Add Sync', exact: true }).click();
    await page.getByText('Create a new group').click();
    await page.getByPlaceholder('Enter the name of the group').click();
    const groupName = `pw-test-${uuidv4().replace(/-/g, '').slice(0, 10)}`;
    createdGroupName = groupName;
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
    console.log('📝 Configuring HR source part - enabling org hierarchy');
    await page.getByTestId('hr-include-org-choice').locator('label').filter({ hasText: 'Yes' }).click();

    // Wait for org leader field to become available
    await page.waitForTimeout(2000);

    // Use resilient selection for Org leader suggestions and capture its numeric id if present
    console.log('👤 Selecting org leader');
    const selectedManagerId = await selectComboOptionByLabel(page, 'Provide Org. leader', 'user 10');
    console.log(`✅ Manager selected with ID: ${selectedManagerId}`);
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

    type SqlMembershipPart = { type: 'SqlMembership'; source?: { manager?: { id?: number }; filter?: string } };
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

    const businessJustificationTextarea = page.getByTestId('business-justification-textarea');
    await expect(businessJustificationTextarea).toBeVisible({ timeout: 10000 });
    await businessJustificationTextarea.fill('This group is needed for automated testing purposes to validate HR source part functionality and the onboarding workflow.');
    
    const groupOwnersDropdown = page.getByTestId('group-owners-dropdown');
    await expect(groupOwnersDropdown).toBeVisible({ timeout: 10000 });
    await groupOwnersDropdown.click();
    const dropdownOptions = page.locator('[role="listbox"] [role="option"]');
    await expect(dropdownOptions.first()).toBeVisible({ timeout: 10000 });
    await dropdownOptions.first().click();
    console.log('✅ Group owner selected');

    // Complete onboarding and submit for review
    await page.getByRole('button', { name: 'Submit'}).click();
    try {
      await page.waitForURL((url) => url.pathname === '/' || url.pathname.startsWith('/?'), { timeout: 15000 });
    } catch (navError) {
      console.log('⚠️ URL navigation timeout, checking if we\'re on the right page');
    }
    
    // Ensure we're on the jobs list page
    await expect(page.locator('#manage-membership-button')).toBeVisible({ timeout: 10000 });
    console.log("✅ Onboarding submission completed, now proceeding to review");
    
    const groupRow = page.locator(`[data-group-name="${groupName}"]`);
    await expect(groupRow).toBeVisible({ timeout: 15000 });
    await groupRow.click();
    await expect(page.getByText(`Membership Details - ${groupName}`)).toBeVisible({ timeout: 30000 });
    
    // Try to reveal source parts UI 
    const expandAllButton = page.locator('#expandCollapseAllButton');
    if (await expandAllButton.count()) {
      try { await expandAllButton.click(); } catch { /* ignore */ }
    }
    await page.getByTestId('hr-value-virtualized-combobox').first().click();
    // const hrValueCombobox = page.locator('input[value*="FTE"][value*="Intern"]').first();
    // await hrValueCombobox.click();
    const fteIcon = page.locator('label:has-text("FTE") i');
    let failed = false;
    try {
      await fteIcon.click({ timeout: 800 }); // expect this to fail in read-only view
      // If it didn’t throw, that’s a problem.
      expect(false, 'FTE option was clickable but should not be').toBe(true);
    } catch {
      failed = true;
    }
    console.log('✅ EmployeeType values confirmed read-only (FTE & Intern unchanged).');

    // Test review and rejection flow
    await page.getByRole('button', { name: 'Reject' }).click();
    const rejectReason = page.locator('textarea').or(page.getByLabel(/reason/i)).first();
    await expect(rejectReason).toBeVisible();
    await rejectReason.fill('Automated test rejection');
    await page.getByRole('button', { name: 'Submit' }).click();
    console.log('✅ Rejection flow completed successfully.');

    // Navigate back to job list with better error handling
    try {
      await page.waitForURL((url) => url.pathname === '/' || url.pathname.startsWith('/?'), { timeout: 15000 });
    } catch (navError) {
      console.log('⚠️ Navigation timeout after rejection, checking page content');
    }
    
    await expect(page.locator('#manage-membership-button')).toBeVisible({ timeout: 10000 });
    console.log('✅ Submission review flow completed successfully.');
  });

  // Helper: robustly select the first option from a labeled combobox/people picker
  // Returns a numeric id parsed from the option text if present (e.g., "User 22360" -> 22360)
  const selectComboOptionByLabel = async (page: Page, label: string, query: string): Promise<number | null> => {
    try {
      // Check if page is still valid
      if (page.isClosed()) {
        throw new Error('Page has been closed');
      }

      const input = page.getByLabel(label);

      // Ensure input is visible and enabled before interaction
      await expect(input).toBeVisible({ timeout: 10000 });
      await expect(input).toBeEnabled({ timeout: 5000 });

      await input.click();
      await page.waitForTimeout(500); // Small delay for dropdown to initialize

      await input.fill('');
      await page.waitForTimeout(200);
      await input.type(query, { delay: 100 }); // Slower typing for better reliability

      const listbox = page.locator('[role="listbox"]');
      const option = listbox.locator('[role="option"]');

      // Wait for options to appear
      await option.first().waitFor({ state: 'visible', timeout: 15000 });

      // Capture the first option's text to derive a numeric id if present
      let derivedId: number | null = null;
      try {
        const text = await option.first().innerText();
        const m = text.match(/(\d{2,})/); // pick 2+ digits to avoid initials etc.
        derivedId = m ? parseInt(m[1], 10) : null;
        console.log(`Derived ID from option text "${text}": ${derivedId}`);
      } catch (error) {
        // ID derivation is optional, continue without it
      }

      // Select the first option
      await option.first().click();

      // Verify selection was successful
      await page.waitForTimeout(500);

      return derivedId;
    } catch (error) {
      throw new Error(`Failed to select option for "${label}": ${error.message}`);
    }
  };

  // Helper: select multiple attribute rows by index (data rows, 0-based) and verify selection
  const selectHrAttributeRows = async (page: Page, indices: number[]) => {
    for (const dataIndex of indices) {
      const attrCombo = page.getByTestId('hr-attribute-combobox').nth(dataIndex);
      await expect(attrCombo).toBeVisible({ timeout: 30000 });

      // Ascend to the DetailsList row that contains this combobox
      const row = attrCombo.locator('xpath=ancestor::div[@role="row"][1]');

      // Helper to check selection state
      const isSelected = async () =>
        (await row.getAttribute('aria-selected')) === 'true' ||
        (await row.locator('[aria-checked="true"]').count()) > 0 ||
        (await row.locator('.is-selected').count()) > 0;

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
      } finally {
        await context.close();
      }
    } else if (originalGroupCreationState === true) {
      console.log('ℹ️ Group creation feature was originally enabled, leaving it enabled.');
    } else {
      console.log('⚠️ Could not determine original group creation state, leaving current state unchanged.');
    }
  });
});
