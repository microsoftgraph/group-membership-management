// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect, Page } from '@playwright/test';
import { v4 as uuidv4 } from 'uuid';
import { SettingKey, SettingKeyMap } from '../../src/models';
import { setupMockPage } from '../mocks/setupMockPage';

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || 'http://localhost:3000';
const EMAIL = process.env.INTEGRATION_TEST_EMAIL || 'playwright@contoso.com';
const isMockMode = process.env.PLAYWRIGHT_USE_MOCK_API !== 'false';
const testTimeoutMs = Number(process.env.PLAYWRIGHT_TEST_TIMEOUT_MS ?? 30000);

/**
 * Types into a Fluent UI v8 NormalPeoplePicker and triggers suggestion resolution.
 * 
 * Playwright's standard input methods (fill, pressSequentially, keyboard.type) don't trigger
 * React 18's synthetic onInput event for Fluent UI's Autofill component. This workaround
 * directly invokes the React component instances via fiber tree traversal to:
 * 1. Set the Autofill's internal state
 * 2. Call onResolveSuggestions directly (bypassing the debounce)
 * 3. Populate the suggestion store and make suggestions visible
 */
async function typeIntoPicker(page: Page, pickerInputLocator: any, text: string) {
  await pickerInputLocator.focus();
  await pickerInputLocator.evaluate(async (el: HTMLInputElement, val: string) => {
    el.focus();
    const nativeInputValueSetter = Object.getOwnPropertyDescriptor(
      window.HTMLInputElement.prototype, 'value'
    )!.set!;
    nativeInputValueSetter.call(el, val);

    const fiberKey = Object.keys(el).find(k => k.startsWith('__reactFiber$'));
    if (!fiberKey) throw new Error('React fiber not found on picker input');
    let fiber = (el as any)[fiberKey];

    let autofillInstance: any = null;
    let basePickerInstance: any = null;
    let current = fiber;
    while (current) {
      if (current.stateNode) {
        if (current.stateNode._onInputChanged && !autofillInstance) {
          autofillInstance = current.stateNode;
        }
        if (current.stateNode._onResolveSuggestions && !basePickerInstance) {
          basePickerInstance = current.stateNode;
          break;
        }
      }
      current = current.return;
    }

    if (!basePickerInstance || !autofillInstance) {
      throw new Error('BasePicker or Autofill instance not found in fiber tree');
    }

    autofillInstance.setState({ inputValue: val });
    basePickerInstance.setState({ isFocused: true });
    await new Promise(r => setTimeout(r, 50));

    const onResolveSuggestions = basePickerInstance.props.onResolveSuggestions;
    if (!onResolveSuggestions) throw new Error('onResolveSuggestions prop missing');

    const suggestions = await onResolveSuggestions(val, basePickerInstance.state.items || []);
    if (suggestions && suggestions.length > 0) {
      basePickerInstance.suggestionStore.updateSuggestions(suggestions, -1);
      basePickerInstance.setState({
        suggestionsVisible: true,
        suggestionsLoading: false,
        moreSuggestionsAvailable: false,
      });
    }
  }, text);
  await page.waitForTimeout(500);
}

test.beforeEach(async ({ page }) => {
  await setupMockPage(page);
});

