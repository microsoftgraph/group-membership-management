// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect, Page } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';

const welcomeMessage = "Hi, I'm GMM Copilot.";
const assistantResponse = 'Here is a filter for FTEs in your department.';

async function navigateToMembershipConfiguration(page: Page) {
  await page.getByText('Create a new group').click();
  await page.getByRole('button', { name: 'Create M365 Group', exact: true }).click();

  const groupNameInput = page.getByPlaceholder('Enter the name of the group');
  await groupNameInput.click();
  await groupNameInput.fill(`pw-copilot-${Date.now()}`);

  await page.getByRole('button', { name: 'Create group' }).click();
  await page.getByRole('dialog', { name: 'Advanced settings (Optional)', exact: true })
    .getByRole('button', { name: 'Not now', exact: true }).click();
  await page.waitForSelector('button:has-text("Next")');

  const nextButton = page.getByRole('button', { name: 'Next' });
  await expect(nextButton).toBeEnabled({ timeout: 30000 });
  await nextButton.click();

  await expect(page.getByTestId('copilot-trigger-button')).toBeVisible();
}

test.beforeEach(async ({ page }) => {
  await setupMockPage(page);
  await page.goto('/ManageMembership');
  await page.waitForTimeout(1000);
  await navigateToMembershipConfiguration(page);
});

test('Copilot button is visible on the ManageMembership page', async ({ page }) => {
  await expect(page.getByTestId('copilot-trigger-button')).toBeVisible();
  await expect(page.getByTestId('copilot-onboarding-choices')).toBeVisible();
  await expect(page.getByRole('region', { name: 'Add a rule' })).toBeVisible();
});

test('onboarding choices lead into chat, stay usable on narrow screens, and yield to manual editing', async ({ page }) => {
  const choices = page.getByTestId('copilot-onboarding-choices');
  const input = page.getByPlaceholder('Ask a question or describe the membership you want');
  await page.getByTestId('copilot-trigger-button').click();
  await expect(page.getByTestId('copilot-onboarding-welcome')).toBeVisible();
  await expect(page.getByTestId('copilot-disclaimer')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Send', exact: true })).toBeDisabled();
  await page.setViewportSize({ width: 390, height: 600 });
  await expect(input).toBeInViewport();
  await input.fill('Show me FTEs');
  await input.press('Enter');
  await expect(page.getByText(assistantResponse)).toBeVisible();
  await expect(input).toBeInViewport();
  await page.getByRole('button', { name: 'New chat', exact: true }).click();
  await expect(page.getByText(assistantResponse)).toHaveCount(0);
  await expect(page.getByTestId('copilot-onboarding-welcome')).toBeVisible();
  await page.getByRole('button', { name: 'Close', exact: true }).click();
  await expect(choices).toBeVisible();
  await page.getByTestId('add-rule-button').click();
  await expect(choices).toHaveCount(0);
  await expect(page.getByTestId('copilot-trigger-button')).toBeVisible();
});

test('clicking the Copilot button opens the panel with welcome message', async ({ page }) => {
  await page.getByTestId('copilot-trigger-button').click();

  await expect(page.getByText(welcomeMessage)).toBeVisible();
});

test('can type a message and send it, then see assistant response', async ({ page }) => {
  await page.getByTestId('copilot-trigger-button').click();

  const input = page.getByPlaceholder('Ask a question or describe the membership you want');
  await input.fill('Show me FTEs in my department');
  await input.press('Enter');

  await expect(page.getByText(assistantResponse)).toBeVisible();
});

test('panel can be closed with the close button', async ({ page }) => {
  await page.getByTestId('copilot-trigger-button').click();
  await expect(page.getByText(welcomeMessage)).toBeVisible();

  await page.getByRole('button', { name: 'Close' }).click();

  await expect(page.getByText(welcomeMessage)).toHaveCount(0);
});

test('suggested prompt buttons are visible when configured', async ({ page }) => {
  await page.getByTestId('copilot-trigger-button').click();
  await expect(page.getByText(welcomeMessage)).toBeVisible();

  await expect(page.getByText('TRY ONE OF THESE TO GET STARTED')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Include all reports who roll up to an employee' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Include members of a group' })).toBeVisible();
});

