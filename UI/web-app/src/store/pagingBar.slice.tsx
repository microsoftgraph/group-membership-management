// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, createSelector } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { SyncStatus } from '../models';
import { fetchJobs } from './jobs.api';

// Define a type for the slice state
export type PagingBarState = {
  visible: boolean;
  pageSize: string;
  pageNumber: number;
  totalNumberOfPages: number;
  sortKey?: string;
  filterString?: string;
  filterActionRequired?: string;
  isSortedDescending?: boolean;
  filterStatus?: string;
  filterDestinationId?: string;
  filterDestinationType?: string;
  filterDestinationName?: string;
  filterDestinationOwner?: string;
  filterDestinationOwnerPersona?: {
    key: number;
    text: string;
    secondaryText: string;
    id: string;
  };
  customSortBy?: string;
}

// Helper functions for localStorage persistence
const STORAGE_KEY = 'gmmJobListState';

const loadPersistedState = (): Partial<PagingBarState> => {
  try {
    const savedState = localStorage.getItem(STORAGE_KEY);
    if (savedState) {
      const parsed = JSON.parse(savedState);
      // Only restore filter state, not sorting or pagination
      return {
        filterStatus: parsed.filterStatus,
        filterActionRequired: parsed.filterActionRequired,
        filterDestinationId: parsed.filterDestinationId,
        filterDestinationType: parsed.filterDestinationType,
        filterDestinationName: parsed.filterDestinationName,
        filterDestinationOwner: parsed.filterDestinationOwner,
        filterDestinationOwnerPersona: parsed.filterDestinationOwnerPersona,
      };
    }
  } catch (error) {
    console.warn('Failed to load persisted job list state:', error);
  }
  return {};
};

// Define the initial state using that type
const persistedState = loadPersistedState();
const initialState: PagingBarState = {
  visible: true,
  pageSize: persistedState.pageSize || '10',
  pageNumber: persistedState.pageNumber || 1,
  totalNumberOfPages: 0,
  sortKey: persistedState.sortKey || undefined,
  filterString: undefined,
  filterActionRequired: persistedState.filterActionRequired || undefined,
  isSortedDescending: persistedState.isSortedDescending || false,
  filterStatus: persistedState.filterStatus || undefined,
  filterDestinationId: persistedState.filterDestinationId || undefined,
  filterDestinationType: persistedState.filterDestinationType || undefined,
  filterDestinationName: persistedState.filterDestinationName || undefined,
  filterDestinationOwner: persistedState.filterDestinationOwner || undefined,
  filterDestinationOwnerPersona: persistedState.filterDestinationOwnerPersona || undefined,
  customSortBy: persistedState.customSortBy || undefined
};

export const pagingBarSlice = createSlice({
  name: 'pagingBar',
  initialState,
  reducers: { 
    setPagingBarVisible: (state, action) => {
      state.visible = action.payload;
    },
    setPageSize: (state, action) => {
      state.pageSize = action.payload;
      state.pageNumber = 1;
    },
    setPageNumber: (state, action) => {
      state.pageNumber = action.payload;
    },
    setTotalNumberOfPages: (state, action) => {
      state.totalNumberOfPages = action.payload;
    },
    setSortKey: (state, action) => {
      state.sortKey = action.payload;
    },
    setIsSortedDescending: (state, action) => {
      state.isSortedDescending = action.payload;
    },
    setFilterString: (state, action) => {
      state.filterString = action.payload;
    },
    setFilterActionRequired: (state, action) => {
      state.filterActionRequired = action.payload;
      state.pageNumber = 1;
    },
    setFilterStatus: (state, action) => {
      state.filterStatus = action.payload;
      state.pageNumber = 1;
    },
    setFilterDestinationId: (state, action) => {
      state.filterDestinationId = action.payload;
      state.pageNumber = 1;
    },
    setFilterDestinationType: (state, action) => {
      state.filterDestinationType = action.payload;
      state.pageNumber = 1;
    },
    setFilterDestinationName: (state, action) => {
      state.filterDestinationName = action.payload;
      state.pageNumber = 1;
    },
    setFilterDestinationOwner: (state, action) => {
      state.filterDestinationOwner = action.payload;
      state.pageNumber = 1;
    },
    setFilterDestinationOwnerPersona: (state, action) => {
      const persona = action.payload;
      state.filterDestinationOwner = persona?.id || undefined;
      state.filterDestinationOwnerPersona = persona || undefined;
      state.pageNumber = 1;
    },
    setCustomSortBy: (state, action) => {
      state.customSortBy = action.payload;
    },
    resetFilters: (state) => {
      state.filterDestinationId = undefined;
      state.filterDestinationType = undefined;
      state.filterDestinationName = undefined;
      state.filterDestinationOwner = undefined;
      state.filterDestinationOwnerPersona = undefined;
      state.filterActionRequired = undefined;
      state.filterStatus = undefined;
      state.pageNumber = 1;
    }
  },
  extraReducers: (builder) => {
    builder.addCase(fetchJobs.fulfilled, (state, action) => {
      const newTotalPages = action.payload.totalNumberOfPages;
      
      // Only reset page if current page is beyond the available pages (invalid page)
      if (newTotalPages > 0 && state.pageNumber > newTotalPages) {
        state.pageNumber = 1;
      }
      
      state.totalNumberOfPages = newTotalPages;
    });
  }
});

