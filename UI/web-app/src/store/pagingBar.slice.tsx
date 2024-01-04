// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
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
}

// Define the initial state using that type
const initialState: PagingBarState = {
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
  filterDestinationOwner: undefined
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
    },
    setFilterStatus: (state, action) => {
      state.filterStatus = action.payload;
    },
    setFilterDestinationId: (state, action) => {
      state.filterDestinationId = action.payload;
    },
    setFilterDestinationType: (state, action) => {
      state.filterDestinationType = action.payload;
    },
    setFilterDestinationName: (state, action) => {
      state.filterDestinationName = action.payload;
    },
    setFilterDestinationOwner: (state, action) => {
      state.filterDestinationOwner = action.payload;
    }
  },
  extraReducers: (builder) => {
    builder.addCase(fetchJobs.fulfilled, (state, action) => {
      state.totalNumberOfPages = action.payload.totalNumberOfPages;
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
  setFilterActionRequired,
  setFilterStatus
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
export const selectPagingBarFilterActionRequired = (state: RootState) => state.pagingBar.filterActionRequired;
export const selectPagingBarFilterStatus = (state: RootState) => state.pagingBar.filterStatus;


export const selectPagingOptions = (state: RootState) => {
  const { pageNumber, pageSize, 
    sortKey, isSortedDescending, 
    filterStatus, filterActionRequired,
    filterDestinationId,
    filterDestinationName,
    filterDestinationType,
    filterDestinationOwner
  } = state.pagingBar;
  
  let orderByString: string | undefined = undefined;
  let filters: string[] = [];
  if (sortKey !== undefined) {
    orderByString = sortKey + (isSortedDescending ? ' desc' : '');
  }
  if (filterDestinationId) {
    filters.push("targetOfficeGroupId eq " + filterDestinationId);
  }
  if (filterActionRequired && filterActionRequired !== 'All') {
    filters.push("status eq '" + filterActionRequired + "'");
  }
  if (filterDestinationType && filterDestinationType !== 'All')
  {
    filters.push("contains(Destination, '" + filterDestinationType + "')");
  }
  if (filterDestinationName)
  {
    filters.push("contains(tolower(DestinationName/Name), tolower('" + filterDestinationName + "'))");
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
  let filterString: string | undefined = filters.length === 0 ? undefined : filters.join(' and ');
    
  const itemsToSkip = (pageNumber - 1) * parseInt(pageSize);
  return { 
    pageSize: parseInt(pageSize),
    itemsToSkip,
    orderBy: orderByString,
    filter: filterString,
    sortKey,
    isSortedDescending
  };
};

export default pagingBarSlice.reducer;
