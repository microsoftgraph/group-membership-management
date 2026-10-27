// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { expect, test } from '@playwright/test';
import { setupMockPage } from '../mocks/setupMockPage';
import { controlGroupCreation, openGroupCreation } from './createGroup.fixtures';

for (const width of [1280, 390]) {
  test(`creation info bubbles match the reviewed text and support hover and keyboard focus at ${width}px`, async ({ page }, testInfo) => {
    await setupMockPage(page);
    const fixture = await controlGroupCreation(page);
    await page.goto('/ManageMembership');
    const dialog = await openGroupCreation(page, 0);
    await page.setViewportSize({ width, height: 720 });
    const helpTexts = [
      ['Group Name', 'Enter a name for your group that helps identify its purpose. This will be visible to all members.'],
      ['Group alias', 'This will be the email address for your group.'],
      ['Description', "Describe the purpose of your group to help members understand what it's for."],
      ['Sensitivity label (example)', "Select a sensitivity label to classify a group and apply your organization's protection settings."],
      ['Group visibility', 'Choose who can discover and join your group. This setting affects how visible your group is to others in your organization.'],
      ['Send welcome emails', "Send a welcome email when someone joins. It helps new members get started and understand the group's purpose. This setting cannot be changed once set."],
      ['Members auto-follow group conversations', 'When checked, members will automatically follow the group and receive group emails and calendar events in their personal inboxes. When unchecked, members will need to manually follow the group to receive messages.'],
      ['Show group mailbox and calendar in Outlook', 'When checked, the group mailbox and calendar will appear in Outlook. This enables members to manage their follow settings (e.g., stop auto-following group messages) using the Outlook interface. When unchecked, the group mailbox and calendar will be hidden from Outlook. This removes the ability to change follow settings in Outlook and prevents members from accessing or searching historical messages stored in the group mailbox via the Outlook client.'],
    ];
    for (const [label, description] of helpTexts) {
      const info = dialog.getByRole('button', { name: `${label} info`, exact: true });
      const tooltip = page.getByRole('tooltip');
      await info.hover();
      await expect(tooltip).toHaveText(description);
      await expect(tooltip).toBeInViewport({ ratio: 1 });
      await page.mouse.move(0, 0);
      await expect(tooltip).toHaveCount(0);
      await info.focus();
      await expect(tooltip).toHaveText(description);
      await expect(page.locator('.ms-Tooltip')).toHaveCSS('opacity', '1');
      await expect(tooltip).toBeInViewport({ ratio: 1 });
      await expect(info).toHaveAccessibleDescription(description);
      await page.screenshot({ path: testInfo.outputPath(`info-${helpTexts.findIndex(([name]) => name === label)}.png`) });
      await page.keyboard.press('Tab');
      await page.mouse.move(0, 0);
      await expect(tooltip).toHaveCount(0);
    }
    for (const [label, checked] of [
      ['Send welcome emails', false],
      ['Members auto-follow group conversations', true],
      ['Show group mailbox and calendar in Outlook', true],
    ] as const) {
      await dialog.getByRole('button', { name: `${label} info`, exact: true }).click();
      await expect(dialog.getByRole('checkbox', { name: label, exact: true })).toBeChecked({ checked });
    }
    await expect(dialog.getByText(/these selections won't be saved or applied/)).toBeVisible();
    expect(fixture.payloads).toHaveLength(0);
  });
}

test('flag off preserves destination selection without cards, dialogs or create calls', async ({ page }) => {
  await setupMockPage(page, { createGroupFeatureEnabled: false });
  const fixture = await controlGroupCreation(page);
  await page.goto('/ManageMembership', { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('combobox', { name: 'Select Destination Type *', exact: true })).toBeVisible();
  await expect(page.getByRole('radio', { name: 'Create a new group', exact: true })).toHaveCount(0);
  await expect(page.getByText('Collaborate', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(fixture.payloads).toHaveLength(0);
  expect(fixture.deferredRequests).toHaveLength(0);
});

test('flag on keeps existing destination selection unchanged until creation is chosen', async ({ page }) => {
  await setupMockPage(page);
  const fixture = await controlGroupCreation(page);
  await page.goto('/ManageMembership');
  await expect(page.getByRole('radio', { name: 'Select existing destination', exact: true })).toBeChecked();
  await expect(page.getByRole('combobox', { name: 'Select Destination Type *', exact: true })).toBeVisible();
  await expect(page.getByText('Collaborate', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(fixture.payloads).toHaveLength(0);
});

for (const groupType of [0, 1] as const) {
  const kind = groupType === 0 ? 'Microsoft 365' : 'Security';

  test(`${kind} requires nonblank fields before enabling creation`, async ({ page }) => {
    await setupMockPage(page);
    const fixture = await controlGroupCreation(page);
    await page.goto('/ManageMembership');
    const dialog = await openGroupCreation(page, groupType);
    const name = dialog.getByPlaceholder('Enter the name of the group');
    const create = dialog.getByRole('button', { name: 'Create group', exact: true });
    await expect(create).toBeDisabled();
    await name.fill('   ');
    await expect(create).toBeDisabled();
    await name.fill('Finance Team');
    await expect(create).toBeEnabled();
    if (groupType === 0) {
      const alias = dialog.getByPlaceholder('Enter the alias of the group');
      await alias.fill('');
      await expect(create).toBeDisabled();
      await alias.fill('   ');
      await expect(create).toBeDisabled();
      await alias.fill('finance');
    } else {
      await expect(dialog.getByPlaceholder('Enter the alias of the group')).toHaveCount(0);
    }
    await expect(create).toBeEnabled();
    await name.fill('');
    await expect(create).toBeDisabled();
    expect(fixture.payloads).toHaveLength(0);
    await name.fill('Finance Team');
    await create.click();
    await expect(dialog).toHaveCount(0);
    if (groupType === 0) await page.getByRole('button', { name: 'Not now', exact: true }).click();
    expect(fixture.payloads).toEqual([{
      groupName: 'Finance Team',
      groupAlias: groupType === 0 ? 'finance' : 'FinanceTeam',
      groupType,
      description: '',
    }]);
  });

  test(`${kind} persists functional inputs, excludes every preview and continues with the returned destination`, async ({ page }, testInfo) => {
    await setupMockPage(page);
    const fixture = await controlGroupCreation(page);
    await page.goto('/ManageMembership');
    await page.getByRole('radio', { name: 'Create a new group', exact: true }).check();
    await page.screenshot({ path: testInfo.outputPath('entry-cards.png') });
    const dialog = await openGroupCreation(page, groupType);
    await expect(dialog.getByRole('heading', { name: `New ${kind} group`, exact: true })).toBeVisible();
    await expect(dialog.locator('.ms-Dialog-main')).toHaveCSS('width', '676px');
    await expect(dialog.getByText(/these selections won't be saved or applied/)).toBeVisible();
    await expect(dialog.getByRole('button', { name: / info$/ })).toHaveCount(groupType === 0 ? 8 : 0);
    await page.screenshot({ path: testInfo.outputPath('initial.png') });
    await dialog.getByPlaceholder('Enter the name of the group').fill('Finance Team');
    await dialog.getByPlaceholder('Tell people the purpose of your group').fill(' Project documentation ');
    await dialog.getByRole('combobox', { name: 'Sensitivity label (example)', exact: true }).click();
    await page.getByRole('option', { name: 'Confidential/Internal-Only (example)', exact: true }).click();
    if (groupType === 0) {
      await dialog.getByPlaceholder('Enter the alias of the group').fill('custom+alias');
      await dialog.getByRole('combobox', { name: 'Group visibility', exact: true }).click();
      await page.getByRole('option', { name: 'Public', exact: true }).click();
      await dialog.getByRole('checkbox', { name: 'Send welcome emails', exact: true }).press('Space');
      await dialog.getByRole('checkbox', { name: 'Members auto-follow group conversations', exact: true }).press('Space');
      await dialog.getByRole('checkbox', { name: 'Show group mailbox and calendar in Outlook', exact: true }).press('Space');
      await expect(dialog.getByRole('checkbox', { name: 'Send welcome emails', exact: true })).toBeChecked();
      await expect(dialog.getByRole('checkbox', { name: 'Members auto-follow group conversations', exact: true })).not.toBeChecked();
      await expect(dialog.getByRole('checkbox', { name: 'Show group mailbox and calendar in Outlook', exact: true })).not.toBeChecked();
    } else {
      await expect(dialog.getByPlaceholder('Enter the alias of the group')).toHaveCount(0);
      await expect(dialog.getByRole('checkbox')).toHaveCount(0);
      await dialog.getByRole('combobox', { name: 'Policy', exact: true }).click();
      await page.getByRole('option', { name: 'Only the owner of this group and admins can add members', exact: true }).click();
    }
    await page.screenshot({ path: testInfo.outputPath('filled-preview.png') });
    await dialog.getByRole('button', { name: 'Create group', exact: true }).click();
    await expect(dialog).toHaveCount(0);
    if (groupType === 0) await page.getByRole('button', { name: 'Not now', exact: true }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Preview-only selections were not saved or applied.' })).toBeVisible();
    await expect(page.getByRole('radio', { name: 'Select existing destination', exact: true })).toBeChecked();
    expect(fixture.payloads).toEqual([{
      groupName: 'Finance Team',
      groupAlias: groupType === 0 ? 'custom+alias' : 'FinanceTeam',
      groupType,
      description: ' Project documentation ',
    }]);
    expect(fixture.deferredRequests).toHaveLength(0);
    const next = page.getByRole('button', { name: /^Next/ });
    await expect(next).toBeEnabled();
    await page.screenshot({ path: testInfo.outputPath('success-disclosure.png') });
    await next.click();
    await expect(page.getByTestId('add-rule-button')).toBeVisible();
    expect(fixture.payloads).toHaveLength(1);
    expect(fixture.deferredRequests).toHaveLength(0);
  });

  test(`${kind} retains all values after a confirmed rejection and permits deliberate retry`, async ({ page }, testInfo) => {
    await setupMockPage(page);
    const fixture = await controlGroupCreation(page, 'rejection');
    await page.goto('/ManageMembership');
    const dialog = await openGroupCreation(page, groupType);
    await dialog.getByPlaceholder('Enter the name of the group').fill('Finance Team');
    await dialog.getByPlaceholder('Tell people the purpose of your group').fill('Retained description');
    await dialog.getByRole('combobox', { name: 'Sensitivity label (example)', exact: true }).click();
    await page.getByRole('option', { name: 'Confidential/Internal-Only (example)', exact: true }).click();
    if (groupType === 0) await dialog.getByRole('checkbox', { name: 'Send welcome emails', exact: true }).press('Space');
    await dialog.getByRole('button', { name: 'Create group', exact: true }).click();
    await expect(dialog.getByRole('alert')).toContainText("We couldn't create the group");
    await expect(page.getByRole('dialog', { name: 'Advanced settings (Optional)', exact: true })).toHaveCount(0);
    await expect(dialog.getByRole('alert')).toBeInViewport();
    await expect(dialog.getByPlaceholder('Enter the name of the group')).toHaveValue('Finance Team');
    await expect(dialog.getByPlaceholder('Tell people the purpose of your group')).toHaveValue('Retained description');
    await expect(dialog.getByRole('combobox', { name: 'Sensitivity label (example)', exact: true })).toContainText('Confidential/Internal-Only');
    if (groupType === 0) await expect(dialog.getByRole('checkbox', { name: 'Send welcome emails', exact: true })).toBeChecked();
    await expect(page.getByText('Synthetic raw internal details')).toHaveCount(0);
    await page.screenshot({ path: testInfo.outputPath('confirmed-failure.png') });
    fixture.setOutcome('success');
    await dialog.getByRole('button', { name: 'Create group', exact: true }).click();
    await expect(dialog).toHaveCount(0);
    if (groupType === 0) await page.getByRole('button', { name: 'Not now', exact: true }).click();
    expect(fixture.payloads).toHaveLength(2);
    expect(fixture.payloads[1]).toEqual(fixture.payloads[0]);
  });

  for (const outcome of ['network', 'server', 'missing-id'] as const) {
    test(`${kind} disables retry after uncertain ${outcome}, including after edits`, async ({ page }) => {
      await setupMockPage(page);
      const fixture = await controlGroupCreation(page, outcome);
      await page.goto('/ManageMembership');
      const dialog = await openGroupCreation(page, groupType);
      await dialog.getByPlaceholder('Enter the name of the group').fill('Finance Team');
      await dialog.getByRole('button', { name: 'Create group', exact: true }).click();
      await expect(dialog.getByRole('alert')).toContainText('Check your groups before creating another one');
      await expect(dialog.getByRole('alert')).toBeInViewport();
      await expect(dialog.getByRole('button', { name: 'Create group', exact: true })).toBeDisabled();
      await dialog.getByPlaceholder('Enter the name of the group').fill('Edited name');
      await expect(dialog.getByRole('button', { name: 'Create group', exact: true })).toBeDisabled();
      await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeEnabled();
      expect(fixture.payloads).toHaveLength(1);
      await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
      await expect(dialog).toHaveCount(0);
    });
  }

  test(`${kind} prevents duplicate submission, dismissal and browser Back while pending`, async ({ page }, testInfo) => {
    await setupMockPage(page);
    const fixture = await controlGroupCreation(page, 'success', true);
    await page.goto('/');
    await page.locator('#manage-membership-button').click();
    const dialog = await openGroupCreation(page, groupType);
    await dialog.getByPlaceholder('Enter the name of the group').fill('Finance+Ops');
    await dialog.getByRole('button', { name: 'Create group', exact: true }).click();
    await expect(dialog.getByRole('button', { name: 'Creating...', exact: true })).toBeDisabled();
    await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeDisabled();
    await expect(dialog.getByRole('button', { name: 'Close', exact: true })).toBeDisabled();
    await dialog.getByRole('button', { name: 'Creating...', exact: true }).dispatchEvent('click');
    await page.keyboard.press('Escape');
    await page.mouse.click(2, 2);
    await page.screenshot({ path: testInfo.outputPath('pending.png') });
    await page.goBack();
    await expect(page).toHaveURL(/\/ManageMembership$/);
    await expect(dialog.getByRole('heading', { name: `New ${kind} group`, exact: true })).toBeVisible();
    expect(fixture.payloads).toHaveLength(1);
    fixture.release();
    await expect(dialog).toHaveCount(0);
    if (groupType === 0) await page.getByRole('button', { name: 'Not now', exact: true }).click();
    await expect(page.getByRole('button', { name: /^Next/ })).toBeEnabled();
    expect(fixture.payloads).toHaveLength(1);
  });

  test(`${kind} supports cancellation, launch focus, fresh drafts and a narrow scrollable dialog`, async ({ page }, testInfo) => {
    await setupMockPage(page);
    const fixture = await controlGroupCreation(page);
    await page.goto('/ManageMembership');
    const dialog = await openGroupCreation(page, groupType);
    await dialog.getByPlaceholder('Enter the name of the group').fill('Discarded');
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
    const launch = page.getByRole('button', { name: groupType === 0 ? 'Create M365 Group' : 'Create Security Group', exact: true });
    await expect(launch).toBeFocused();
    await launch.click();
    await expect(dialog.getByPlaceholder('Enter the name of the group')).toHaveValue('');
    await dialog.getByPlaceholder('Enter the name of the group').focus();
    for (let i = 0; i < 16; i++) {
      await page.keyboard.press('Tab');
      expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
    }
    await page.setViewportSize({ width: 390, height: 600 });
    await dialog.getByPlaceholder('Tell people the purpose of your group').fill('Long description '.repeat(30));
    const bounds = await dialog.locator('.ms-Dialog-main').boundingBox();
    expect(bounds).not.toBeNull();
    expect(bounds?.width).toBeLessThanOrEqual(390);
    if (groupType === 0) {
      const preview = dialog.getByRole('checkbox', { name: 'Show group mailbox and calendar in Outlook', exact: true });
      await preview.scrollIntoViewIfNeeded();
      const previewBounds = await preview.boundingBox();
      const footerBounds = await dialog.getByRole('button', { name: 'Cancel', exact: true }).boundingBox();
      expect(previewBounds).not.toBeNull();
      expect(footerBounds).not.toBeNull();
      expect(previewBounds!.y + previewBounds!.height).toBeLessThan(footerBounds!.y);
      await dialog.getByText('Show group mailbox and calendar in Outlook', { exact: true }).click();
      await expect(preview).not.toBeChecked();
    }
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).scrollIntoViewIfNeeded();
    await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath('mobile.png') });
    expect(fixture.payloads).toHaveLength(0);
  });
}

for (const width of [1280, 390]) {
  test(`advanced settings preview supports editing, help and continuation without writes at ${width}px`, async ({ page }, testInfo) => {
    await setupMockPage(page);
    const fixture = await controlGroupCreation(page);
    const writes: string[] = [];
    page.on('request', request => {
      if (['POST', 'PATCH', 'PUT', 'DELETE'].includes(request.method()) && new URL(request.url()).pathname.startsWith('/api/')) {
        writes.push(request.url());
      }
    });
    await page.goto('/ManageMembership');
    const createDialog = await openGroupCreation(page, 0);
    await createDialog.getByPlaceholder('Enter the name of the group').fill('Finance Team');
    await createDialog.getByRole('button', { name: 'Create group', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Advanced settings (Optional)', exact: true });
    await expect(dialog.getByRole('heading', { name: 'Advanced settings (Optional)', exact: true })).toBeVisible();
    await page.setViewportSize({ width, height: 720 });
    await expect(createDialog).toHaveCount(0);
    await expect(dialog.getByText('Configure additional settings for "Finance Team"')).toBeVisible();
    await expect(dialog.getByText(/these selections won't be saved or applied/)).toBeVisible();
    const senders = dialog.getByRole('textbox', { name: 'Authorized Senders', exact: true });
    await expect(senders).toBeFocused();
    const writesBeforePreview = [...writes];
    await senders.fill('sender@example.com');
    for (const [label, description] of [
      ['Authorized Senders', 'Add users or groups who are permitted to send messages to the group. If blank, all members can send messages to the group. If the group has over 5,000 members it is required to have at least one authorized sender.'],
      ['Hide from global address list', "Hiding from the global address list will prevent others from discovering this group in your organization's address book. The email address can still receive messages if someone knows it and types it manually."],
      ['Enable security group functionality', 'When checked, this group will be security-enabled and cannot be disabled. It can be used to assign permissions to apps and resources, such as managing access to shared apps, assigning roles in Entra ID, or enforcing conditional access policies. This setting is not required for groups used only in Microsoft 365 collaboration tools such as Outlook or Teams.'],
      ['Allow people outside the organization to email the group', 'When checked, people external to the organization can send messages to the group.'],
    ]) {
      const info = dialog.getByRole('button', { name: `${label} info`, exact: true });
      const tooltip = page.getByRole('tooltip');
      await info.hover();
      await expect(tooltip).toHaveText(description);
      await expect(tooltip).toBeInViewport({ ratio: 1 });
      await page.mouse.move(0, 0);
      await expect(tooltip).toHaveCount(0);
      await info.focus();
      await expect(tooltip).toHaveText(description);
      await expect(info).toHaveAccessibleDescription(description);
      await expect(tooltip).toBeInViewport({ ratio: 1 });
      await page.keyboard.press('Tab');
      await expect(tooltip).toHaveCount(0);
    }
    for (const label of [
      'Hide from global address list',
      'Enable security group functionality',
      'Allow people outside the organization to email the group',
    ]) {
      const checkbox = dialog.getByRole('checkbox', { name: label, exact: true });
      await expect(checkbox).not.toBeChecked();
      await checkbox.press('Space');
      await expect(checkbox).toBeChecked();
      const info = dialog.getByRole('button', { name: `${label} info`, exact: true });
      await info.focus();
      await expect(page.getByRole('tooltip')).toBeVisible();
      await expect(page.getByRole('tooltip')).toBeInViewport({ ratio: 1 });
      await info.click();
      await expect(checkbox).toBeChecked();
    }
    await senders.focus();
    for (let i = 0; i < 12; i++) {
      await page.keyboard.press('Tab');
      expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
    }
    const bounds = await dialog.locator('.ms-Dialog-main').boundingBox();
    expect(bounds).not.toBeNull();
    expect(bounds!.x).toBeGreaterThanOrEqual(0);
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
    await dialog.getByRole('button', { name: 'Apply settings', exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: testInfo.outputPath('advanced-settings.png') });
    await dialog.getByRole('button', { name: 'Apply settings', exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(page.getByRole('status').filter({ hasText: 'Preview-only selections were not saved or applied.' })).toBeVisible();
    expect(writes).toEqual(writesBeforePreview);
    expect(fixture.payloads).toHaveLength(1);
    await page.getByRole('button', { name: /^Next/ }).click();
    await expect(page.getByTestId('add-rule-button')).toBeVisible();
  });
}

test('keeps waiting for the original creation request and continues when it succeeds', async ({ page }) => {
  await setupMockPage(page);
  const fixture = await controlGroupCreation(page, 'success', true);
  await page.goto('/ManageMembership');
  const dialog = await openGroupCreation(page, 1);
  await dialog.getByPlaceholder('Enter the name of the group').fill('Delayed group');
  await page.clock.install();
  await dialog.getByRole('button', { name: 'Create group', exact: true }).click();
  await expect.poll(() => fixture.payloads.length).toBe(1);
  await page.clock.fastForward(59_000);
  await expect(dialog.getByRole('button', { name: 'Creating...', exact: true })).toBeDisabled();
  await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeDisabled();
  await expect(dialog.getByRole('alert')).toHaveCount(0);
  expect(fixture.payloads).toHaveLength(1);
  fixture.release();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole('button', { name: /^Next/ })).toBeEnabled();
  expect(fixture.payloads).toHaveLength(1);
});

test('auto-alias preserves custom edits and rechecking derives immediately', async ({ page }) => {
  await setupMockPage(page);
  await page.goto('/ManageMembership');
  const dialog = await openGroupCreation(page, 0);
  const name = dialog.getByPlaceholder('Enter the name of the group');
  const alias = dialog.getByPlaceholder('Enter the alias of the group');
  const autoAlias = dialog.getByRole('checkbox', { name: 'Use group name as email alias', exact: true });
  await name.fill('Finance Team@');
  await expect(alias).toHaveValue('FinanceTeam');
  await autoAlias.press('Space');
  await expect(autoAlias).not.toBeChecked();
  await name.fill('New Team');
  await expect(alias).toHaveValue('FinanceTeam');
  await autoAlias.press('Space');
  await expect(autoAlias).toBeChecked();
  await expect(alias).toHaveValue('NewTeam');
  await alias.fill('custom+alias');
  await expect(autoAlias).not.toBeChecked();
  await name.fill('Another Team');
  await expect(alias).toHaveValue('custom+alias');
  await autoAlias.press('Space');
  await expect(autoAlias).toBeChecked();
  await expect(alias).toHaveValue('AnotherTeam');
});

test('recovers destination loading using the known group ID without another create', async ({ page }) => {
  await setupMockPage(page);
  const fixture = await controlGroupCreation(page);
  let reads = 0;
  await page.route('**/api/v1/destinations/groups/*/endpoints', async route => {
    reads++;
    if (reads <= 2) return route.fulfill({ status: 500, json: { detail: 'Synthetic read failure' } });
    await route.fallback();
  });
  await page.goto('/ManageMembership');
  const dialog = await openGroupCreation(page, 1);
  await dialog.getByPlaceholder('Enter the name of the group').fill('Known Created Group');
  await dialog.getByRole('button', { name: 'Create group', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole('alert')).toContainText('The group was created');
  await expect(page.getByRole('button', { name: /^Next/ })).toBeDisabled();
  await page.getByRole('button', { name: 'Check Again', exact: true }).click();
  await expect.poll(() => reads).toBe(2);
  await expect(page.getByRole('alert')).toContainText('The group was created');
  await expect(page.getByRole('button', { name: /^Next/ })).toBeDisabled();
  expect(fixture.payloads).toHaveLength(1);
  await page.getByRole('button', { name: 'Check Again', exact: true }).click();
  await expect(page.getByRole('button', { name: /^Next/ })).toBeEnabled();
  expect(reads).toBe(3);
  expect(fixture.payloads).toHaveLength(1);
  await page.getByRole('button', { name: /^Next/ }).click();
  await expect(page.getByTestId('add-rule-button')).toBeVisible();
  expect(fixture.payloads).toHaveLength(1);
});
