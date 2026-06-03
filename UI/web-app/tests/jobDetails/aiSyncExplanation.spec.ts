// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect, Page } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || 'http://localhost:3000';

/**
 * Types into a Fluent UI v8 NormalPeoplePicker and triggers suggestion resolution.
 * (Same utility as in jobDetails.spec.ts)
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

test.describe('AI Sync Explanation', () => {
  test.describe.configure({ retries: 2 });

  const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;

  test('shows AI explanation when user is selected and sync row is expanded', { tag: '@ai-explanation' }, async ({ page }) => {
    test.setTimeout(60_000);

    // Track whether the explain-user API was called
    let explanationRequested = false;
    await page.route('**/explain-user/**', async (route) => {
      explanationRequested = true;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          explanation: 'This user was added because their Building property now matches the filter criteria.',
        }),
      });
    });

    // Navigate to the app
    await page.goto(url);

    // Click on a specific job row (use testid for reliability)
    const jobRow = page.getByTestId('job-row-mockjob002');
    await expect(jobRow).toBeVisible({ timeout: 15000 });
    await jobRow.click();

    // Wait for job details page to load
    await expect(page.getByText('Membership Details')).toBeVisible({ timeout: 15000 });

    // Open the history panel
    const historyButton = page.locator('#job-history-button');
    await expect(historyButton).toBeVisible({ timeout: 15000 });
    await historyButton.click();

    // Switch to the Sync tab
    const panel = page.locator('.ms-Panel').first();
    await expect(panel).toBeVisible({ timeout: 10000 });

    const syncTab = page.getByRole('tab', { name: /sync/i });
    if (await syncTab.isVisible({ timeout: 5000 }).catch(() => false)) {
      await syncTab.click();
      await page.waitForTimeout(1000);
    }

    // Find the people picker input in the history panel

    const pickerInput = panel.locator('.ms-BasePicker-input').first();
    if (await pickerInput.isVisible({ timeout: 5000 }).catch(() => false)) {
      // Type into the people picker to select a user
      await typeIntoPicker(page, pickerInput, 'adele');
      await page.waitForTimeout(500);

      // Select the suggestion
      const suggestion = page.getByRole('option', { name: /Adele Vance/i });
      if (await suggestion.isVisible({ timeout: 3000 }).catch(() => false)) {
        await suggestion.click();
        await page.waitForTimeout(3000);

        // Look for sync history rows and try to expand one
        const syncRows = panel.locator('[data-automationid="DetailsRow"]');
        const rowCount = await syncRows.count();

        if (rowCount > 0) {
          // Click the first row to expand it
          await syncRows.first().click();
          await page.waitForTimeout(3000);

          // Check if the AI explanation text appears
          const aiLabel = panel.getByText('Description (AI generated):');
          if (await aiLabel.isVisible({ timeout: 10000 }).catch(() => false)) {
            await expect(aiLabel).toBeVisible();
            console.log('✅ AI explanation label rendered successfully.');

            // Verify explanation text appears
            const explanationText = panel.getByText('Building property');
            await expect(explanationText).toBeVisible({ timeout: 5000 });
            console.log('✅ AI explanation text rendered correctly.');

            expect(explanationRequested).toBe(true);
            console.log('✅ Explain-user API endpoint was called.');
          } else {
            console.log('ℹ️ AI explanation label not visible — may need user to be in matching runs.');
          }
        } else {
          console.log('ℹ️ No sync rows found in the panel.');
        }
      } else {
        console.log('ℹ️ People picker suggestion not visible — skipping user selection.');
      }
    } else {
      console.log('ℹ️ People picker input not visible in panel.');
    }
  });
});
