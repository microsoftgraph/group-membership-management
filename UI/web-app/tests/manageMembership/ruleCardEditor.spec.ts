// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect, Page } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';

const rulesDescription =
  "These rules define who's included in the membership. Select a rule card to review its configuration and details.";
const emptyTitle = 'No rules yet';
const emptyDescription = "Add a rule to define who's included in this membership.";

async function navigateToMembershipConfiguration(page: Page) {
  await page.getByText('Create a new group').click();
  await page.getByRole('button', { name: 'Create M365 Group', exact: true }).click();

  const groupNameInput = page.getByPlaceholder('Enter the name of the group');
  await groupNameInput.click();
  await groupNameInput.fill(`pw-rulecard-${Date.now()}`);

  await page.getByRole('button', { name: 'Create group' }).click();
  await page.waitForSelector('button:has-text("Next")');

  const nextButton = page.getByRole('button', { name: 'Next' });
  await expect(nextButton).toBeEnabled({ timeout: 30000 });
  await nextButton.click();

  // The rules header only renders once Step 2 (Membership Configuration) is shown.
  await expect(page.getByText(rulesDescription)).toBeVisible({ timeout: 30000 });
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
  // These tests cover the rule-card editor itself; Copilot's onboarding choices replace the rules header and empty state.
  await page.evaluate(async () => {
    const response = await fetch('/api/v1/settings/IsAICopilotEnabled', {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify('false'),
    });
    if (!response.ok) throw new Error(`Mock settings update failed: ${response.status}`);
  });
  await page.reload();
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