export const { 
  setPagingBarVisible, 
  setPageSize, 
  setPageNumber, 
  setTotalNumberOfPages, 
  setSortKey, 
  setIsSortedDescending,
  setFilterString,
  setFilterDestinationId,
  setFilterDestinationType,
  setFilterDestinationName,
  setFilterDestinationOwner,
  setFilterDestinationOwnerPersona,
  setFilterActionRequired,
  setFilterStatus,
  setCustomSortBy,
  resetFilters
} = pagingBarSlice.actions;
export const selectPagingBar = (state: RootState) => state.pagingBar;
export const selectPagingBarVisible = (state: RootState) => state.pagingBar.visible;
export const selectPagingBarPageSize = (state: RootState) => state.pagingBar.pageSize;
export const selectPagingBarPageNumber = (state: RootState) => state.pagingBar.pageNumber;
export const selectPagingBarTotalNumberOfPages = (state: RootState) => state.pagingBar.totalNumberOfPages;
export const selectPagingBarSortKey = (state: RootState) => state.pagingBar.sortKey;
export const selectPagingBarIsSortedDescending = (state: RootState) => state.pagingBar.isSortedDescending;
export const selectPagingBarFilterString = (state: RootState) => state.pagingBar.filterString;
export const selectPagingBarfilterDestinationId = (state: RootState) => state.pagingBar.filterDestinationId;
export const selectPagingBarfilterDestinationType = (state: RootState) => state.pagingBar.filterDestinationType;
export const selectPagingBarfilterDestinationName = (state: RootState) => state.pagingBar.filterDestinationName;
export const selectPagingBarfilterDestinationOwner = (state: RootState) => state.pagingBar.filterDestinationOwner;
export const selectPagingBarfilterDestinationOwnerPersona = (state: RootState) => state.pagingBar.filterDestinationOwnerPersona;
export const selectPagingBarFilterActionRequired = (state: RootState) => state.pagingBar.filterActionRequired;
export const selectPagingBarFilterStatus = (state: RootState) => state.pagingBar.filterStatus;
export const selectPagingBarCustomSortBy = (state: RootState) => state.pagingBar.customSortBy;


export const selectPagingOptions = createSelector(
  [
    (state: RootState) => state.pagingBar.pageNumber,
    (state: RootState) => state.pagingBar.pageSize,
    (state: RootState) => state.pagingBar.sortKey,
    (state: RootState) => state.pagingBar.isSortedDescending,
    (state: RootState) => state.pagingBar.filterStatus,
    (state: RootState) => state.pagingBar.filterActionRequired,
    (state: RootState) => state.pagingBar.filterDestinationId,
    (state: RootState) => state.pagingBar.filterDestinationName,
    (state: RootState) => state.pagingBar.filterDestinationType,
    (state: RootState) => state.pagingBar.filterDestinationOwner,
    (state: RootState) => state.pagingBar.customSortBy,
  ],
  (pageNumber, pageSize, sortKey, isSortedDescending, filterStatus, filterActionRequired, 
   filterDestinationId, filterDestinationName, filterDestinationType, filterDestinationOwner, customSortBy) => {
    
    let orderByString: string | undefined = undefined;
    const filters: string[] = [];
    if (sortKey !== undefined && sortKey !== 'targetGroupName' && sortKey !== 'lastModifiedTime') {
      orderByString = sortKey + (isSortedDescending ? ' desc' : '');
    }
    if (filterDestinationId) {
      filters.push("Group/GroupId eq " + filterDestinationId);
    }
    if (filterActionRequired && filterActionRequired !== 'All') {
      filters.push("status eq '" + filterActionRequired + "'");
    }
    if (filterDestinationType && filterDestinationType !== 'All')
    {
      filters.push("contains(Destination, '" + filterDestinationType + "')");
    }
    if (filterDestinationName) {
      const subConditions: string[] = [];
    
      subConditions.push("contains(tolower(DestinationName/Name), tolower('" + filterDestinationName + "'))");
      subConditions.push("contains(tolower(DestinationEmail/Email), tolower('" + filterDestinationName + "'))");
    
      if (isGuidValid(filterDestinationName)) {
        subConditions.push("targetOfficeGroupId eq " + filterDestinationName);
      }
      const combinedSubFilter = "(" + subConditions.join(" or ") + ")";
      filters.push(combinedSubFilter);
    }
    
    if (filterDestinationOwner)
    {
      filters.push("DestinationOwners/any(o: o/ObjectId eq " + filterDestinationOwner + ")");
    }
    
    if (filterStatus === 'Enabled') {
      filters.push("(status eq '" + SyncStatus.Idle + "' or status eq '" + SyncStatus.InProgress + "')");
    }
    else if (filterStatus === 'Disabled') {
      filters.push("not (status eq '" + SyncStatus.Idle + "' or status eq '" + SyncStatus.InProgress + "')");
    }
    const filterString: string | undefined = filters.length === 0 ? undefined : filters.join(' and ');
      
    const itemsToSkip = (pageNumber - 1) * parseInt(pageSize);
    return { 
      pageSize: parseInt(pageSize),
      itemsToSkip,
      orderBy: orderByString,
      filter: filterString,
      sortKey,
      isSortedDescending,
      customSortBy: (customSortBy === 'targetGroupName' || customSortBy === 'lastModifiedTime') ? customSortBy : undefined
    };
  }
);

export default pagingBarSlice.reducer;

function isGuidValid(guid: string): boolean {
  const guidRegex = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
  return guidRegex.test(guid);
};
