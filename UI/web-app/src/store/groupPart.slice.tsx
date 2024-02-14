// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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

export const selectedGroups = (state: RootState) => state.groupPart.searchResults;
export default groupPartSlice.reducer;
