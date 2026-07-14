// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect, Page } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || 'http://localhost:3000';
const testTimeoutMs = Number(process.env.PLAYWRIGHT_TEST_TIMEOUT_MS ?? 60000);

type SpotCheckPart = {
  index: number;
  type: string;
  supported: boolean;
  exclusionary: boolean;
  included: boolean | null;
};

type SpotCheckResult = {
  accountEnabled: boolean;
  hasUnsupportedParts: boolean;
  parts: SpotCheckPart[];
};

/**
 * Types into a Fluent UI v8 NormalPeoplePicker and triggers suggestion resolution.
 *
 * Playwright's standard input methods don't reliably fire React 18's synthetic
 * onInput for Fluent UI's Autofill, so we drive the component instances directly
 * via the fiber tree (same approach used by jobDetails.spec.ts).
 */
async function typeIntoPicker(page: Page, pickerInputLocator: ReturnType<Page['locator']>, text: string) {
  await pickerInputLocator.focus();
  await pickerInputLocator.evaluate(async (el: HTMLInputElement, val: string) => {
    el.focus();
    const nativeInputValueSetter = Object.getOwnPropertyDescriptor(
      window.HTMLInputElement.prototype,
      'value'
    )!.set!;
    nativeInputValueSetter.call(el, val);

    const fiberKey = Object.keys(el).find((k) => k.startsWith('__reactFiber$'));
    if (!fiberKey) throw new Error('React fiber not found on picker input');
    let fiber = (el as unknown as Record<string, unknown>)[fiberKey];

    let autofillInstance: any = null;
    let basePickerInstance: any = null;
    let current: any = fiber;
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
    await new Promise((r) => setTimeout(r, 50));

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

/**
 * Routes the spotCheck endpoint to a fixed payload (or status code).
 * Registered inside each test so it takes precedence over the default mock.
 */
async function overrideSpotCheckResponse(
  page: Page,
  options: { result?: SpotCheckResult; status?: number }
) {
  await page.route(/\/api\/v1\/spotCheck\/job\/[^/]+\/user\/[^/?]+/, async (route) => {
    if (options.status && options.status !== 200) {
      await route.fulfill({ status: options.status, contentType: 'application/json', body: '{}' });
      return;
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(options.result ?? {}),
    });
  });
}

async function openMembershipDetails(page: Page) {
  const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
  await page.goto(url);

  const jobRow = page.getByTestId('job-row-mockjob002');
  await expect(jobRow).toBeVisible({ timeout: 15000 });
  await jobRow.click();

  await expect(page.getByText('Membership Details')).toBeVisible({ timeout: 15000 });
  await expect(page.getByText('Engineering-All').first()).toBeVisible({ timeout: 15000 });
}

/**
 * Opens the job, types a user into the spot-check picker, selects the suggestion,
 * and waits for the spotCheck API call to complete.
 */
async function spotCheckMockUser(page: Page) {
  await openMembershipDetails(page);

  const spotCheckTitle = page.getByText('Spot-check a user');
  await expect(spotCheckTitle).toBeVisible({ timeout: 10000 });

  const pickerInput = page.locator('#userSpotCheckPicker');
  await expect(pickerInput).toBeVisible({ timeout: 10000 });

  await typeIntoPicker(page, pickerInput, 'Mock User');

  const suggestion = page.locator('.ms-Suggestions-itemButton').filter({ hasText: 'Mock User' }).first();
  await expect(suggestion).toBeVisible({ timeout: 10000 });

  const spotCheckResponsePromise = page.waitForResponse(
    (resp) => /\/api\/v1\/spotCheck\/job\/.+\/user\/.+/.test(resp.url()) && resp.request().method() === 'GET'
  );

  await suggestion.click();

  await spotCheckResponsePromise;
  await page.waitForTimeout(500);
}

test.beforeEach(async ({ page }) => {
  await setupMockPage(page);
});

test.describe('User Spot Check', () => {
  test.describe.configure({ retries: 2 });

  test('spot-check box is visible for a submission reviewer', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await openMembershipDetails(page);

    await expect(page.getByText('Spot-check a user')).toBeVisible({ timeout: 10000 });
    await expect(
      page.getByText('Search for a user to see whether they are included by each rule.', { exact: false })
    ).toBeVisible();
    await expect(page.locator('#userSpotCheckPicker')).toBeVisible();
  });

  test('shows "included" for a non-exclusionary part the user matches', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await overrideSpotCheckResponse(page, {
      result: {
        accountEnabled: true,
        hasUnsupportedParts: false,
        parts: [{ index: 0, type: 'GroupMembership', supported: true, exclusionary: false, included: true }],
      },
    });

    await spotCheckMockUser(page);

    await expect(page.getByText('included', { exact: true })).toBeVisible({ timeout: 5000 });
  });

  test('shows "not included" for a non-exclusionary part the user does not match', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await overrideSpotCheckResponse(page, {
      result: {
        accountEnabled: true,
        hasUnsupportedParts: false,
        parts: [{ index: 0, type: 'GroupMembership', supported: true, exclusionary: false, included: false }],
      },
    });

    await spotCheckMockUser(page);

    await expect(page.getByText('not included', { exact: true })).toBeVisible({ timeout: 5000 });
  });

  test('shows "exclusionary, included" for an exclusionary part the user matches', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await overrideSpotCheckResponse(page, {
      result: {
        accountEnabled: true,
        hasUnsupportedParts: false,
        parts: [{ index: 0, type: 'GroupMembership', supported: true, exclusionary: true, included: true }],
      },
    });

    await spotCheckMockUser(page);

    await expect(page.getByText('exclusionary, included', { exact: true })).toBeVisible({ timeout: 5000 });
  });

  test('shows "exclusionary, not included" for an exclusionary part the user does not match', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await overrideSpotCheckResponse(page, {
      result: {
        accountEnabled: true,
        hasUnsupportedParts: false,
        parts: [{ index: 0, type: 'GroupMembership', supported: true, exclusionary: true, included: false }],
      },
    });

    await spotCheckMockUser(page);

    await expect(page.getByText('exclusionary, not included', { exact: true })).toBeVisible({ timeout: 5000 });
  });

  test('shows "not supported" and a banner for unsupported parts', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await overrideSpotCheckResponse(page, {
      result: {
        accountEnabled: true,
        hasUnsupportedParts: true,
        parts: [{ index: 0, type: 'PlaceMembership', supported: false, exclusionary: false, included: null }],
      },
    });

    await spotCheckMockUser(page);

    await expect(page.getByText('not supported', { exact: true })).toBeVisible({ timeout: 5000 });
    await expect(
      page.getByText('This query contains rule types that can\'t be checked.', { exact: false })
    ).toBeVisible();
  });

  test('shows "unable to determine" when membership could not be evaluated', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await overrideSpotCheckResponse(page, {
      result: {
        accountEnabled: true,
        hasUnsupportedParts: false,
        parts: [{ index: 0, type: 'GroupMembership', supported: true, exclusionary: false, included: null }],
      },
    });

    await spotCheckMockUser(page);

    await expect(page.getByText('unable to determine', { exact: true })).toBeVisible({ timeout: 5000 });
  });

  test('shows account-disabled message and no parts when the account is disabled', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await overrideSpotCheckResponse(page, {
      result: { accountEnabled: false, hasUnsupportedParts: false, parts: [] },
    });

    await spotCheckMockUser(page);

    await expect(page.getByText('This user\'s account is currently disabled')).toBeVisible({ timeout: 5000 });
  });

  test('shows an error message when the spot-check request fails', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await overrideSpotCheckResponse(page, { status: 500 });

    await spotCheckMockUser(page);

    await expect(
      page.getByText('Something went wrong while checking this user. Please try again.')
    ).toBeVisible({ timeout: 5000 });
  });

  test('shows an error message when the user is not found (404)', async ({ page }) => {
    test.setTimeout(testTimeoutMs);

    await overrideSpotCheckResponse(page, { status: 404 });

    await spotCheckMockUser(page);

    await expect(
      page.getByText('Something went wrong while checking this user. Please try again.')
    ).toBeVisible({ timeout: 5000 });
  });
});
