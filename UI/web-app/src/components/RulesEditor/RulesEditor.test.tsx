// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RulesEditor } from './RulesEditor';
import { renderWithProviders } from '../../testing/renderWithProviders';
import manageMembershipReducer from '../../store/manageMembership.slice';
import rolesReducer from '../../store/roles.slice';
import settingsReducer from '../../store/settings.slice';
import { SettingKey } from '../../models/SettingKey';
import type { RootState } from '../../store';
import type { ISourcePart } from '../../models/ISourcePart';
import type { HRSourcePart } from '../../models/HRSourcePart';
import type { GroupMembershipSourcePart } from '../../models/GroupMembershipSourcePart';
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

const buildState = (
  parts: ISourcePart[],
  isWriter = true,
  isAIOnboardingChat = false
): Partial<RootState> => {
  const manageMembership = {
    ...manageMembershipReducer(undefined, { type: '@@INIT' }),
    sourceParts: parts,
  };
  const roles = {
    ...rolesReducer(undefined, { type: '@@INIT' }),
    isJobOwnerWriter: isWriter,
    isAIOnboardingChat,
  };
  return { manageMembership, roles } as unknown as Partial<RootState>;
};

// RulesEditor renders CopilotTriggerButton, which reads the current route to
// decide whether the Copilot entry point applies, so it needs router context.
const renderEditor = (
  options?: Parameters<typeof renderWithProviders>[1],
  initialEntry = '/'
) =>
  renderWithProviders(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Routes>
        <Route path="/ManageMembership/:jobId" element={<RulesEditor />} />
        <Route path="*" element={<RulesEditor />} />
      </Routes>
    </MemoryRouter>,
    options
  );

describe('RulesEditor', () => {
  it('offers one Copilot action and one manual action for eligible empty onboarding', () => {
    const { store } = renderEditor({ preloadedState: buildState([], true, true) });
    expect(screen.getByText('Not sure where to start?')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Add a rule' })).toBeInTheDocument();
    expect(screen.getAllByTestId('copilot-trigger-button')).toHaveLength(1);
    expect(screen.getAllByRole('button', { name: 'Add' })).toHaveLength(1);
    fireEvent.click(screen.getByTestId('copilot-trigger-button'));
    expect(store.getState().copilot.isPanelOpen).toBe(true);
    expect(store.getState().manageMembership.sourceParts).toHaveLength(0);
  });

  it('switches from the onboarding choice cards to the editor when Add is selected', () => {
    const { store } = renderEditor({ preloadedState: buildState([], true, true) });
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(store.getState().manageMembership.sourceParts).toHaveLength(1);
    expect(screen.queryByTestId('copilot-onboarding-choices')).not.toBeInTheDocument();
    expect(screen.getByTestId('copilot-trigger-button')).toBeInTheDocument();
  });

  it('retains the manual empty state when the Copilot feature flag is off', () => {
    renderEditor({ preloadedState: {
      ...buildState([], true, true),
      settings: {
        ...settingsReducer(undefined, { type: '@@INIT' }),
        settings: [{ settingKey: SettingKey.IsAICopilotEnabled, settingValue: 'false' }],
      },
    } });
    expect(screen.getByText('No rules yet')).toBeInTheDocument();
    expect(screen.queryByTestId('copilot-onboarding-choices')).not.toBeInTheDocument();
    expect(screen.queryByTestId('copilot-trigger-button')).not.toBeInTheDocument();
  });

  it('does not present onboarding choice cards when an existing membership is empty', () => {
    const state = buildState([], true, true);
    renderEditor({ preloadedState: {
      ...state,
      manageMembership: {
        ...manageMembershipReducer(undefined, { type: '@@INIT' }),
        isEditingExistingJob: true,
      },
    } }, '/ManageMembership/existing-job-id');
    expect(screen.queryByTestId('copilot-onboarding-choices')).not.toBeInTheDocument();
    expect(screen.getByText('No rules yet')).toBeInTheDocument();
  });

  it('disables both onboarding choices for a non-writer', () => {
    renderEditor({ preloadedState: buildState([], false, true) });
    expect(screen.getByTestId('copilot-trigger-button')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
  });

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

  it('duplicates the exact SQL identity selection without sharing the query object', () => {
    const source = makePart('a', 'Rule A');
    source.query = { ...(source.query as HRSourcePart), identityTypes: ['agents', 'users'] };
    const { store } = renderEditor({
      preloadedState: buildState([source]),
    });

    fireEvent.click(screen.getByRole('button', { name: 'Duplicate rule: Rule A' }));

    const parts = store.getState().manageMembership.sourceParts;
    expect((parts[1].query as HRSourcePart).identityTypes).toEqual(['agents', 'users']);
    expect(parts[1].query).not.toBe(parts[0].query);
  });

  it('duplicates the exact group identity selection without sharing the query object', () => {
    const source = makePart('a', 'Rule A');
    source.query = {
      type: SourcePartType.GroupMembership,
      source: '123e4567-e89b-12d3-a456-426614174000',
      exclusionary: true,
      identityTypes: ['agents', 'users'],
    };
    const { store } = renderEditor({
      preloadedState: buildState([source]),
    });

    fireEvent.click(screen.getByRole('button', { name: 'Duplicate rule: Rule A' }));

    const parts = store.getState().manageMembership.sourceParts;
    expect((parts[1].query as GroupMembershipSourcePart).identityTypes).toEqual(['agents', 'users']);
    expect(parts[1].query).not.toBe(parts[0].query);
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

  it('shows the Copilot update trigger for the same AI role as onboarding', () => {
    renderEditor(
      { preloadedState: buildState([makePart('a', 'Rule A')], true, true) },
      '/ManageMembership/existing-job-id'
    );

    expect(
      screen.getByRole('button', { name: 'Let Copilot resume building it for you' })
    ).toBeEnabled();
  });

  it('keeps the Copilot update trigger hidden without the AI role', () => {
    renderEditor(
      { preloadedState: buildState([makePart('a', 'Rule A')], true, false) },
      '/ManageMembership/existing-job-id'
    );

    expect(screen.queryByTestId('copilot-trigger-button')).not.toBeInTheDocument();
  });
});