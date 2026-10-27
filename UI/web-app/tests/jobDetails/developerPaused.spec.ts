// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect } from '@playwright/test';
import { SettingKey } from '../../src/models';
import { setupMockPage } from '../mocks/setupMockPage';

test.describe('Job status edit permissions', () => {
  test.skip(process.env.PLAYWRIGHT_USE_MOCK_API === 'false', 'Requires mock roles and job state.');
  const jobId = 'a1b2c3d4-1234-4567-89ab-0123456789ab';
  const statusLabels: Record<string, string> = {
    DeveloperPaused: 'Disabled - Developer Paused',
    ThresholdExceeded: 'Disabled - Threshold Exceeded',
    CustomerPaused: 'Disabled',
  };

  for (const scenario of [
    { name: 'owner writer', isJobOwnerWriter: true, isJobOwnerEnabler: false, isSubmissionReviewer: false, isJobTenantWriter: false, status: 'DeveloperPaused', canToggle: false, canEdit: false },
    { name: 'owner writer with uppercase job URL', isJobOwnerWriter: true, isJobOwnerEnabler: false, isSubmissionReviewer: false, isJobTenantWriter: false, status: 'DeveloperPaused', canToggle: false, canEdit: false, routeJobId: jobId.toUpperCase() },
    { name: 'owner enabler', isJobOwnerWriter: false, isJobOwnerEnabler: true, isSubmissionReviewer: false, isJobTenantWriter: false, status: 'DeveloperPaused', canToggle: false, canEdit: false },
    { name: 'owner writer and reviewer', isJobOwnerWriter: true, isJobOwnerEnabler: false, isSubmissionReviewer: true, isJobTenantWriter: false, status: 'DeveloperPaused', canToggle: false, canEdit: false },
    { name: 'tenant writer', isJobOwnerWriter: false, isJobOwnerEnabler: false, isSubmissionReviewer: false, isJobTenantWriter: true, status: 'DeveloperPaused', canToggle: true, canEdit: true },
    { name: 'customer-paused owner writer', isJobOwnerWriter: true, isJobOwnerEnabler: false, isSubmissionReviewer: false, isJobTenantWriter: false, status: 'CustomerPaused', canToggle: true, canEdit: true },
    { name: 'threshold-exceeded owner writer', isJobOwnerWriter: true, isJobOwnerEnabler: false, isSubmissionReviewer: false, isJobTenantWriter: false, status: 'ThresholdExceeded', canToggle: false, canEdit: true },
    { name: 'threshold-exceeded owner writer and reviewer', isJobOwnerWriter: true, isJobOwnerEnabler: false, isSubmissionReviewer: true, isJobTenantWriter: false, status: 'ThresholdExceeded', canToggle: false, canEdit: true },
    { name: 'threshold-exceeded tenant writer', isJobOwnerWriter: false, isJobOwnerEnabler: false, isSubmissionReviewer: false, isJobTenantWriter: true, status: 'ThresholdExceeded', canToggle: false, canEdit: true },
  ]) {
    test(`${scenario.name} has the expected edit access`, async ({ page }) => {
      const routeJobId = 'routeJobId' in scenario ? scenario.routeJobId : jobId;
      await setupMockPage(page);
      await page.route('**/api/v1/settings/', route => route.fulfill({
        json: [{ settingKey: SettingKey.IsAICopilotEnabled, settingValue: 'false' }],
      }));
      await page.route('**/api/v1/roles/getAllRoles', route => route.fulfill({
        json: { ...scenario, isJobOwnerReader: true },
      }));
      await page.route('**/api/v1/jobDetails/job/*', route => route.fulfill({
        json: {
          syncJobId: jobId,
          targetGroupId: 'group-002',
          targetDestinationType: 'GroupMembership',
          targetGroupName: 'Engineering-All',
          status: scenario.status,
          enabledOrNot: false,
          query: '[]',
          titles: [],
          period: 6,
          startDate: '2026-01-01T00:00:00Z',
          lastSuccessfulStartTime: '2026-03-01T00:00:00Z',
          lastSuccessfulRunTime: '2026-03-01T00:03:00Z',
          estimatedNextRunTime: '2026-03-01T06:00:00Z',
          thresholdPercentageForAdditions: 20,
          thresholdPercentageForRemovals: 20,
          endpoints: [],
          requestor: 'playwright@contoso.com',
        },
      }));

      await page.goto(`/JobDetails/${routeJobId}`, { waitUntil: 'domcontentloaded' });
      await expect(page.getByText('Engineering-All', { exact: true }).first()).toBeVisible();
      await expect(page.getByText(statusLabels[scenario.status], { exact: true })).toBeVisible();
      const toggle = page.getByRole('switch', { name: 'Disabled', exact: true });
      const editButtons = page.getByRole('button', { name: 'Edit', exact: true });
      if (scenario.canToggle) {
        await expect(toggle).toBeEnabled();
      } else {
        await expect(toggle).toBeDisabled();
      }
      await expect(editButtons).toHaveCount(scenario.canEdit ? 2 : 0);

      await page.goto(`/ManageMembership/${routeJobId}`, { waitUntil: 'domcontentloaded' });
      if (scenario.canEdit) {
        await expect(page.getByRole('button', { name: 'Next Step', exact: true })).toBeVisible();
        await expect(page).toHaveURL(new RegExp(`/ManageMembership/${routeJobId}$`));
      } else {
        await expect(page).toHaveURL(new RegExp(`/JobDetails/${routeJobId}$`));
        await expect(toggle).toBeDisabled();
        await expect(editButtons).toHaveCount(0);
      }
    });
  }
});
