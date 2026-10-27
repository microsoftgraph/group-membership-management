// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { expect, test } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || 'http://localhost:3000';

test.beforeEach(async ({ page }) => {
  await setupMockPage(page);
});

test('preserves the generated alias and locks group creation controls while the request is pending', async ({ page }) => {
  let releaseResponse!: () => void;
  const responseGate = new Promise<void>(resolve => { releaseResponse = resolve; });
  await page.route('**/api/v1/destinations/groups', async route => {
    await responseGate;
    await route.fallback();
  });

  await page.goto(`${DOMAIN}/ManageMembership`);
  await page.getByRole('radio', { name: 'Create a new group', exact: true }).check();
  await page.getByRole('button', { name: 'Create M365 Group', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'New Microsoft 365 group', exact: true });
  const groupName = page.getByPlaceholder('Enter the name of the group');
  const groupAlias = page.getByPlaceholder('Enter the alias of the group');
  await groupName.fill('Finance+Ops');
  await expect(groupAlias).toHaveValue('Finance+Ops');
  const behaviorPreview = page.getByRole('checkbox', { name: 'Show group mailbox and calendar in Outlook', exact: true });
  await behaviorPreview.press('Space');
  await expect(behaviorPreview).not.toBeChecked();

  const requestPromise = page.waitForRequest(request =>
    request.method() === 'POST' && request.url().endsWith('/api/v1/destinations/groups')
  );
  await page.getByRole('button', { name: 'Create group', exact: true }).click();
  const request = await requestPromise;
  expect(request.postDataJSON()).toEqual({
    groupName: 'Finance+Ops',
    groupAlias: 'Finance+Ops',
    groupType: 0,
    description: '',
  });

  await expect(groupName).toBeDisabled();
  await expect(groupAlias).toBeDisabled();
  await expect(behaviorPreview).toBeDisabled();
  await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Creating...', exact: true })).toBeDisabled();

  releaseResponse();
  await expect(dialog).toHaveCount(0);
  await page.getByRole('dialog', { name: 'Advanced settings (Optional)', exact: true }).getByRole('button', { name: 'Not now', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Preview-only selections were not saved or applied.' })).toBeVisible();
  await expect(page.getByRole('button', { name: /^Next/ })).toBeEnabled();
});

test('restores an existing job query after refreshing ManageMembership', async ({ page }) => {
  await page.goto(`${DOMAIN}/ManageMembership/mockjob002`);
  await page.evaluate(() => {
    const currentState = window.history.state ?? {};
    window.history.replaceState(
      {
        ...currentState,
        usr: {
          ...currentState.usr,
          currentStep: 1,
          jobId: 'mockjob002',
        },
      },
      '',
      window.location.href
    );
  });
  await page.reload();

  await expect(page.getByText('Exclude All Users in Marketing', { exact: false })).toBeVisible();
  // The restored query must hydrate into an interactive rule card, not just render
  // as text. This previously asserted the source-part "Expand" affordance, which the
  // rule card carousel replaced.
  await expect(
    page.getByRole('button', { name: /Select rule:.*Exclude All Users in Marketing/ })
  ).toBeVisible();
});

test('applies a Copilot proposal to an existing membership without saving', async ({ page }) => {
  await page.goto(`${DOMAIN}/ManageMembership/mockjob002`);
  await page.evaluate(() => {
    const currentState = window.history.state ?? {};
    window.history.replaceState(
      { ...currentState, usr: { ...currentState.usr, currentStep: 1, jobId: 'mockjob002' } },
      '',
      window.location.href
    );
  });
  await page.reload();

  await page.route(/\/api\/v1\/(?:copilot|Copilot\/chat)$/, route => route.fulfill({
    json: route.request().postDataJSON().message.startsWith('What is my current membership?')
      ? { message: 'Includes all full-time employees.', resultingQuery: [], appliedOperations: [] }
      : {
        message: '**Proposed Membership:** **Includes full-time employees in the US.**',
        resultingQuery: [{
          partId: 'layout-part', sourceType: 'SqlMembership', title: 'US FTEs',
          filter: "Country = 'US'", isExclusion: false, useOrgStructure: false,
        }],
        appliedOperations: [{ op: 'set' }],
      },
  }));
  const trigger = page.getByRole('button', { name: 'Let Copilot resume building it for you' });
  await trigger.click();
  await expect(page.getByTestId('copilot-update-resume')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Continue', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'New chat', exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Continue', exact: true }).click();
  const input = page.getByPlaceholder('Ask a question or describe the membership you want');
  await input.fill('Filter that to the US');
  await input.press('Enter');
  await page.getByRole('button', { name: 'Apply & Review', exact: true }).click();
  await expect(input).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Select rule:.*US FTEs/ })).toBeVisible();

  await trigger.click();
  await expect(page.getByTestId('copilot-update-resume')).toContainText('Includes full-time employees in the US.');
});