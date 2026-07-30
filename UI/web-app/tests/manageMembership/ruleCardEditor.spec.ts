// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect, Page } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';

const rulesDescription =
  "These rules define who's included in the membership. Select a rule card to review its configuration and details.";
const emptyTitle = 'No rules yet';
const emptyDescription = "Add a rule to define who's included in this membership.";

// Mirrors the picker interaction used in copilot.spec.ts to drive the Fluent UI
// people picker through its internal React fiber (mock mode has no real network).
async function typeIntoPicker(page: Page, pickerInputLocator: any, text: string) {
  await pickerInputLocator.focus();
  await pickerInputLocator.evaluate(async (el: HTMLInputElement, val: string) => {
    el.focus();
    const nativeInputValueSetter = Object.getOwnPropertyDescriptor(
      window.HTMLInputElement.prototype,
      'value'
    )!.set!;
    nativeInputValueSetter.call(el, val);

    const fiberKey = Object.keys(el).find((key) => key.startsWith('__reactFiber$'));
    if (!fiberKey) {
      throw new Error('React fiber not found on picker input');
    }

    let autofillInstance: any = null;
    let basePickerInstance: any = null;
    let current = (el as any)[fiberKey];

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
    await new Promise((resolve) => setTimeout(resolve, 50));

    const suggestions = await basePickerInstance.props.onResolveSuggestions(
      val,
      basePickerInstance.state.items || []
    );

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

async function navigateToMembershipConfiguration(page: Page) {
  await page.getByText('Create a new group').click();

  const groupNameInput = page.getByPlaceholder('Enter the name of the group');
  await groupNameInput.click();
  await groupNameInput.fill(`pw-rulecard-${Date.now()}`);

  const pickerInput = page.locator('.ms-BasePicker-input').first();
  await pickerInput.click();
  await page.waitForTimeout(500);

  await typeIntoPicker(page, pickerInput, 'adele');
  await page.getByRole('option', { name: 'Adele Vance' }).click();
  await page.waitForTimeout(500);

  await typeIntoPicker(page, pickerInput, 'alex');
  await page.getByRole('option', { name: 'Alex Wilber' }).first().click();

  await page.getByRole('button', { name: 'Create group' }).click();
  await page.waitForSelector('button:has-text("Next")');

  const nextButton = page.getByRole('button', { name: 'Next' });
  await expect(nextButton).toBeEnabled({ timeout: 30000 });
  await nextButton.click();

  // The Copilot button only renders once Step 2 (Membership Configuration) is shown.
  await expect(page.getByRole('button', { name: /GMM Copilot/i })).toBeVisible();
}

// Locators scoped to the rule card carousel actions.
const deleteButtons = (page: Page) => page.getByRole('button', { name: /Delete rule:/ });
const duplicateButtons = (page: Page) => page.getByRole('button', { name: /Duplicate rule:/ });

async function addFirstRule(page: Page) {
  // When there are no rules, both the header and the empty-state expose an "Add"
  // button; either one adds a rule. Use the header button (always present).
  await page.getByRole('button', { name: 'Add' }).first().click();
  await expect(deleteButtons(page)).toHaveCount(1);
}

test.beforeEach(async ({ page }) => {
  await setupMockPage(page);
  await page.goto('/ManageMembership');
  await page.waitForTimeout(1000);
  await navigateToMembershipConfiguration(page);
});

test('shows the empty state when a new membership has no rules', async ({ page }) => {
  await expect(page.getByText(rulesDescription)).toBeVisible();
  await expect(page.getByText(emptyTitle)).toBeVisible();
  await expect(page.getByText(emptyDescription)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Add' }).first()).toBeVisible();
  await expect(deleteButtons(page)).toHaveCount(0);
});

test('adding a rule replaces the empty state with a rule card and editor', async ({ page }) => {
  await addFirstRule(page);

  // Empty state is gone once a rule exists.
  await expect(page.getByText(emptyTitle)).toHaveCount(0);
  // The card exposes per-card duplicate/delete actions.
  await expect(duplicateButtons(page)).toHaveCount(1);
  await expect(deleteButtons(page)).toHaveCount(1);
});

test('duplicating a rule card adds a new card', async ({ page }) => {
  await addFirstRule(page);

  await duplicateButtons(page).first().click();

  await expect(deleteButtons(page)).toHaveCount(2);
  await expect(duplicateButtons(page)).toHaveCount(2);
});

test('deleting a rule card removes it', async ({ page }) => {
  await addFirstRule(page);
  await duplicateButtons(page).first().click();
  await expect(deleteButtons(page)).toHaveCount(2);

  await deleteButtons(page).first().click();

  await expect(deleteButtons(page)).toHaveCount(1);
});

test('deleting the last rule card returns to the empty state', async ({ page }) => {
  await addFirstRule(page);

  await deleteButtons(page).first().click();

  await expect(deleteButtons(page)).toHaveCount(0);
  await expect(page.getByText(emptyTitle)).toBeVisible();
});
