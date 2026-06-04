// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect, Page } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';

const welcomeMessage = "Hi, I'm GMM Copilot.";
const assistantResponse = 'Here is a filter for FTEs in your department.';

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
  await groupNameInput.fill(`pw-copilot-${Date.now()}`);

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

  // Wait for the wizard to advance and the next step to render before clicking Next again
  await page.getByRole('button', { name: 'Next' }).waitFor({ state: 'visible', timeout: 10000 });
  await expect(page.getByRole('button', { name: 'Next' })).toBeEnabled({ timeout: 10000 });
  await page.getByRole('button', { name: 'Next' }).click();

  await expect(page.getByRole('button', { name: /GMM Copilot/i })).toBeVisible();
}

test.beforeEach(async ({ page }) => {
  await setupMockPage(page);
  await page.goto('/ManageMembership');
  await page.waitForTimeout(1000);
  await navigateToMembershipConfiguration(page);
});

test('Copilot button is visible on the ManageMembership page', async ({ page }) => {
  await expect(page.getByRole('button', { name: /GMM Copilot/i })).toBeVisible();
});

test('clicking the Copilot button opens the panel with welcome message', async ({ page }) => {
  await page.getByRole('button', { name: /GMM Copilot/i }).click();

  await expect(page.getByText(welcomeMessage)).toBeVisible();
});

test('can type a message and send it, then see assistant response', async ({ page }) => {
  await page.getByRole('button', { name: /GMM Copilot/i }).click();

  const input = page.getByPlaceholder('Ask a question or describe the membership you want');
  await input.fill('Show me FTEs in my department');
  await input.press('Enter');

  await expect(page.getByText(assistantResponse)).toBeVisible();
});

test('panel can be closed with the close button', async ({ page }) => {
  await page.getByRole('button', { name: /GMM Copilot/i }).click();
  await expect(page.getByText(welcomeMessage)).toBeVisible();

  await page.getByRole('button', { name: 'Close' }).click();

  await expect(page.getByText(welcomeMessage)).toHaveCount(0);
});

test('suggested prompt buttons are visible when configured', async ({ page }) => {
  await page.getByRole('button', { name: /GMM Copilot/i }).click();
  await expect(page.getByText(welcomeMessage)).toBeVisible();

  await expect(page.getByText('TRY ONE OF THESE TO GET STARTED')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Include all reports who roll up to an employee' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Include members of a group' })).toBeVisible();
});

test('clicking a suggested prompt sends it as a message', async ({ page }) => {
  await page.getByRole('button', { name: /GMM Copilot/i }).click();
  await expect(page.getByText(welcomeMessage)).toBeVisible();

  await page.getByRole('button', { name: 'Include all reports who roll up to an employee' }).click();

  // Verify the prompt label appears as the user message bubble
  await expect(page.getByText('Include all reports who roll up to an employee').last()).toBeVisible();
  // Verify the assistant responded
  await expect(page.getByText(assistantResponse)).toBeVisible();
});
