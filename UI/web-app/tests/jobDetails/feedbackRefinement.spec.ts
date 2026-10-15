// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';
import { SettingKey } from '../../src/models/SettingKey';

test.describe('AI feedback reliance disclaimer', () => {
  test.skip(process.env.PLAYWRIGHT_USE_MOCK_API === 'false', 'Requires mock API fixtures.');

  for (const enabled of [true, false]) {
    test(`shows the disclaimer only when refinement is enabled (${enabled})`, async ({ page }) => {
      await setupMockPage(page);
      await page.route(/\/api\/v1\/settings\/?(?:\?.*)?$/, async route => {
        const setting = {
          settingKey: SettingKey.IsAIRejectionFeedbackRefinementEnabled,
          settingValue: String(enabled),
        };
        const key = new URL(route.request().url()).searchParams.get('key');
        await route.fulfill({ json: key ? setting : [setting] });
      });
      await page.route('**/api/v1/jobDetails/job/mockjob002', async route => {
        await route.fulfill({
          json: {
            syncJobId: 'mockjob002',
            targetGroupId: 'group-002',
            targetDestinationType: 'GroupMembership',
            targetGroupName: 'Mock pending request',
            targetChannelId: '',
            targetChannelName: '',
            status: 'PendingReview',
            enabledOrNot: false,
            query: '',
            titles: [],
            endpoints: [],
            period: 24,
            startDate: '2026-09-01T00:00:00Z',
            lastSuccessfulStartTime: '1753-01-01T00:00:00Z',
            lastSuccessfulRunTime: '1753-01-01T00:00:00Z',
            estimatedNextRunTime: '1753-01-01T00:00:00Z',
            thresholdPercentageForAdditions: 100,
            thresholdPercentageForRemovals: 20,
          },
        });
      });

      const refinementInputs: string[] = [];
      await page.route('**/api/v1/feedback/refine', async route => {
        refinementInputs.push(route.request().postDataJSON().feedback);
        await route.fulfill({ json: { refinedText: 'Please clarify the scope.' } });
      });

      await page.goto('/JobDetails/mockjob002');
      await expect(page.getByText('Membership Details')).toBeVisible();
      await page.getByRole('button', { name: 'Reject', exact: true }).click();

      const dialog = page.getByRole('alertdialog', { name: 'Reject Submission' });
      const editor = dialog.getByRole('textbox', { name: /rejection reason/i });
      await expect(editor).toBeVisible();
      const disclaimer = dialog.getByText('AI-generated content may be inaccurate. Review before sending.');
      const refine = dialog.getByRole('button', { name: 'Refine feedback with AI' });

      if (!enabled) {
        await expect(disclaimer).toHaveCount(0);
        await expect(refine).toHaveCount(0);
        return;
      }

      await expect(disclaimer).toBeVisible();
      await expect(refine).toHaveAttribute('aria-describedby', 'feedback-refinement-disclaimer');
      await editor.fill('scope unclear');
      await refine.click();
      await expect(editor).toHaveValue('Please clarify the scope.');
      await expect(disclaimer).toBeVisible();
      expect(refinementInputs).toEqual(['scope unclear']);

      await dialog.getByRole('button', { name: 'Restore original' }).click();
      await expect(editor).toHaveValue('scope unclear');
      await expect(disclaimer).toBeVisible();
    });
  }
});
