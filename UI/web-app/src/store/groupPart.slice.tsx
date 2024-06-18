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
}

// Define the initial state using that type
const initialState: GroupPartState = {
  searchResults: []
};

export const groupPartSlice = createSlice({
  name: 'groupPart',
  initialState,
  reducers: {
  },
  extraReducers: (builder) => {
    builder.addCase(searchDestinations.fulfilled, (state, action) => {
      state.searchResults = action.payload;
    })
  },
});

export const selectSelectedGroupById = (state: RootState, id: string) => {
  return Array.isArray(state.groupPart.searchResults)
  ? state.groupPart.searchResults.find((group) => group.id === id)
  : undefined;
};

export default groupPartSlice.reducer;

export const useSelectedGroupById = (id: string) => {
  return useSelector((state: RootState) => selectSelectedGroupById(state, id));
};
