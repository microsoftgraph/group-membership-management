// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { expect, test } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || 'http://localhost:3000';

test.beforeEach(async ({ page }) => {
  await setupMockPage(page);
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
  await expect(page.getByRole('button', { name: 'Expand', exact: true })).toBeVisible();
});
