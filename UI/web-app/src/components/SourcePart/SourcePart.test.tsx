// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { SourcePart } from './SourcePart';
import { renderWithProviders } from '../../testing/renderWithProviders';
import rolesReducer from '../../store/roles.slice';
import manageMembershipReducer from '../../store/manageMembership.slice';
import type { RootState } from '../../store';
import type { ISourcePart } from '../../models/ISourcePart';
import { SourcePartType } from '../../models/SourcePartType';
import { DestinationType } from '../../models/DestinationType';

const getRolesState = (overrides: Partial<RootState['roles']> = {}): RootState['roles'] => ({
  ...rolesReducer(undefined, { type: 'test/init' }),
  // A job writer may edit source parts; this enables the source-type dropdown.
  isJobOwnerWriter: true,
  ...overrides,
});

const getManageMembershipState = (
  destinationOverride?: Partial<RootState['manageMembership']['selectedDestination']>
): RootState['manageMembership'] => {
  const base = manageMembershipReducer(undefined, { type: 'test/init' });
  return {
    ...base,
    selectedDestination: destinationOverride as RootState['manageMembership']['selectedDestination'],
  };
};

const teamsChannelDestination = {
  id: 'group-002',
  name: 'Engineering-All',
  type: DestinationType.TeamsChannelMembership,
  channelId: 'channel-001',
  channelName: 'General',
};

const groupDestination = {
  id: 'group-002',
  name: 'Engineering-All',
  type: DestinationType.GroupMembership,
};

const mismatchedTeamsChannelDestination = {
  id: 'other-group',
  name: 'Other Team',
  type: DestinationType.TeamsChannelMembership,
  channelId: 'other-channel',
  channelName: 'Other Channel',
};

const groupPart = (): ISourcePart => ({
  id: 'part-1',
  title: 'All Users in Group',
  query: { type: SourcePartType.GroupMembership, source: '', exclusionary: false },
  isNew: false,
  isExpanded: true,
});

const teamsChannelPart = (): ISourcePart => ({
  id: 'part-1',
  title: 'General',
  query: {
    type: SourcePartType.TeamsChannelMembership,
    source: { objectId: 'group-002', channelId: 'channel-001' },
    exclusionary: false,
  },
  isNew: false,
  isExpanded: true,
});

const renderSourcePart = (part: ISourcePart, destination: any, roleOverrides: Partial<RootState['roles']> = {}) =>
  renderWithProviders(
    <SourcePart partId={part.id} query={part.query} part={part} isEditable />,
    {
      preloadedState: {
        roles: getRolesState(roleOverrides),
        manageMembership: getManageMembershipState(destination),
      } as Partial<RootState>,
    }
  );

describe('SourcePart — TeamsChannel source', () => {
  it('offers the Teams Channel source option when the destination is a Teams channel and the user is a Teams channel onboarder', () => {
    renderSourcePart(groupPart(), teamsChannelDestination, { isTeamsChannelOnboarder: true });

    // The inclusionary ChoiceGroup is radios; the source-type Dropdown is identified by its label.
    fireEvent.click(screen.getByRole('combobox', { name: /select rule type/i }));

    expect(screen.getByRole('option', { name: 'Teams Channel' })).toBeInTheDocument();
  });

  it('offers the Teams Channel source option when the user is a tenant job writer', () => {
    renderSourcePart(groupPart(), teamsChannelDestination, { isJobTenantWriter: true });

    fireEvent.click(screen.getByRole('combobox', { name: /select rule type/i }));

    expect(screen.getByRole('option', { name: 'Teams Channel' })).toBeInTheDocument();
  });

  it('hides the Teams Channel source option when the user lacks onboarder and tenant-writer roles', () => {
    renderSourcePart(groupPart(), teamsChannelDestination);

    fireEvent.click(screen.getByRole('combobox', { name: /select rule type/i }));

    expect(screen.queryByRole('option', { name: 'Teams Channel' })).not.toBeInTheDocument();
    // Sanity: the dropdown did open.
    expect(screen.getByRole('option', { name: 'Group Membership' })).toBeInTheDocument();
  });

  it('keeps the Teams Channel option available for an already-configured Teams channel part even without any role', () => {
    // A job writer without the tenant-writer/onboarder role opens a job that already has a Teams
    // channel source part. The option must remain in the dropdown so it renders coherently (not
    // blank) and the selection cannot be silently lost.
    renderSourcePart(teamsChannelPart(), teamsChannelDestination);

    fireEvent.click(screen.getByRole('combobox', { name: /select rule type/i }));

    expect(screen.getByRole('option', { name: 'Teams Channel' })).toBeInTheDocument();
  });

  it('hides the Teams Channel source option when the destination is a group', () => {
    renderSourcePart(groupPart(), groupDestination);

    fireEvent.click(screen.getByRole('combobox', { name: /select rule type/i }));

    expect(screen.queryByRole('option', { name: 'Teams Channel' })).not.toBeInTheDocument();
    // Sanity: the group option is present so the dropdown did open.
    expect(screen.getByRole('option', { name: 'Group Membership' })).toBeInTheDocument();
  });

  it('renders a read-only Teams Channel source with team + channel names and a forced-inclusionary radio', () => {
    const { container } = renderSourcePart(teamsChannelPart(), teamsChannelDestination, { isTeamsChannelOnboarder: true });

    // An info MessageBar carries the read-only caveat. Fluent v8 MessageBar does not
    // render its string children into the DOM under jsdom, so assert its presence
    // rather than its text (the text is covered by localization unit tests).
    expect(container.querySelector('.ms-MessageBar')).toBeInTheDocument();

    // Both the team name and channel name from the destination are shown in disabled, read-only fields.
    const teamField = screen.getByDisplayValue('Engineering-All') as HTMLInputElement;
    expect(teamField).toBeDisabled();
    expect(teamField.readOnly).toBe(true);

    const channelField = screen.getByDisplayValue('General') as HTMLInputElement;
    expect(channelField).toBeDisabled();
    expect(channelField.readOnly).toBe(true);

    // Exclusionary is not selectable for a Teams channel source: include/exclude radios are disabled.
    const radios = screen.getAllByRole('radio') as HTMLInputElement[];
    radios.forEach((radio) => expect(radio).toBeDisabled());
  });

  it('falls back to the raw source ids when the part channel does not match the selected destination', () => {
    // Guards against showing a team/channel name that differs from what will actually be saved:
    // when the destination ids do not match the part's stored source ids, show the raw ids.
    renderSourcePart(teamsChannelPart(), mismatchedTeamsChannelDestination, { isTeamsChannelOnboarder: true });

    expect(screen.getByDisplayValue('group-002')).toBeInTheDocument();
    expect(screen.getByDisplayValue('channel-001')).toBeInTheDocument();
    expect(screen.queryByDisplayValue('Other Team')).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue('Other Channel')).not.toBeInTheDocument();
  });
});
