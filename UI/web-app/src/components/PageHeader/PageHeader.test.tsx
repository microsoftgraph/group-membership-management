// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi } from 'vitest';

import { renderWithProviders } from '../../testing/renderWithProviders';
import { PageHeader } from './PageHeader';
import { defaultStrings } from '../../services/localization';

const mockNavigate = jest.fn();

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
});

vi.mock('../Banner', () => ({
  Banner: () => <div data-testid="banner" />,
}));

describe('PageHeader', () => {
  beforeEach(() => {
    mockNavigate.mockClear();
  });

  it('renders the back button, banner, and children by default', () => {
    renderWithProviders(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <PageHeader>
          <div>Header Content</div>
        </PageHeader>
      </MemoryRouter>
    );

    expect(screen.getByRole('button', { name: defaultStrings.back })).toBeInTheDocument();
    expect(screen.getByTestId('banner')).toBeInTheDocument();
    expect(screen.getByText('Header Content')).toBeInTheDocument();
  });

  it('navigates back when the default back button is clicked', () => {
    renderWithProviders(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <PageHeader />
      </MemoryRouter>
    );

    fireEvent.click(screen.getByRole('button', { name: defaultStrings.back }));

    expect(mockNavigate).toHaveBeenCalledWith(-1);
  });

  it('hides the back button when requested', () => {
    renderWithProviders(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <PageHeader backButtonHidden>
          <div />
        </PageHeader>
      </MemoryRouter>
    );

    expect(screen.queryByRole('button', { name: defaultStrings.back })).toBeNull();
  });

  it('prefers the custom dashboard handler when supplied', () => {
    const customHandler = jest.fn();

    renderWithProviders(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <PageHeader onBackToDashboardButtonClick={customHandler} />
      </MemoryRouter>
    );

    fireEvent.click(screen.getByRole('button', { name: defaultStrings.backToDashboard }));

    expect(customHandler).toHaveBeenCalledTimes(1);
    expect(mockNavigate).not.toHaveBeenCalled();
  });
});
