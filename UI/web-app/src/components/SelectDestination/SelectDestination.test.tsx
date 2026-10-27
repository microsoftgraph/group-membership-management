// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { SelectDestination } from './SelectDestination';
import { renderWithProviders } from '../../testing/renderWithProviders';
import settingsReducer from '../../store/settings.slice';
import { SettingKey } from '../../models/SettingKey';
import { createGroup } from '../../store/groups.api';
import { NewGroupType } from '../../models/NewGroupType';

const renderDestination = (enabled: boolean, onGroupCreated = vi.fn()) =>
  renderWithProviders(
    <SelectDestination onSearchDestinationChange={vi.fn()} onSearchChannelChange={vi.fn()} onGroupCreated={onGroupCreated} />,
    {
      preloadedState: {
        settings: {
          ...settingsReducer(undefined, { type: 'init' }),
          settings: [{ settingKey: SettingKey.CreateGroupFeatureEnabled, settingValue: String(enabled) }],
        },
      },
    }
  );

describe('Create group entry boundary', () => {
  it('preserves flag-off existing destination controls and hides all create UI', () => {
    renderDestination(false);
    expect(screen.getByRole('combobox', { name: 'Select Destination Type' })).toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: 'Create a new group' })).not.toBeInTheDocument();
    expect(screen.queryByText('Collaborate')).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('leaves existing destination search available when the flag is on', () => {
    renderDestination(true);
    expect(screen.getByRole('radio', { name: 'Select existing destination' })).toBeChecked();
    expect(screen.getByRole('combobox', { name: 'Select Destination Type' })).toBeInTheDocument();
    expect(screen.queryByText('Collaborate')).not.toBeInTheDocument();
  });

  it('opens distinct fresh dialogs and restores launch focus on cancellation', async () => {
    renderDestination(true);
    fireEvent.click(screen.getByRole('radio', { name: 'Create a new group' }));
    const launch = screen.getByRole('button', { name: 'Create M365 Group' });
    launch.focus();
    fireEvent.click(launch);
    expect(screen.getByRole('dialog', { name: 'New Microsoft 365 group' })).toBeInTheDocument();
    fireEvent.change(screen.getByPlaceholderText('Enter the name of the group'), { target: { value: 'Discarded' } });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(launch).toHaveFocus());
    fireEvent.click(screen.getByRole('button', { name: 'Create Security Group' }));
    expect(screen.getByRole('dialog', { name: 'New Security group' })).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Enter the name of the group')).toHaveValue('');
  });

  it('closes on a returned ID, selects the actual destination and announces unapplied previews', async () => {
    const { store } = renderDestination(true);
    fireEvent.click(screen.getByRole('radio', { name: 'Create a new group' }));
    fireEvent.click(screen.getByRole('button', { name: 'Create Security Group' }));
    const args = { groupName: 'Finance', groupAlias: 'Finance', groupType: NewGroupType.Security, description: '' };
    act(() => {
      store.dispatch(createGroup.pending('request', args));
      store.dispatch(createGroup.fulfilled({ ok: true, statusCode: 200, groupId: 'actual-id' }, 'request', args));
    });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(store.getState().manageMembership.selectedDestination?.id).toBe('actual-id');
    expect(store.getState().manageMembership.selectedDestination?.groupSettings).toBeUndefined();
    expect(screen.getByRole('status')).toHaveTextContent(/preview-only selections were not saved or applied/i);
    expect(screen.getByRole('radio', { name: 'Select existing destination' })).toBeChecked();
  });

  it.each(['Apply settings', 'Not now', 'Close', 'Escape'])(
    'shows the advanced preview only after Microsoft 365 creation succeeds and continues with %s',
    async action => {
      const { store } = renderDestination(true);
      fireEvent.click(screen.getByRole('radio', { name: 'Create a new group' }));
      fireEvent.click(screen.getByRole('button', { name: 'Create M365 Group' }));
      fireEvent.change(screen.getByPlaceholderText('Enter the name of the group'), { target: { value: 'Finance' } });
      fireEvent.click(screen.getByRole('button', { name: 'Create group', exact: true }));
      const args = { groupName: 'Finance', groupAlias: 'Finance', groupType: NewGroupType.Microsoft365, description: '' };
      act(() => {
        store.dispatch(createGroup.pending('request', args));
      });
      expect(screen.queryByRole('dialog', { name: 'Advanced settings (Optional)' })).not.toBeInTheDocument();
      act(() => {
        store.dispatch(createGroup.fulfilled({ ok: true, statusCode: 200, groupId: 'actual-id' }, 'request', args));
      });
      await waitFor(() => expect(screen.getByRole('dialog', { name: 'Advanced settings (Optional)' })).toBeInTheDocument());
      expect(screen.queryByRole('dialog', { name: 'New Microsoft 365 group' })).not.toBeInTheDocument();
      expect(screen.getByText('Configure additional settings for "Finance"')).toBeInTheDocument();
      expect(screen.getByText(/these selections won't be saved or applied/)).toBeInTheDocument();
      fireEvent.change(screen.getByRole('textbox', { name: 'Authorized Senders', exact: true }), { target: { value: 'sender@example.com' } });
      const checkboxes = screen.getAllByRole('checkbox');
      expect(checkboxes).toHaveLength(3);
      for (const checkbox of checkboxes) {
        expect(checkbox).not.toBeChecked();
        fireEvent.click(checkbox);
        expect(checkbox).toBeChecked();
      }
      if (action === 'Escape') {
        fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape', code: 'Escape', keyCode: 27 });
      } else {
        fireEvent.click(screen.getByRole('button', { name: action, exact: true }));
      }
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
      expect(store.getState().manageMembership.groupSettings).toBeUndefined();
      expect(store.getState().manageMembership.selectedDestination?.groupSettings).toBeUndefined();
      expect(store.getState().manageMembership.selectedDestination?.id).toBe('actual-id');
      expect(screen.getByRole('status')).toHaveTextContent(/preview-only selections were not saved or applied/i);
    }
  );
});
