// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { screen } from '@testing-library/react';
import { RulesReview } from './RulesReview';
import { renderWithProviders } from '../../testing/renderWithProviders';
import groupPartReducer from '../../store/groupPart.slice';
import type { RootState } from '../../store';
import type { ISourcePart } from '../../models/ISourcePart';
import { SourcePartType } from '../../models/SourcePartType';

const makePart = (id: string, title: string): ISourcePart => ({
  id,
  title,
  query: {
    type: SourcePartType.HR,
    source: { manager: { id: undefined, depth: undefined }, filter: "x <> ''" },
    exclusionary: false,
  },
  isNew: false,
  isExpanded: false,
});

const getGroupPartState = (): RootState['groupPart'] =>
  groupPartReducer(undefined, { type: 'test/init' });

describe('RulesReview', () => {
  it('renders the RULES header and a card for each rule', () => {
    const parts = [makePart('a', 'Rule A'), makePart('b', 'Rule B')];

    renderWithProviders(<RulesReview parts={parts} />);

    expect(screen.getByText('Rule A')).toBeInTheDocument();
    expect(screen.getByText('Rule B')).toBeInTheDocument();
  });

  it('hides the header when showHeader is false', () => {
    renderWithProviders(<RulesReview parts={[makePart('a', 'Rule A')]} showHeader={false} />);

    // The "RULES" title text should not be present when the header is hidden.
    expect(screen.queryByText(/^RULES$/i)).not.toBeInTheDocument();
    expect(screen.getByText('Rule A')).toBeInTheDocument();
  });

  it('renders nothing card-related when there are no rules', () => {
    renderWithProviders(<RulesReview parts={[]} />);

    expect(screen.queryByRole('button', { name: /Scroll rules/ })).not.toBeInTheDocument();
  });

  it('omits the details panel for an HR/SqlMembership rule with no attribute filter (org leader only)', () => {
    const orgLeaderOnlyPart: ISourcePart = {
      ...makePart('a', 'Rule A'),
      query: {
        type: SourcePartType.HR,
        source: { manager: { id: 1, depth: 1 }, filter: '' },
        exclusionary: false,
      },
    };

    renderWithProviders(<RulesReview parts={[orgLeaderOnlyPart]} />);

    // The card still renders, but there is no attribute table to point at below the carousel.
    expect(screen.getByText('Rule A')).toBeInTheDocument();
    expect(screen.queryByTestId('hr-attributes-table')).not.toBeInTheDocument();
  });

  it('shows group name and alias on the card without rendering the redundant people picker', () => {
    const groupRule: ISourcePart = {
      ...makePart('group-rule', 'All Users in Group Test'),
      query: {
        type: SourcePartType.GroupMembership,
        source: 'group-guid',
        exclusionary: false,
      },
    };
    const preloadedState: Partial<RootState> = {
      groupPart: {
        ...getGroupPartState(),
        searchResults: [
          { key: 0, text: 'Group Test', secondaryText: 'group_alias', id: 'group-guid' },
        ],
      },
    };

    renderWithProviders(<RulesReview parts={[groupRule]} />, { preloadedState });

    expect(screen.getAllByText('Group Test').length).toBeGreaterThan(0);
    expect(screen.getAllByText('group_alias').length).toBeGreaterThan(0);
    expect(screen.queryByLabelText('Search group name')).not.toBeInTheDocument();
  });
});
