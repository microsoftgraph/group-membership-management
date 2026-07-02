// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useSelector } from 'react-redux';
import { createSlice } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { searchDestinations } from './manageMembership.api';
import { DestinationPickerPersona } from '../models';

// Define a type for the slice state
export interface GroupPartState {
  searchResults?: DestinationPickerPersona[];
  // Cache of groups resolved via searchDestinations, keyed by id. Unlike
  // `searchResults` (which is overwritten by every search and is used to back
  // the destination picker's suggestion list), this map accumulates results so
  // multiple rule cards can each resolve their own group's name/alias without
  // racing to overwrite a single shared array.
  resolvedGroupsById?: Record<string, DestinationPickerPersona>;
}

// Define the initial state using that type
const initialState: GroupPartState = {
  searchResults: [],
  resolvedGroupsById: {}
};

export const groupPartSlice = createSlice({
  name: 'groupPart',
  initialState,
  reducers: {
  },
  extraReducers: (builder) => {
    builder.addCase(searchDestinations.fulfilled, (state, action) => {
      state.searchResults = action.payload;
      if (!state.resolvedGroupsById) {
        state.resolvedGroupsById = {};
      }
      for (const group of action.payload) {
        if (group.id) {
          state.resolvedGroupsById[group.id] = group;
        }
      }
    })
  },
});

export const selectSelectedGroupById = (state: RootState, id: string) => {
  const cached = state.groupPart.resolvedGroupsById?.[id];
  if (cached) {
    return cached;
  }
  return Array.isArray(state.groupPart.searchResults)
  ? state.groupPart.searchResults.find((group) => group.id === id)
  : undefined;
};

export default groupPartSlice.reducer;

export const useSelectedGroupById = (id: string) => {
  return useSelector((state: RootState) => selectSelectedGroupById(state, id));
};