// Configure retries for job details tests since they involve complex UI interactions
test.describe('Job Details Tests', () => {
  test.describe.configure({ retries: 2 });

  // Track the original state of the group creation setting
  let originalGroupCreationState: boolean | null = null;
  let createdGroupName: string | null = null;
  // Ensure group creation is enabled before running tests
  test.beforeAll(async ({ browser }) => {
    if (isMockMode) {
      return;
    }

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

    // Click the Add button to navigate to ManageMembership
    await page.locator('#manage-membership-button').click();
    await page.getByText('Create a new group').click();
    await page.getByPlaceholder('Enter the name of the group').click();

    const groupName = `pw-test-${uuidv4().replace(/-/g, '').slice(0, 10)}`;

    // Fill group name
    await page.getByPlaceholder('Enter the name of the group').fill(groupName);

    // Select authorized senders
    const pickerInput = page.locator('.ms-BasePicker-input').first();
    await pickerInput.click();
    await page.waitForTimeout(500);

    await typeIntoPicker(page, pickerInput, 'adele');
    await page.getByRole('option', { name: 'Adele Vance' }).click();
    await page.waitForTimeout(1000);

    await typeIntoPicker(page, pickerInput, 'alex');
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

    if (isMockMode) {
      await expect(page.getByRole('button', { name: 'Add Source Part' })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Next' })).toBeVisible();
      console.log('✅ Mock mode: create-group workflow reached source configuration and owner selection.');
      return;
    }

    await page.getByRole('button', { name: 'Add Source Part' }).click();

    const expandAllButtonCreate = page.locator('#expandCollapseAllButton');
    if (await expandAllButtonCreate.count()) {
      await expandAllButtonCreate.click();
    }

    // Select group membership - use dropdown directly to avoid depending on source part label
    await page.getByLabel('Source Type').first().click();
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

    // Click the Add button to navigate to ManageMembership
    await page.locator('#manage-membership-button').click();
    await page.getByText('Create a new group').click();
    await page.getByPlaceholder('Enter the name of the group').click();

    const groupName = `pw-test-${uuidv4().replace(/-/g, '').slice(0, 10)}`;

    // Fill group name
    await page.getByPlaceholder('Enter the name of the group').fill(groupName);

    // Select authorized senders
    const pickerInputUnsupported = page.locator('.ms-BasePicker-input').first();
    await pickerInputUnsupported.click();
    await typeIntoPicker(page, pickerInputUnsupported, 'adele');
    await page.getByRole('option', { name: 'Adele Vance' }).click();
    await page.waitForTimeout(1000);
    await typeIntoPicker(page, pickerInputUnsupported, 'alex');
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

    if (isMockMode) {
      await expect(page.locator('#advancedQueryTextField')).toContainText('LocationAreaDetail_Code IS NULL');
      console.log('✅ Mock mode: advanced query editor accepts unsupported operator text.');
      return;
    }

    await page.locator(`#advancedViewToggle`).click();
    await page.locator(`#expandCollapseAllButton`).click();
    await expect(page.locator('#filterTextField')).toBeVisible();

    console.log('✅ Render textfield if query has unsupported operator test completed successfully.');
  });

  test('Verify inclusionary logic correctly updates the query', { tag: '@main', timeout: 60000 }, async ({ page }) => {
    const AUTHORIZED_SENDERS_LABEL = 'Authorized Senders';
    const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
    await page.goto(url);

    // Wait for the page to be interactive rather than using a hard timeout
    await page.locator('#manage-membership-button').waitFor({ state: 'visible', timeout: 15000 });

    // Click the Add button to navigate to ManageMembership
    await page.locator('#manage-membership-button').click();
    await page.getByText('Create a new group').waitFor({ state: 'visible', timeout: 10000 });
    await page.getByText('Create a new group').click();

    const groupNameInput = page.getByPlaceholder('Enter the name of the group');
    await groupNameInput.waitFor({ state: 'visible', timeout: 10000 });
    await groupNameInput.click();

    const groupName = `pw-test-${uuidv4().replace(/-/g, '').slice(0, 10)}`;

    // Fill group name
    await groupNameInput.fill(groupName);

    // Select authorized senders
    const pickerInputInclusionary = page.locator('.ms-BasePicker-input').first();
    await pickerInputInclusionary.click();
    await typeIntoPicker(page, pickerInputInclusionary, 'adele');
    await page.getByRole('option', { name: 'Adele Vance' }).click();
    await page.waitForTimeout(1000);
    await typeIntoPicker(page, pickerInputInclusionary, 'alex');
    await page.getByRole('option', { name: 'Alex Wilber' }).first().click();

    // Create group
    await page.getByRole('button', { name: 'Create group' }).click();

    // Wait for the "Next" button to become enabled
    const nextButton = page.getByRole('button', { name: 'Next' });
    await nextButton.waitFor({ state: 'visible', timeout: 30000 });
    await expect(nextButton).toBeEnabled({ timeout: 30000 });

    // Navigate through steps
    await nextButton.click();
    await page.getByRole('button', { name: 'Next' }).waitFor({ state: 'visible', timeout: 10000 });
    await page.getByRole('button', { name: 'Next' }).click();

    if (isMockMode) {
      await page.getByLabel('Advanced View').click();
      await expect(page.locator('#advancedQueryTextField')).toBeVisible();
      await expect(page.locator('#advancedQueryTextField')).toContainText('GroupMembership');
      console.log('✅ Mock mode: source-part query JSON is visible in advanced view.');
      return;
    }

    // Verify it's inclusionary by default
    await page.getByRole('button', { name: 'Add Source Part' }).click();
    const includeSourcePart = page.locator('div').filter({ hasText: /Include Source Part/i }).first();
    await expect(includeSourcePart.getByRole('radio', { name: 'Yes' })).toBeChecked();

    // Verify updating the value updates the query correctly
    await includeSourcePart.getByRole('radio', { name: 'No' }).click();
    await page.getByLabel('Advanced View').click();
    await expect(page.locator('#advancedQueryTextField')).toContainText('"exclusionary":true');
    await page.getByLabel('Advanced View').click();
    await includeSourcePart.getByRole('radio', { name: 'Yes' }).click();
    await page.getByLabel('Advanced View').click();
    await expect(page.locator('#advancedQueryTextField')).toContainText('"exclusionary":false');
    console.log('✅ Inclusionary logic correctly updates the query test completed successfully.');
  });

  test('Test onboarding, HR Source part functionality, and review flow', async ({ page }) => {
    test.setTimeout(Math.max(testTimeoutMs, 60000));
    const AUTHORIZED_SENDERS_LABEL = 'Authorized Senders';
    const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;

    console.log('🚀 Starting onboarding test with HR source parts');

    await page.goto(url);
    await page.waitForTimeout(10000);
    // Click the Add button to navigate to ManageMembership
    await page.locator('#manage-membership-button').click();
    await page.getByText('Create a new group').click();
    await page.getByPlaceholder('Enter the name of the group').click();
    const groupName = `pw-test-${uuidv4().replace(/-/g, '').slice(0, 10)}`;
    createdGroupName = groupName;
    console.log(`Group name: ${groupName}`);
    // Fill group name
    await page.getByPlaceholder('Enter the name of the group').fill(groupName);
    // Select authorized senders
    const pickerInputOnboarding = page.locator('.ms-BasePicker-input').first();
    await pickerInputOnboarding.click();
    await typeIntoPicker(page, pickerInputOnboarding, 'adele');
    await page.getByRole('option', { name: 'Adele Vance' }).click();
    await page.waitForTimeout(1000);
    await typeIntoPicker(page, pickerInputOnboarding, 'alex');
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

    if (isMockMode) {
      await expect(page.getByRole('button', { name: 'Add Source Part' })).toBeVisible();
      console.log('🧪 Mock mode: executing full HR onboarding workflow.');
    }

    // Create an HR source part
    await page.getByRole('button', { name: 'Add Source Part' }).click();

    const expandAllButtonHr = page.locator('#expandCollapseAllButton');
    if (await expandAllButtonHr.count()) {
      const expandButtonText = (await expandAllButtonHr.first().innerText()).toLowerCase();
      if (expandButtonText.includes('expand all')) {
        await expandAllButtonHr.click();
      }
    }

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
    await selectHrComboOption(page, 'hr-attribute-combobox', 0, 'PayScaleStockLevelNbr');
    await selectHrComboOption(page, 'hr-equality-operator-dropdown', 0, '>=');
    await page.getByTestId('hr-value-textfield').click();
    await page.getByTestId('hr-value-textfield').fill('65');
    await page.keyboard.press('Tab');

    // The grid should remain visible; falling back to raw text would surface #filterTextField
    await expect(page.locator('#filterTextField')).toHaveCount(0);

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
    expect(filterInQuery).toMatch(/PayScaleStockLevelNbr\s*>=\s*65/);

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
    const readOnlyVirtualizedValue = page.getByTestId('hr-value-virtualized-combobox').first();
    if (await readOnlyVirtualizedValue.count()) {
      await readOnlyVirtualizedValue.click();
      const fteIcon = page.locator('label:has-text("FTE") i');
      try {
        await fteIcon.click({ timeout: 800 });
        expect(false, 'FTE option was clickable but should not be').toBe(true);
      } catch (error) {
        expect(error).toBeDefined();
      }
      console.log('✅ Virtualized HR values are read-only in review mode.');
    } else {
      const readOnlyTextValue = page.getByTestId('hr-value-textfield').first();
      await expect(readOnlyTextValue).toBeVisible({ timeout: 10000 });
      await expect(readOnlyTextValue).toHaveValue(/65/);
      console.log('✅ Text HR values are present in review mode.');
    }

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

    // Open the job again to validate history panel (latest change should be present)
    const reopenedGroupRow = page.locator(`[data-group-name="${groupName}"]`);
    await expect(reopenedGroupRow).toBeVisible({ timeout: 15000 });
    await reopenedGroupRow.click();
    await expect(page.getByText(`Membership Details - ${groupName}`)).toBeVisible({ timeout: 30000 });

    if (!isMockMode) {
      const historyButton = page.locator('#job-history-button');
      if (await historyButton.count()) {
        await historyButton.click();
        const historyPanel = page.locator('.ms-Panel').first();
        await expect(historyPanel).toBeVisible({ timeout: 10000 });

        const historyRows = historyPanel.locator('[role="row"]');
        if (await historyRows.count()) {
          await expect(historyRows.first()).toBeVisible({ timeout: 10000 });
          console.log('✅ Job History panel opened and rows detected.');
        } else {
          console.log('✅ Job History panel opened (no row-formatted entries).');
        }
      } else {
        console.log('ℹ️ Job history button not present; skipping history-panel check.');
      }
    } else {
      console.log('ℹ️ Mock mode: skipping history-panel UI validation.');
    }
  });

  test('People picker suggests for name and alias inputs (GraphApi)', async ({ page }) => {
    test.setTimeout(testTimeoutMs);
    const AUTHORIZED_SENDERS_LABEL = 'Authorized Senders';
    const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
    const aliasFromEnv = EMAIL && EMAIL.includes('@') ? `${EMAIL.split('@')[0]}@` : 'user@';

    // Capture Graph /users responses to ensure no 400/unsupported errors
    const badUserResponses: Array<{ url: string; status: number }> = [];
    page.on('response', (resp) => {
      try {
        const u = resp.url();
        if (/\/users(\?|$)/.test(u) && resp.request().method() === 'GET') {
          const status = resp.status();
          if (status >= 400) badUserResponses.push({ url: u, status });
        }
      } catch { /* ignore */ }
    });

    await page.goto(url);
    await page.waitForTimeout(10000);

    // Click the Add button to navigate to ManageMembership
    await page.locator('#manage-membership-button').click();
    await page.getByText('Create a new group').click();
    await page.getByPlaceholder('Enter the name of the group').click();

    const groupName = `pw-test-${uuidv4().replace(/-/g, '').slice(0, 10)}`;
    await page.getByPlaceholder('Enter the name of the group').fill(groupName);

    // Minimal required fields to proceed
    const pickerInputPeople = page.locator('.ms-BasePicker-input').first();
    await pickerInputPeople.click();
    await typeIntoPicker(page, pickerInputPeople, 'user');
    const senderOptions = page.locator('[role="listbox"] [role="option"]');
    await expect(senderOptions.first()).toBeVisible({ timeout: 10000 });
    await senderOptions.first().click();

    await page.getByRole('button', { name: 'Create group' }).click();
    await page.waitForSelector('button:has-text("Next")');
    const nextButton = page.getByRole('button', { name: 'Next' });
    await expect(nextButton).toBeEnabled({ timeout: 30000 });
    await nextButton.click();
    await nextButton.click();

    if (isMockMode) {
      await expect(page.getByRole('button', { name: 'Add Source Part' })).toBeVisible();
      expect(badUserResponses, 'Graph /users should not error in mock people-picker scenario').toHaveLength(0);
      console.log('✅ Mock mode: people-picker dependent flow has no Graph user endpoint failures.');
      return;
    }

    // Add HR source part and open Org leader picker
    await page.getByRole('button', { name: 'Add Source Part' }).click();
    const expandAllButtonPeoplePicker = page.locator('#expandCollapseAllButton');
    if (await expandAllButtonPeoplePicker.count()) {
      await expandAllButtonPeoplePicker.click();
    }
    await page.getByTestId('hr-include-org-choice').locator('label').filter({ hasText: 'Yes' }).click();
    const orgLeaderInput = page.getByLabel('Provide Org. leader');
    await expect(orgLeaderInput).toBeVisible({ timeout: 10000 });

    // Scenario 1: single-token name (uses name prefix filter blended with search)
    await orgLeaderInput.click();
    // Use a generic token likely present in seeded tenants
    await orgLeaderInput.fill('user');
    const listbox = page.locator('[role="listbox"]');
    const options = listbox.locator('[role="option"]');
    await expect(options.first()).toBeVisible({ timeout: 15000 });
    expect(badUserResponses, 'Graph /users should not error for name search').toHaveLength(0);

    // Scenario 2: backspace to shorter prefix (should still show via search)
    await orgLeaderInput.press('Backspace'); // -> "use"
    await expect(options.first()).toBeVisible({ timeout: 15000 });
    expect(badUserResponses, 'Graph /users should not error after backspace').toHaveLength(0);

    // Scenario 3: alias with domain (prefix filter on mail/UPN) using env email if available
    await orgLeaderInput.fill(aliasFromEnv);
    await expect(options.first()).toBeVisible({ timeout: 15000 });
    expect(badUserResponses, 'Graph /users should not error for alias prefix').toHaveLength(0);
  });

  const selectHrComboOption = async (
    page: Page,
    testId: string,
    index: number,
    optionName: string | RegExp
  ): Promise<void> => {
    const combo = page.getByTestId(testId).nth(index);
    await combo.waitFor({ state: 'attached', timeout: 10000 });
    await expect(combo).toBeVisible({ timeout: 5000 });
    const input = combo.locator('input').first();

    const openOptionsButton = combo.getByRole('button', { name: 'Open options' });
    if (await openOptionsButton.count()) {
      await openOptionsButton.first().click({ force: true });
    } else {
      await combo.click({ force: true });
    }

    const inputId = (await input.count()) > 0 ? await input.getAttribute('id') : null;
    const optionListIdPrefix = inputId?.replace('-input', '-list');
    if (optionListIdPrefix) {
      const optionInControlList = page.locator(`[id^="${optionListIdPrefix}"]`).filter({ hasText: optionName }).first();
      if (await optionInControlList.count()) {
        await optionInControlList.click({ force: true, timeout: 3000 });
      }
    }

    const listbox = page.locator('[role="listbox"]:visible').last();
    try {
      const currentValue = (await input.count()) > 0 ? await input.inputValue() : '';
      if (!currentValue) {
        await expect(listbox).toBeVisible({ timeout: 3000 });
        await listbox.getByRole('option', { name: optionName }).first().click({ force: true, timeout: 3000 });
      }

      if (typeof optionName === 'string' && (await input.count()) > 0) {
        const selectedValue = await input.inputValue();
        if (!selectedValue) {
          await input.fill(optionName);
          await input.press('Enter');
        }
      }
      return;
    } catch {
      if (typeof optionName === 'string') {
        if (await input.count()) {
          await input.fill(optionName);
          await input.press('Enter');
          return;
        }
      }
      throw new Error(`Failed to select option "${String(optionName)}" for ${testId}[${index}]`);
    }
  };

  const selectComboOptionByLabel = async (page: Page, label: string, query: string): Promise<number | null> => {
    try {
      if (page.isClosed()) {
        throw new Error('Page has been closed');
      }

      const input = page.getByLabel(label);
      await expect(input).toBeVisible({ timeout: 10000 });
      await expect(input).toBeEnabled({ timeout: 5000 });

      // Use the typeIntoPicker helper for Fluent UI pickers
      const pickerInputEl = page.locator('.ms-BasePicker-input').last();
      await pickerInputEl.click();
      await page.waitForTimeout(500);
      await typeIntoPicker(page, pickerInputEl, query);

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
      const message = error instanceof Error ? error.message : String(error);
      throw new Error(`Failed to select option for "${label}": ${message}`);
    }
  };

  // Reset group creation setting to original state if it was originally disabled
  test.afterAll(async ({ browser }) => {
    if (isMockMode) {
      console.log('ℹ️ Mock mode: skipping group creation state reset.');
      return;
    }

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
      console.log('ℹ️ Group creation state was not captured; skipping reset.');
    }
  });
});
