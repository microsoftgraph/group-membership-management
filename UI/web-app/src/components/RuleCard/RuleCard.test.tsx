// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { RuleCard } from './RuleCard';
import { renderWithProviders } from '../../testing/renderWithProviders';
import groupPartReducer from '../../store/groupPart.slice';
import titleReducer from '../../store/title.slice';
import type { RootState } from '../../store';
import type { ISourcePart } from '../../models/ISourcePart';
import { SourcePartType } from '../../models/SourcePartType';

const getGroupPartState = (): RootState['groupPart'] =>
  groupPartReducer(undefined, { type: 'test/init' });

const getTitleState = (): RootState['title'] =>
  titleReducer(undefined, { type: 'test/init' });

const hrPart = (overrides: Partial<ISourcePart> = {}): ISourcePart => ({
  id: 'hr-1',
  title: 'Specific Email Users',
  query: {
    type: SourcePartType.HR,
    source: { manager: { id: undefined, depth: undefined }, filter: "email <> ''" },
    exclusionary: false,
  },
  isNew: false,
  isExpanded: false,
  ...overrides,
});

const groupPart = (overrides: Partial<ISourcePart> = {}): ISourcePart => ({
  id: 'group-1',
  title: 'All Users in Group test 1',
  query: {
    type: SourcePartType.GroupMembership,
    source: 'group-guid',
    exclusionary: false,
  },
  isNew: false,
  isExpanded: false,
  ...overrides,
});

describe('RuleCard', () => {
  it('renders an inclusive HR rule with Org leader and Depth not set', () => {
    renderWithProviders(<RuleCard part={hrPart()} onSelect={() => {}} />);

    expect(screen.getByText('Inclusive')).toBeInTheDocument();
    expect(screen.getByText('HR')).toBeInTheDocument();
    expect(screen.getByText('Specific Email Users')).toBeInTheDocument();
    expect(screen.getByText('Org leader:')).toBeInTheDocument();
    expect(screen.getByText('Depth:')).toBeInTheDocument();
    expect(screen.getAllByText('Not set')).toHaveLength(2);
  });

  it('renders the Exclusive badge for an exclusionary rule', () => {
    renderWithProviders(
      <RuleCard part={hrPart({ query: { ...hrPart().query, exclusionary: true } })} onSelect={() => {}} />
    );

    expect(screen.getByText('Exclusive')).toBeInTheDocument();
    expect(screen.queryByText('Inclusive')).not.toBeInTheDocument();
  });

  it('renders a Group rule with Name and Alias from the resolved persona', () => {
    const preloadedState: Partial<RootState> = {
      groupPart: {
        ...getGroupPartState(),
        searchResults: [
          { key: 0, text: 'Group Test 1', secondaryText: 'group_alias', id: 'group-guid' },
        ],
      },
    };

    renderWithProviders(<RuleCard part={groupPart()} onSelect={() => {}} />, { preloadedState });

    expect(screen.getByText('Group')).toBeInTheDocument();
    expect(screen.getByText('Name:')).toBeInTheDocument();
    expect(screen.getByText('Alias:')).toBeInTheDocument();
    expect(screen.getAllByText('Group Test 1').length).toBeGreaterThan(0);
    expect(screen.getAllByText('group_alias').length).toBeGreaterThan(0);
  });

  it('invokes onSelect with the rule id when clicked', () => {
    const onSelect = vi.fn();
    renderWithProviders(<RuleCard part={hrPart()} onSelect={onSelect} />);

    fireEvent.click(screen.getByRole('button', { name: /Specific Email Users/ }));

    expect(onSelect).toHaveBeenCalledWith('hr-1');
  });

  it('does not render Duplicate/Delete actions by default (read-only contexts)', () => {
    renderWithProviders(<RuleCard part={hrPart()} onSelect={() => {}} />);

    expect(screen.queryByRole('button', { name: /Duplicate rule/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Delete rule/ })).not.toBeInTheDocument();
  });

  it('renders Duplicate/Delete actions and fires callbacks without selecting the card', () => {
    const onSelect = vi.fn();
    const onDuplicate = vi.fn();
    const onDelete = vi.fn();
    renderWithProviders(
      <RuleCard
        part={hrPart()}
        onSelect={onSelect}
        showActions
        onDuplicate={onDuplicate}
        onDelete={onDelete}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: /Duplicate rule: Specific Email Users/ }));
    expect(onDuplicate).toHaveBeenCalledWith('hr-1');

    fireEvent.click(screen.getByRole('button', { name: /Delete rule: Specific Email Users/ }));
    expect(onDelete).toHaveBeenCalledWith('hr-1');

    // Actions must not bubble up to card selection.
    expect(onSelect).not.toHaveBeenCalled();
  });

  it('shows the hidden-membership indicator when the referenced group is hidden', () => {
    const preloadedState: Partial<RootState> = {
      groupPart: {
        ...getGroupPartState(),
        searchResults: [
          { key: 0, text: 'Group Test 1', secondaryText: 'group_alias', id: 'group-guid' },
        ],
      },
      jobs: { selectedJob: { hiddenMembershipSourceIds: ['group-guid'] } } as unknown as RootState['jobs'],
    };

    renderWithProviders(<RuleCard part={groupPart()} onSelect={() => {}} />, { preloadedState });

    expect(screen.getByText('Hidden membership group')).toBeInTheDocument();
  });

  it('does not show the hidden-membership indicator when the group is not hidden', () => {
    const preloadedState: Partial<RootState> = {
      groupPart: {
        ...getGroupPartState(),
        searchResults: [
          { key: 0, text: 'Group Test 1', secondaryText: 'group_alias', id: 'group-guid' },
        ],
      },
      jobs: { selectedJob: { hiddenMembershipSourceIds: [] } } as unknown as RootState['jobs'],
    };

    renderWithProviders(<RuleCard part={groupPart()} onSelect={() => {}} />, { preloadedState });

    expect(screen.queryByText('Hidden membership group')).not.toBeInTheDocument();
  });

  it('shows the generating-title indicator while the rule title is being recalculated', () => {
    const preloadedState: Partial<RootState> = {
      title: { ...getTitleState(), partsGeneratingTitle: { 'hr-1': 1 } },
    };

    renderWithProviders(<RuleCard part={hrPart()} onSelect={() => {}} />, { preloadedState });

    expect(screen.getAllByText('Generating title...').length).toBeGreaterThan(0);
  });

  it('does not show the generating-title indicator for other rules', () => {
    const preloadedState: Partial<RootState> = {
      title: { ...getTitleState(), partsGeneratingTitle: { 'some-other-part': 1 } },
    };

    renderWithProviders(<RuleCard part={hrPart()} onSelect={() => {}} />, { preloadedState });

    expect(screen.queryByText('Generating title...')).not.toBeInTheDocument();
  });
});
