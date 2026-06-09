import React from 'react';
import { screen } from '@testing-library/react';
import { vi } from 'vitest';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { AppFooter } from './AppFooter';

vi.mock('../PageVersion', () => ({
  PageVersion: () => <div data-testid="page-version" />,
}));

vi.mock('../PrivacyPolicyLink', () => ({
  PrivacyPolicyLink: ({ className }: { className?: string }) => (
    <div data-testid="privacy-policy-link" className={className} />
  ),
}));

vi.mock('../PagingBar/PagingBar', () => ({
  PagingBar: () => <div data-testid="paging-bar" />,
}));

describe('AppFooter', () => {
  const basePagingBarState = {
    visible: true,
    pageSize: '10',
    pageNumber: 1,
    totalNumberOfPages: 0,
    sortKey: undefined,
    filterString: undefined,
    filterActionRequired: undefined,
    isSortedDescending: false,
    filterStatus: undefined,
    filterDestinationId: undefined,
    filterDestinationType: undefined,
    filterDestinationName: undefined,
    filterDestinationOwner: undefined,
    filterDestinationOwnerPersona: undefined,
    customSortBy: undefined,
  } as const;

  it('shows the paging bar when it is enabled', () => {
    renderWithProviders(<AppFooter />, {
      preloadedState: {
        pagingBar: basePagingBarState,
      },
    });

    expect(screen.getByTestId('page-version')).toBeInTheDocument();
    expect(screen.getByTestId('privacy-policy-link')).toBeInTheDocument();
    expect(screen.getByTestId('paging-bar')).toBeInTheDocument();
  });

  it('hides the paging bar when it is disabled', () => {
    renderWithProviders(<AppFooter />, {
      preloadedState: {
        pagingBar: {
          ...basePagingBarState,
          visible: false,
        },
      },
    });

    expect(screen.getByTestId('page-version')).toBeInTheDocument();
    expect(screen.getByTestId('privacy-policy-link')).toBeInTheDocument();
    expect(screen.queryByTestId('paging-bar')).toBeNull();
  });
});
