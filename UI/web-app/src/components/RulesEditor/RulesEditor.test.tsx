// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { RulesEditor } from './RulesEditor';
import { renderWithProviders } from '../../testing/renderWithProviders';
import manageMembershipReducer from '../../store/manageMembership.slice';
import rolesReducer from '../../store/roles.slice';
import type { RootState } from '../../store';
import type { ISourcePart } from '../../models/ISourcePart';
import { SourcePartType } from '../../models/SourcePartType';

const makePart = (id: string, title: string): ISourcePart => ({
  id,
  title,
  query: {
    type: SourcePartType.HR,
    source: { manager: { id: undefined, depth: undefined }, filter: "email <> ''" },
    exclusionary: false,
  },
  isNew: false,
  isExpanded: false,
});

const buildState = (parts: ISourcePart[], isWriter = true): Partial<RootState> => {
  const manageMembership = {
    ...manageMembershipReducer(undefined, { type: '@@INIT' }),
    sourceParts: parts,
  };
  const roles = {
    ...rolesReducer(undefined, { type: '@@INIT' }),
    isJobOwnerWriter: isWriter,
  };
  return { manageMembership, roles } as unknown as Partial<RootState>;
};

// RulesEditor renders CopilotTriggerButton, which reads the current route to
// decide whether the Copilot entry point applies, so it needs router context.
const renderEditor = (options?: Parameters<typeof renderWithProviders>[1]) =>
  renderWithProviders(
    <MemoryRouter>
      <RulesEditor />
    </MemoryRouter>,
    options
  );

describe('RulesEditor', () => {
  it('renders the empty state with a prominent Add when there are no rules', () => {
    renderEditor({ preloadedState: buildState([]) });

    expect(screen.getByText('No rules yet')).toBeInTheDocument();
    // No carousel is rendered in the empty state.
    expect(screen.queryByRole('button', { name: /Scroll rules/ })).not.toBeInTheDocument();
    // There is at least one Add action available.
    expect(screen.getAllByRole('button', { name: /^Add$/ }).length).toBeGreaterThan(0);
  });

  it('adds a rule from the empty state', () => {
    const { store } = renderEditor({ preloadedState: buildState([]) });

    fireEvent.click(screen.getAllByRole('button', { name: /^Add$/ })[0]);

    expect(store.getState().manageMembership.sourceParts).toHaveLength(1);
  });

  it('selects the first rule by default', () => {
    renderEditor({
      preloadedState: buildState([makePart('a', 'Rule A'), makePart('b', 'Rule B')]),
    });

    expect(screen.getByRole('button', { name: 'Select rule: Rule A', pressed: true })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Select rule: Rule B', pressed: false })).toBeInTheDocument();
  });

  it('duplicates a rule immediately after its source', () => {
    const { store } = renderEditor({
      preloadedState: buildState([makePart('a', 'Rule A'), makePart('b', 'Rule B')]),
    });

    fireEvent.click(screen.getByRole('button', { name: 'Duplicate rule: Rule A' }));

    const parts = store.getState().manageMembership.sourceParts;
    expect(parts).toHaveLength(3);
    expect(parts[0].id).toBe('a');
    // The copy is inserted immediately after its source and keeps the source's title.
    expect(parts[1].title).toBe('Rule A');
    expect(parts[1].id).not.toBe('a');
    expect(parts[2].id).toBe('b');
  });

  it('deletes a rule from the store', () => {
    const { store } = renderEditor({
      preloadedState: buildState([makePart('a', 'Rule A'), makePart('b', 'Rule B')]),
    });

    fireEvent.click(screen.getByRole('button', { name: 'Delete rule: Rule B' }));

    const parts = store.getState().manageMembership.sourceParts;
    expect(parts.map((p) => p.id)).toEqual(['a']);
  });

  it('disables the Add action when the user is not a job writer', () => {
    renderEditor({ preloadedState: buildState([], false) });

    screen.getAllByRole('button', { name: /^Add$/ }).forEach((button) => {
      expect(button).toBeDisabled();
    });
  });
});
