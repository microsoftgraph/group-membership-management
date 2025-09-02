// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Middleware } from '@reduxjs/toolkit';
import type { RootState } from './store';
import type { PagingBarState } from './pagingBar.slice';

// Actions that should trigger localStorage persistence
const PERSISTABLE_ACTIONS = [
  'pagingBar/setPageSize',
  'pagingBar/setPageNumber',
  'pagingBar/setSortKey',
  'pagingBar/setIsSortedDescending',
  'pagingBar/setFilterActionRequired',
  'pagingBar/setFilterStatus',
  'pagingBar/setFilterDestinationId',
  'pagingBar/setFilterDestinationType',
  'pagingBar/setFilterDestinationName',
  'pagingBar/setFilterDestinationOwner',
  'pagingBar/setFilterDestinationOwnerPersona',
  'pagingBar/setCustomSortBy',
  'pagingBar/resetFilters',
  'jobs/fetchJobs/fulfilled', // For page reset on invalid page
];

const STORAGE_KEY = 'gmmJobListState';

const saveStateToStorage = (pagingBarState: PagingBarState) => {
  try {
    const stateToSave = {
      pageSize: pagingBarState.pageSize,
      pageNumber: pagingBarState.pageNumber,
      sortKey: pagingBarState.sortKey,
      isSortedDescending: pagingBarState.isSortedDescending,
      filterStatus: pagingBarState.filterStatus,
      filterActionRequired: pagingBarState.filterActionRequired,
      filterDestinationId: pagingBarState.filterDestinationId,
      filterDestinationType: pagingBarState.filterDestinationType,
      filterDestinationName: pagingBarState.filterDestinationName,
      filterDestinationOwner: pagingBarState.filterDestinationOwner,
      filterDestinationOwnerPersona: pagingBarState.filterDestinationOwnerPersona,
      customSortBy: pagingBarState.customSortBy,
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(stateToSave));
  } catch (error) {
    console.warn('Failed to save job list state:', error);
  }
};

export const localStorageMiddleware: Middleware<Record<string, never>, RootState> = (store) => (next) => (action) => {
  const result = next(action);
  
  // Check if this action should trigger persistence
  if (PERSISTABLE_ACTIONS.includes(action.type)) {
    const state = store.getState();
    saveStateToStorage(state.pagingBar);
  }
  
  return result;
};
