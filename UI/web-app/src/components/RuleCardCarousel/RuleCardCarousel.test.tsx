// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { RuleCardCarousel } from './RuleCardCarousel';
import { renderWithProviders } from '../../testing/renderWithProviders';
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

describe('RuleCardCarousel', () => {
  it('renders a card for each rule', () => {
    const parts = [makePart('a', 'Rule A'), makePart('b', 'Rule B')];

    renderWithProviders(
      <RuleCardCarousel parts={parts} selectedPartId="a" onSelectPart={() => {}} />
    );

    expect(screen.getByText('Rule A')).toBeInTheDocument();
    expect(screen.getByText('Rule B')).toBeInTheDocument();
  });

  it('renders scroll buttons', () => {
    renderWithProviders(
      <RuleCardCarousel parts={[makePart('a', 'Rule A')]} onSelectPart={() => {}} />
    );

    expect(screen.getByRole('button', { name: 'Scroll rules left' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Scroll rules right' })).toBeInTheDocument();
  });

  it('invokes onSelectPart with the rule id when a card is clicked', () => {
    const onSelectPart = vi.fn();
    const parts = [makePart('a', 'Rule A'), makePart('b', 'Rule B')];

    renderWithProviders(
      <RuleCardCarousel parts={parts} onSelectPart={onSelectPart} />
    );

    fireEvent.click(screen.getByRole('button', { name: /Rule B/ }));

    expect(onSelectPart).toHaveBeenCalledWith('b');
  });
});
