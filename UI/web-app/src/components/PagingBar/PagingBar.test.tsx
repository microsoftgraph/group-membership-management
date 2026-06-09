import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { PagingBar } from './PagingBar';

describe('PagingBar', () => {
  const baseState = {
    pagingBar: {
      visible: true,
      pageSize: '10',
      pageNumber: 1,
      totalNumberOfPages: 3,
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
    },
  } as const;

  beforeEach(() => {
    localStorage.clear();
  });

  it('disables navigating backwards from the first page', () => {
    renderWithProviders(<PagingBar />, { preloadedState: baseState });

    expect(screen.getByTitle('Prev')).toBeDisabled();
    expect(screen.getByTitle('Next')).not.toBeDisabled();
  });

  it('advances to the next page when requested', () => {
    const { store } = renderWithProviders(<PagingBar />, {
      preloadedState: {
        pagingBar: {
          ...baseState.pagingBar,
          pageNumber: 1,
        },
      },
    });

    fireEvent.click(screen.getByTitle('Next'));

    expect(store.getState().pagingBar.pageNumber).toBe(2);
  });

  it('updates the page size through the dropdown', async () => {
    const { store } = renderWithProviders(<PagingBar />, { preloadedState: baseState });

    fireEvent.click(screen.getByRole('combobox', { name: 'Items per page' }));
    fireEvent.click(await screen.findByRole('option', { name: '20' }));

    expect(store.getState().pagingBar.pageSize).toBe('20');
    expect(store.getState().pagingBar.pageNumber).toBe(1);
  });
});
