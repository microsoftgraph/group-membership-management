import React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { vi } from 'vitest';
import { AdvancedQuery } from './AdvancedQuery';
import { renderWithProviders } from '../../testing/renderWithProviders';
import manageMembershipReducer from '../../store/manageMembership.slice';
import rolesReducer from '../../store/roles.slice';
import type { RootState } from '../../store';

const mockValidateQuery = jest.fn(async (_query: unknown, setMessage?: (message: React.ReactNode) => void) => {
  setMessage?.('Validation complete');
  return true;
});

vi.mock('../../store/hooks', async () => {
  const actual = await vi.importActual<typeof import('../../store/hooks')>('../../store/hooks');
  return {
    ...actual,
    useQueryValidation: () => ({
      validateQuery: mockValidateQuery,
    }),
  };
});

const getManageMembershipState = (): RootState['manageMembership'] =>
  manageMembershipReducer(undefined, { type: 'test/init' });

const getRolesState = (): RootState['roles'] =>
  rolesReducer(undefined, { type: 'test/init' });

describe('AdvancedQuery', () => {
  beforeEach(() => {
    mockValidateQuery.mockClear();
  });

  it('shows the default placeholder query when nothing is supplied', () => {
    const preloadedState: Partial<RootState> = {
      manageMembership: { ...getManageMembershipState(), advancedViewQuery: '' },
      roles: { ...getRolesState(), isJobOwnerWriter: true },
    };

    renderWithProviders(
      <AdvancedQuery query="" onQueryChange={jest.fn()} partId={0} isEditable />,
      { preloadedState }
    );

    const textArea = screen.getByRole('textbox', { name: 'Query' }) as HTMLTextAreaElement;

    expect(textArea.value).toContain('SqlMembership');
  });

  it('notifies callers when the query text changes', () => {
    const onQueryChange = jest.fn();
    const preloadedState: Partial<RootState> = {
      manageMembership: { ...getManageMembershipState(), advancedViewQuery: '' },
      roles: { ...getRolesState(), isJobOwnerWriter: true },
    };

    renderWithProviders(
      <AdvancedQuery query="[]" onQueryChange={onQueryChange} partId={0} isEditable />,
      { preloadedState }
    );

    fireEvent.change(screen.getByRole('textbox', { name: 'Query' }), {
      target: { value: '[1]' },
    });

    expect(onQueryChange).toHaveBeenCalledWith(expect.any(Object), '[1]');
  });

  it('dispatches the updated query and runs validation on blur', async () => {
    mockValidateQuery.mockImplementation(async (_query: unknown, setMessage?: (message: React.ReactNode) => void) => {
      setMessage?.('Query looks good');
      return true;
    });

    const preloadedState: Partial<RootState> = {
      manageMembership: { ...getManageMembershipState(), advancedViewQuery: '' },
      roles: { ...getRolesState(), isJobOwnerWriter: true },
    };

    const { store } = renderWithProviders(
      <AdvancedQuery query="[]" onQueryChange={jest.fn()} partId={0} isEditable />,
      { preloadedState }
    );

    const textbox = screen.getByRole('textbox', { name: 'Query' });
    const updatedJson = '[{"type":"GroupMembership","source":"123"}]';

    fireEvent.change(textbox, { target: { value: updatedJson } });
    fireEvent.blur(textbox);

    await waitFor(() => expect(mockValidateQuery).toHaveBeenCalled());

    expect(mockValidateQuery).toHaveBeenCalledWith(
      [{ type: 'GroupMembership', source: '123' }],
      expect.any(Function)
    );
    expect(store.getState().manageMembership.advancedViewQuery).toEqual(updatedJson);
    expect(screen.getByText('Query looks good')).toBeInTheDocument();
  });
});
