import React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { AdvancedViewSourcePart } from './AdvancedViewSourcePart';
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
  title: '',
  query: {
    type: SourcePartType.GroupOwnership,
    source: ['All'],
    exclusionary: false,
  },
  isNew: false,
  isExpanded: false,
});

describe('AdvancedViewSourcePart', () => {
  it('renders the existing source part query', () => {
    const part = createSourcePart();
    const preloadedState: Partial<RootState> = {
      manageMembership: { ...getManageMembershipState(), sourceParts: [part] },
      roles: { ...getRolesState(), isJobOwnerWriter: true },
    };

    renderWithProviders(
      <AdvancedViewSourcePart part={part} isEditable />,
      { preloadedState }
    );

    expect(screen.getByRole('textbox', { name: 'Query' })).toHaveValue(
      JSON.stringify(part.query)
    );
  });

  it('accepts a valid query and updates the store', async () => {
    const part = createSourcePart();
    const preloadedState: Partial<RootState> = {
      manageMembership: { ...getManageMembershipState(), sourceParts: [part] },
      roles: { ...getRolesState(), isJobOwnerWriter: true },
    };

    const { store } = renderWithProviders(
      <AdvancedViewSourcePart part={part} isEditable />,
      { preloadedState }
    );

    const input = screen.getByRole('textbox', { name: 'Query' });
    const nextQuery = '{"type":"GroupOwnership","source":["Hybrid"],"exclusionary":false}';

    fireEvent.change(input, { target: { value: nextQuery } });
    fireEvent.blur(input);

    await waitFor(() =>
      expect(screen.getByText('Query is valid.')).toBeInTheDocument()
    );

    expect(store.getState().manageMembership.sourceParts[0].query).toEqual({
      type: SourcePartType.GroupOwnership,
      source: ['Hybrid'],
      exclusionary: false,
    });
  });

  it('shows an error when the query is not valid JSON', async () => {
    const part = createSourcePart();
    const preloadedState: Partial<RootState> = {
      manageMembership: { ...getManageMembershipState(), sourceParts: [part] },
      roles: { ...getRolesState(), isJobOwnerWriter: true },
    };

    renderWithProviders(
      <AdvancedViewSourcePart part={part} isEditable />,
      { preloadedState }
    );

    const input = screen.getByRole('textbox', { name: 'Query' });

    fireEvent.change(input, { target: { value: '{' } });
    fireEvent.blur(input);

    await waitFor(() =>
      expect(
        screen.getByText('Failed to parse query. Ensure it is valid JSON.')
      ).toBeInTheDocument()
    );
  });
});
