import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { vi } from 'vitest';
import { renderWithProviders } from '../testing/renderWithProviders';
import AddOwner from './AddOwner';

const mockNavigate = jest.fn();

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
});

describe('AddOwner', () => {
  beforeEach(() => {
    mockNavigate.mockClear();
  });

  it('renders the add owner button', () => {
    renderWithProviders(<AddOwner />);

    expect(
      screen.getByRole('button', { name: 'Add GMM as an owner' })
    ).toBeInTheDocument();
  });

  it('navigates to the owner page when clicked', () => {
    renderWithProviders(<AddOwner />);

    fireEvent.click(screen.getByRole('button', { name: 'Add GMM as an owner' }));

    expect(mockNavigate).toHaveBeenCalledWith('/OwnerPage', {
      replace: false,
      state: { item: 1 },
    });
  });
});