test('clicking a suggested prompt sends it as a message', async ({ page }) => {
  await page.getByTestId('copilot-trigger-button').click();
  await expect(page.getByText(welcomeMessage)).toBeVisible();

  await page.getByRole('button', { name: 'Include all reports who roll up to an employee' }).click();
  await expect(page.getByRole('button', { name: 'Include all reports who roll up to an employee' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Include members of a group' })).toHaveCount(0);

  // Verify the prompt label appears as the user message bubble
  await expect(page.getByText('Include all reports who roll up to an employee').last()).toBeVisible();
  // Verify the assistant responded
  await expect(page.getByText(assistantResponse)).toBeVisible();
});

test('reopening a chat before apply offers inline Continue and New chat', async ({ page }) => {
  const trigger = page.getByTestId('copilot-trigger-button');
  await trigger.click();
  const input = page.getByPlaceholder('Ask a question or describe the membership you want');
  await input.fill('Show me FTEs');
  await input.press('Enter');
  await expect(page.getByText(assistantResponse)).toBeVisible();
  await page.getByRole('button', { name: 'Close', exact: true }).click();
  await trigger.click();
  const resume = page.getByTestId('copilot-update-resume');
  await expect(resume).toBeVisible();
  await expect(page.getByRole('alertdialog')).toHaveCount(0);
  await resume.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(input).toBeFocused();
  await expect(page.getByText(assistantResponse)).toBeVisible();
  await page.getByRole('button', { name: 'Close', exact: true }).click();
  await trigger.click();
  await resume.getByRole('button', { name: 'New chat', exact: true }).click();
  await expect(page.getByText(assistantResponse)).toHaveCount(0);
  await expect(page.getByText(welcomeMessage)).toBeVisible();
});

test('reopening an applied chat shows the accepted summary and reuses it without a new description request', async ({ page }) => {
  const message = 'Ready.\n\n**Proposed Membership:** Includes **full-time employees**.\nAll departments are included.\n\nClick **Apply & Review** to review.';
  let descriptionRequests = 0;
  await page.route(/\/api\/v1\/(?:copilot|Copilot\/chat)$/, route => {
    if (route.request().postDataJSON().message.startsWith('What is my current membership?')) descriptionRequests++;
    return route.fulfill({
      json: {
        message,
        resultingQuery: [{
          partId: 'copilot-part-1',
          filter: "EmployeeType_Code = 'FTE'",
          title: 'FTEs',
          isExclusion: false,
          useOrgStructure: false,
        }],
        appliedOperations: [{ op: 'add', partId: 'copilot-part-1' }],
      },
    });
  });
  const trigger = page.getByTestId('copilot-trigger-button');
  await trigger.click();
  const input = page.getByPlaceholder('Ask a question or describe the membership you want');
  await input.fill('Include full-time employees');
  await input.press('Enter');
  await page.getByRole('button', { name: 'Apply & Review', exact: true }).click();
  await expect(input).toHaveCount(0);
  await trigger.click();
  const resume = page.getByTestId('copilot-update-resume');
  await expect(page.getByTestId('copilot-apply-action')).toHaveCount(1);
  const summary = resume.getByTestId('copilot-applied-summary');
  await expect(summary).toContainText('All departments are included.');
  await expect(summary).not.toContainText('Click Apply & Review');
  await resume.getByRole('button', { name: 'New chat', exact: true }).click();
  await expect(page.getByTestId('copilot-current-membership')).toContainText('All departments are included.');
  await expect(page.getByRole('button', { name: 'Apply & Review', exact: true })).toHaveCount(0);
  expect(descriptionRequests).toBe(0);
});