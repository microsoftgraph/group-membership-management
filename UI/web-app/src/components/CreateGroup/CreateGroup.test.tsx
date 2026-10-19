// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { CreateGroup } from './CreateGroup';
import { renderWithProviders } from '../../testing/renderWithProviders';
import manageMembershipReducer from '../../store/manageMembership.slice';
import { createGroup } from '../../store/groups.api';
import { NewGroupType } from '../../models/NewGroupType';

const request = { groupName: 'Finance', groupAlias: 'Finance', groupType: NewGroupType.Microsoft365, description: '' };
beforeEach(() => {
  window.HTMLElement.prototype.scrollIntoView = vi.fn();
});

const renderDialog = (groupType = NewGroupType.Microsoft365, onGroupCreated = vi.fn(), onDismiss = vi.fn()) =>
  renderWithProviders(<CreateGroup groupType={groupType} onGroupCreated={onGroupCreated} onDismiss={onDismiss} />);

describe('Create group dialogs', () => {
  it.each([
    ['Group Name', 'Enter a name for your group that helps identify its purpose. This will be visible to all members.'],
    ['Group alias', 'This will be the email address for your group.'],
    ['Description', "Describe the purpose of your group to help members understand what it's for."],
    ['Sensitivity label (example)', "Select a sensitivity label to classify a group and apply your organization's protection settings."],
    ['Group visibility', 'Choose who can discover and join your group. This setting affects how visible your group is to others in your organization.'],
    ['Send welcome emails', "Send a welcome email when someone joins. It helps new members get started and understand the group's purpose. This setting cannot be changed once set."],
    ['Members auto-follow group conversations', 'When checked, members will automatically follow the group and receive group emails and calendar events in their personal inboxes. When unchecked, members will need to manually follow the group to receive messages.'],
    ['Show group mailbox and calendar in Outlook', 'When checked, the group mailbox and calendar will appear in Outlook. This enables members to manage their follow settings (e.g., stop auto-following group messages) using the Outlook interface. When unchecked, the group mailbox and calendar will be hidden from Outlook. This removes the ability to change follow settings in Outlook and prevents members from accessing or searching historical messages stored in the group mailbox via the Outlook client.'],
  ])('provides the exact reviewed help text for %s', (label, description) => {
    renderDialog();
    const info = screen.getByRole('button', { name: `${label} info`, exact: true });
    expect(info).toHaveAccessibleDescription(description);
    expect(info).toHaveAttribute('type', 'button');
  });

  it('preserves input labels and keeps info buttons separate from checkbox activation', () => {
    const onGroupCreated = vi.fn();
    renderDialog(NewGroupType.Microsoft365, onGroupCreated);
    expect(screen.getByRole('textbox', { name: 'Group Name', exact: true })).toBeRequired();
    expect(screen.getByRole('textbox', { name: 'Group alias', exact: true })).toBeRequired();
    expect(screen.getByRole('textbox', { name: 'Description', exact: true })).not.toBeRequired();
    for (const label of ['Send welcome emails', 'Members auto-follow group conversations', 'Show group mailbox and calendar in Outlook']) {
      const checkbox = screen.getByRole('checkbox', { name: label, exact: true });
      const checked = label !== 'Send welcome emails';
      fireEvent.click(screen.getByRole('button', { name: `${label} info`, exact: true }));
      expect(checkbox).toHaveProperty('checked', checked);
    }
    expect(onGroupCreated).not.toHaveBeenCalled();
  });

  it('renders the Security dialog without field info bubbles and preserves informational notices', () => {
    renderDialog(NewGroupType.Security);
    expect(screen.queryAllByRole('button', { name: / info$/ })).toHaveLength(0);
    expect(screen.getByRole('textbox', { name: 'Group Name', exact: true })).toBeRequired();
    expect(screen.getByRole('textbox', { name: 'Description', exact: true })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Sensitivity label (example)', exact: true })).toBeInTheDocument();
    expect(screen.getByText(/these selections won't be saved or applied/i)).toBeInTheDocument();
    expect(screen.getByText(/this preview does not apply a label/i)).toBeInTheDocument();
  });

  it('renders the Microsoft 365 dialog and reviewed defaults without real Exchange settings', () => {
    renderDialog();
    expect(screen.getByRole('dialog', { name: 'New Microsoft 365 group' })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Use group name as email alias' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Send welcome emails' })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Members auto-follow group conversations' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Show group mailbox and calendar in Outlook' })).toBeChecked();
    expect(screen.getByRole('combobox', { name: 'Group visibility' })).toHaveTextContent('Private');
    expect(screen.queryByRole('combobox', { name: 'Authorized Senders' })).not.toBeInTheDocument();
    expect(screen.queryAllByRole('switch')).toHaveLength(0);
    expect(screen.getByText(/these selections won't be saved or applied/i)).toBeInTheDocument();
  });

  it('renders the corrected Security title and no Microsoft 365-only inputs', () => {
    renderDialog(NewGroupType.Security);
    expect(screen.getByRole('dialog', { name: 'New Security group' })).toBeInTheDocument();
    expect(screen.queryByPlaceholderText('Enter the alias of the group')).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Group visibility' })).not.toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Policy' })).toHaveTextContent('Select policy type');
    fireEvent.click(screen.getByRole('combobox', { name: 'Policy' }));
    expect(screen.getByRole('option', { name: 'This group requires owner approval' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Only the owner of this group and admins can add members' })).toBeInTheDocument();
  });

  it('supports every auto-alias transition without overwriting a custom alias', () => {
    renderDialog();
    const name = screen.getByPlaceholderText('Enter the name of the group');
    const alias = screen.getByPlaceholderText('Enter the alias of the group');
    const checkbox = screen.getByRole('checkbox', { name: 'Use group name as email alias' });
    fireEvent.change(name, { target: { value: 'Finance Team@' } });
    expect(alias).toHaveValue('FinanceTeam');
    fireEvent.click(checkbox);
    expect(alias).toHaveValue('FinanceTeam');
    fireEvent.change(name, { target: { value: 'New Team' } });
    expect(alias).toHaveValue('FinanceTeam');
    fireEvent.click(checkbox);
    expect(alias).toHaveValue('NewTeam');
    fireEvent.change(alias, { target: { value: 'custom+alias' } });
    expect(checkbox).not.toBeChecked();
    fireEvent.change(name, { target: { value: 'Another Team' } });
    expect(alias).toHaveValue('custom+alias');
    fireEvent.click(checkbox);
    expect(alias).toHaveValue('AnotherTeam');
  });

  it.each([
    { groupType: NewGroupType.Microsoft365, groupName: '', groupAlias: 'finance' },
    { groupType: NewGroupType.Microsoft365, groupName: ' \t ', groupAlias: 'finance' },
    { groupType: NewGroupType.Microsoft365, groupName: 'Finance', groupAlias: '' },
    { groupType: NewGroupType.Microsoft365, groupName: 'Finance', groupAlias: ' \t ' },
    { groupType: NewGroupType.Security, groupName: '', groupAlias: '' },
    { groupType: NewGroupType.Security, groupName: ' \t ', groupAlias: '' },
  ])('blocks creation with blank required fields: $groupType / "$groupName" / "$groupAlias"', ({
    groupType, groupName, groupAlias,
  }) => {
    const onGroupCreated = vi.fn();
    renderDialog(groupType, onGroupCreated);
    const create = screen.getByRole('button', { name: 'Create group', exact: true });
    expect(create).toBeDisabled();
    fireEvent.change(screen.getByPlaceholderText('Enter the name of the group'), { target: { value: groupName } });
    if (groupType === NewGroupType.Microsoft365) {
      fireEvent.change(screen.getByPlaceholderText('Enter the alias of the group'), { target: { value: groupAlias } });
    }
    expect(create).toBeDisabled();
    fireEvent.click(create);
    expect(onGroupCreated).not.toHaveBeenCalled();
    fireEvent.change(screen.getByPlaceholderText('Enter the name of the group'), { target: { value: 'Finance' } });
    if (groupType === NewGroupType.Microsoft365) {
      fireEvent.change(screen.getByPlaceholderText('Enter the alias of the group'), { target: { value: 'finance' } });
    }
    expect(create).toBeEnabled();
  });

  it.each([NewGroupType.Microsoft365, NewGroupType.Security])(
    'submits functional fields only and persists an explicitly blank description for type %s',
    async groupType => {
      const onGroupCreated = vi.fn();
      const { store } = renderDialog(groupType, onGroupCreated);
      fireEvent.change(screen.getByPlaceholderText('Enter the name of the group'), { target: { value: 'Finance Team' } });
      fireEvent.click(screen.getByRole('combobox', { name: 'Sensitivity label (example)' }));
      fireEvent.click(screen.getByRole('option', { name: 'Confidential/Internal-Only (example)' }));
      if (groupType === NewGroupType.Microsoft365) {
        fireEvent.change(screen.getByPlaceholderText('Enter the alias of the group'), { target: { value: 'custom' } });
        fireEvent.click(screen.getByRole('checkbox', { name: 'Send welcome emails' }));
      } else {
        fireEvent.click(screen.getByRole('combobox', { name: 'Policy' }));
        fireEvent.click(screen.getByRole('option', { name: 'This group requires owner approval' }));
      }
      fireEvent.click(screen.getByRole('button', { name: 'Create group', exact: true }));
      await waitFor(() => expect(onGroupCreated).toHaveBeenCalledTimes(1));
      expect(onGroupCreated).toHaveBeenCalledWith('Finance Team', groupType === NewGroupType.Security ? 'FinanceTeam' : 'custom', groupType, '');
      expect(store.getState().manageMembership.groupSettings).toBeUndefined();
    }
  );

  it('prevents duplicate activation and every dialog dismissal while pending', async () => {
    let finish!: () => void;
    const pending = new Promise<void>(resolve => { finish = resolve; });
    const onGroupCreated = vi.fn(() => pending);
    const onDismiss = vi.fn();
    renderDialog(NewGroupType.Microsoft365, onGroupCreated, onDismiss);
    fireEvent.change(screen.getByPlaceholderText('Enter the name of the group'), { target: { value: 'Finance' } });
    const create = screen.getByRole('button', { name: 'Create group', exact: true });
    fireEvent.click(create);
    fireEvent.click(create);
    expect(onGroupCreated).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled();
    screen.getAllByRole('textbox').forEach(control => expect(control).toBeDisabled());
    screen.getAllByRole('checkbox').forEach(control => expect(control).toBeDisabled());
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape', code: 'Escape', keyCode: 27 });
    expect(onDismiss).not.toHaveBeenCalled();
    await act(async () => { finish(); await pending; });
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeEnabled();
  });

  it('retains functional and preview values after a confirmed rejection and permits retry', async () => {
    const onGroupCreated = vi.fn();
    const { store } = renderDialog(NewGroupType.Microsoft365, onGroupCreated);
    fireEvent.change(screen.getByPlaceholderText('Enter the name of the group'), { target: { value: 'Finance' } });
    fireEvent.change(screen.getByPlaceholderText('Tell people the purpose of your group'), { target: { value: 'Description' } });
    fireEvent.click(screen.getByRole('checkbox', { name: 'Send welcome emails' }));
    act(() => {
      store.dispatch(createGroup.pending('request', request));
      store.dispatch(createGroup.rejected(null, 'request', request, { kind: 'rejected', statusCode: 400 }));
    });
    expect(screen.getByPlaceholderText('Enter the name of the group')).toHaveValue('Finance');
    expect(screen.getByPlaceholderText('Tell people the purpose of your group')).toHaveValue('Description');
    expect(screen.getByRole('checkbox', { name: 'Send welcome emails' })).toBeChecked();
    expect(screen.getByRole('button', { name: 'Create group', exact: true })).toBeEnabled();
    expect(screen.getByRole('alert')).toHaveTextContent(/couldn't create the group/i);
    expect(window.HTMLElement.prototype.scrollIntoView).toHaveBeenCalledWith({ block: 'nearest' });
    fireEvent.click(screen.getByRole('button', { name: 'Create group', exact: true }));
    await waitFor(() => expect(onGroupCreated).toHaveBeenCalledTimes(1));
  });

  it.each(['transport', 'missing-id'])('disables retry after an uncertain %s outcome, even after edits', outcome => {
    const { store } = renderDialog();
    act(() => {
      store.dispatch(createGroup.pending('request', request));
      if (outcome === 'transport') {
        store.dispatch(createGroup.rejected(new Error('raw exception details'), 'request', request));
      } else {
        store.dispatch(createGroup.fulfilled({ ok: true, statusCode: 200 }, 'request', request));
      }
    });
    expect(screen.getByRole('alert')).toHaveTextContent(/check your groups before creating another one/i);
    expect(screen.queryByText('raw exception details')).not.toBeInTheDocument();
    fireEvent.change(screen.getByPlaceholderText('Enter the name of the group'), { target: { value: 'Edited' } });
    expect(screen.getByRole('button', { name: 'Create group', exact: true })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeEnabled();
  });

  it('waits for submission to finish before displaying and scrolling to an uncertain-result error', async () => {
    let finish!: () => void;
    const pending = new Promise<void>(resolve => { finish = resolve; });
    const { store } = renderDialog(NewGroupType.Microsoft365, vi.fn(() => pending));
    fireEvent.change(screen.getByPlaceholderText('Enter the name of the group'), { target: { value: 'Finance' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create group', exact: true }));
    act(() => {
      store.dispatch(createGroup.pending('request', request));
      store.dispatch(createGroup.rejected(new Error('timed out'), 'request', request, { kind: 'uncertain' }));
    });
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    await act(async () => { finish(); await pending; });
    expect(screen.getByRole('alert')).toHaveTextContent("We couldn't confirm that your group was created.");
    expect(window.HTMLElement.prototype.scrollIntoView).toHaveBeenCalledWith({ block: 'nearest' });
    expect(screen.getByRole('button', { name: 'Create group', exact: true })).toBeDisabled();
  });

  it('cancels without submitting', () => {
    const onDismiss = vi.fn();
    const onGroupCreated = vi.fn();
    renderDialog(NewGroupType.Security, onGroupCreated, onDismiss);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onDismiss).toHaveBeenCalledTimes(1);
    expect(onGroupCreated).not.toHaveBeenCalled();
  });

  it('does not let preview controls mutate existing onboarding settings', () => {
    const groupSettings = { authorizedSenders: [], hiddenFromExchangeClients: true, welcomeMessageEnabled: true };
    const { store } = renderWithProviders(
      <CreateGroup groupType={NewGroupType.Microsoft365} onGroupCreated={vi.fn()} onDismiss={vi.fn()} />,
      { preloadedState: { manageMembership: { ...manageMembershipReducer(undefined, { type: 'init' }), groupSettings } } }
    );
    fireEvent.click(screen.getByRole('checkbox', { name: 'Show group mailbox and calendar in Outlook' }));
    expect(store.getState().manageMembership.groupSettings).toEqual(groupSettings);
  });
});
