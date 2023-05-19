// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { addOwner } from './owner.api';

// Define a type for the slice state
export type OwnerState = {
  loading: boolean;
  status?: string;
}

// Define the initial state using that type
const initialState: OwnerState = {
  loading: false,
  status: ""
};

export const ownerSlice = createSlice({
  name: 'owner',
  initialState,
  reducers: {
    setOwner: (state, action: PayloadAction<string>) => {
      state.status = action.payload;
    }
  },
  extraReducers: (builder) => {
    builder.addCase(addOwner.fulfilled, (state, action) => {
      state.loading = false;
      state.status = action.payload;
    });
  }
});

export const { setOwner } = ownerSlice.actions;
export const selectOwner = (state: RootState) => state.owner;
export default ownerSlice.reducer;