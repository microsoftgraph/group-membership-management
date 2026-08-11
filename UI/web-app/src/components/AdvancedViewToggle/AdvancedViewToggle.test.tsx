// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { AdvancedViewToggle } from './AdvancedViewToggle';
import { renderWithProviders } from '../../testing/renderWithProviders';
import manageMembershipReducer from '../../store/manageMembership.slice';
import rolesReducer from '../../store/roles.slice';
import type { RootState } from '../../store';
import type { ISourcePart } from '../../models/ISourcePart';
import { SourcePartType } from '../../models/SourcePartType';

const getManageMembershipState = (): RootState['manageMembership'] =>
  manageMembershipReducer(undefined, { type: 'test/init' });

const getRolesState = (): RootState['roles'] =>
  rolesReducer(undefined, { type: 'test/init' });

const createSourcePart = (): ISourcePart => ({
  id: 'part-1',
  title: 'Part one',
  query: {
    type: SourcePartType.GroupMembership,
    source: '123e4567-e89b-12d3-a456-426614174000',
    exclusionary: false,
  },
  isNew: false,
  isExpanded: false,
});

const buildState = (
  roles: Partial<RootState['roles']>,
  manageMembership: Partial<RootState['manageMembership']> = {}
): Partial<RootState> => ({
  manageMembership: { ...getManageMembershipState(), ...manageMembership },
  roles: { ...getRolesState(), ...roles },
});

describe('AdvancedViewToggle', () => {
  it('does not render for users without a job tenant role', () => {
    renderWithProviders(<AdvancedViewToggle />, {
      preloadedState: buildState({ isJobOwnerWriter: true }),
    });

    expect(screen.queryByRole('switch')).not.toBeInTheDocument();
  });

  it('renders for job tenant writers', () => {
    renderWithProviders(<AdvancedViewToggle />, {
      preloadedState: buildState({ isJobTenantWriter: true }),
    });

    expect(screen.getByRole('switch')).toBeInTheDocument();
  });

  it('renders for job tenant readers', () => {
    renderWithProviders(<AdvancedViewToggle />, {
      preloadedState: buildState({ isJobTenantReader: true }),
    });

    expect(screen.getByRole('switch')).toBeInTheDocument();
  });

  it('shows the advanced query without modifying source parts for readers', () => {
    const part = createSourcePart();
    const { store } = renderWithProviders(<AdvancedViewToggle />, {
      preloadedState: buildState({ isJobTenantReader: true }, { sourceParts: [part] }),
    });

    fireEvent.click(screen.getByRole('switch'));

    const state = store.getState().manageMembership;
    expect(state.isAdvancedView).toBe(true);
    expect(state.advancedViewQuery).toContain('GroupMembership');
    expect(state.sourceParts).toEqual([part]);
  });

  it('leaves source parts untouched when a reader toggles back to the regular view', () => {
    const part = createSourcePart();
    const { store } = renderWithProviders(<AdvancedViewToggle />, {
      preloadedState: buildState(
        { isJobTenantReader: true },
        { sourceParts: [part], isAdvancedView: true, advancedViewQuery: '[]' }
      ),
    });

    fireEvent.click(screen.getByRole('switch'));

    const state = store.getState().manageMembership;
    expect(state.isAdvancedView).toBe(false);
    expect(state.sourceParts).toEqual([part]);
  });

  it('converts the advanced query back into source parts for writers', () => {
    const part = createSourcePart();
    const { store } = renderWithProviders(<AdvancedViewToggle />, {
      preloadedState: buildState(
        { isJobTenantWriter: true },
        {
          sourceParts: [part],
          isAdvancedView: true,
          isAdvancedQueryValid: true,
          advancedViewQuery: JSON.stringify([
            { type: SourcePartType.GroupMembership, source: 'abc', exclusionary: false },
          ]),
        }
      ),
    });

    fireEvent.click(screen.getByRole('switch'));

    const state = store.getState().manageMembership;
    expect(state.isAdvancedView).toBe(false);
    expect(state.sourceParts).toHaveLength(1);
    expect(state.sourceParts[0].query).toEqual({
      type: SourcePartType.GroupMembership,
      source: 'abc',
      exclusionary: false,
    });
  });

  it('honors an explicit readOnly override for writers', () => {
    const part = createSourcePart();
    const { store } = renderWithProviders(<AdvancedViewToggle readOnly />, {
      preloadedState: buildState(
        { isJobTenantWriter: true },
        {
          sourceParts: [part],
          isAdvancedView: true,
          isAdvancedQueryValid: true,
          advancedViewQuery: JSON.stringify([
            { type: SourcePartType.GroupMembership, source: 'abc', exclusionary: false },
          ]),
        }
      ),
    });

    fireEvent.click(screen.getByRole('switch'));

    const state = store.getState().manageMembership;
    expect(state.isAdvancedView).toBe(false);
    expect(state.sourceParts).toEqual([part]);
  });
});
