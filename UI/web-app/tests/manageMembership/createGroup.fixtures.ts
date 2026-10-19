// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Page } from '@playwright/test';

type CreateOutcome = 'success' | 'rejection' | 'network' | 'server' | 'missing-id';

export async function controlGroupCreation(page: Page, initialOutcome: CreateOutcome = 'success', hold = false) {
  let outcome = initialOutcome;
  const payloads: Record<string, unknown>[] = [];
  const deferredRequests: string[] = [];
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  if (!hold) release();
  page.on('request', request => {
    if (/\/(?:labels|sensitivity|naming-policy|visibility-options|policy|behavior)(?:\/|$)/i.test(new URL(request.url()).pathname)) {
      deferredRequests.push(request.url());
    }
  });
  await page.route('**/api/v1/destinations/groups', async route => {
    payloads.push(route.request().postDataJSON());
    await gate;
    if (outcome === 'success') return route.fallback();
    if (outcome === 'network') return route.abort('failed');
    const status = outcome === 'rejection' ? 400 : outcome === 'server' ? 500 : 200;
    await route.fulfill({
      status,
      json: { ok: status === 200, statusCode: status, detail: 'Synthetic raw internal details' },
    });
  });
  return { payloads, deferredRequests, release, setOutcome: (value: CreateOutcome) => { outcome = value; } };
}

export async function openGroupCreation(page: Page, groupType: 0 | 1) {
  await page.getByRole('radio', { name: 'Create a new group', exact: true }).check();
  await page.getByRole('button', { name: groupType === 0 ? 'Create M365 Group' : 'Create Security Group', exact: true }).click();
  return page.getByRole('dialog', { name: groupType === 0 ? 'New Microsoft 365 group' : 'New Security group', exact: true });
}
