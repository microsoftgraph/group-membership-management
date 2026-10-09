// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { CreateGroup } from './CreateGroup';
import { renderWithProviders } from '../../testing/renderWithProviders';
import manageMembershipReducer from '../../store/manageMembership.slice';
import { NewGroupType } from '../../models/NewGroupType';

const getManageMembershipState = () =>
  manageMembershipReducer(undefined, { type: 'test/init' });

const selectSecurity = () => fireEvent.click(screen.getByRole('radio', { name: /security/i }));

describe('CreateGroup', () => {
  it('defaults to Microsoft 365 and shows the alias field and the Microsoft 365 settings', () => {
    renderWithProviders(<CreateGroup onGroupCreated={vi.fn()} />);

    expect(screen.getByRole('radio', { name: /microsoft 365/i })).toBeChecked();
    expect(screen.getByPlaceholderText('Enter the alias of the group')).toBeInTheDocument();
    expect(screen.getAllByRole('switch').length).toBeGreaterThan(0);
  });

  it('hides the alias field and the Microsoft 365 settings when Security is selected', () => {
    renderWithProviders(<CreateGroup onGroupCreated={vi.fn()} />);

    selectSecurity();

    expect(screen.queryByPlaceholderText('Enter the alias of the group')).not.toBeInTheDocument();
    expect(screen.queryAllByRole('switch')).toHaveLength(0);
  });

  it('submits the selected group type and the typed alias for a Microsoft 365 group', () => {
    const onGroupCreated = vi.fn();
    renderWithProviders(<CreateGroup onGroupCreated={onGroupCreated} />);

    fireEvent.change(screen.getByPlaceholderText('Enter the name of the group'), { target: { value: 'Finance Team' } });
    fireEvent.change(screen.getByPlaceholderText('Enter the alias of the group'), { target: { value: 'fin-team' } });
    fireEvent.click(screen.getByRole('button', { name: /create group/i }));

    expect(onGroupCreated).toHaveBeenCalledWith('Finance Team', 'fin-team', NewGroupType.Microsoft365);
  });

  it('derives the alias from the group name for a security group', () => {
    const onGroupCreated = vi.fn();
    renderWithProviders(<CreateGroup onGroupCreated={onGroupCreated} />);

    fireEvent.change(screen.getByPlaceholderText('Enter the name of the group'), { target: { value: 'Finance Admins!' } });
    selectSecurity();
    fireEvent.click(screen.getByRole('button', { name: /create group/i }));

    // Graph requires a mail nickname on a security group even though the field is hidden.
    expect(onGroupCreated).toHaveBeenCalledWith('Finance Admins!', 'FinanceAdmins', NewGroupType.Security);
  });

  it('ignores an alias typed before switching to Security', () => {
    const onGroupCreated = vi.fn();
    renderWithProviders(<CreateGroup onGroupCreated={onGroupCreated} />);

    fireEvent.change(screen.getByPlaceholderText('Enter the name of the group'), { target: { value: 'Finance Admins' } });
    fireEvent.change(screen.getByPlaceholderText('Enter the alias of the group'), { target: { value: 'something-else' } });
    selectSecurity();
    fireEvent.click(screen.getByRole('button', { name: /create group/i }));

    expect(onGroupCreated).toHaveBeenCalledWith('Finance Admins', 'FinanceAdmins', NewGroupType.Security);
  });

  it('clears Microsoft 365 group settings when switching to Security', () => {
    const { store } = renderWithProviders(<CreateGroup onGroupCreated={vi.fn()} />, {
      preloadedState: {
        manageMembership: {
          ...getManageMembershipState(),
          groupSettings: {
            authorizedSenders: [],
            hiddenFromExchangeClients: false,
            welcomeMessageEnabled: true,
          },
        },
      },
    });

    selectSecurity();

    // Otherwise Exchange-only settings would be onboarded against a group that has no mailbox.
    expect(store.getState().manageMembership.groupSettings).toBeUndefined();
  });
});
