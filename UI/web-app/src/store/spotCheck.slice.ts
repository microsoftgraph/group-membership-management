// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { spotCheckUser } from './spotCheck.api';
import { SpotCheckResult } from '../models/SpotCheckResult';

export type SpotCheckStatus = 'idle' | 'loading' | 'succeeded' | 'failed';

export type SpotCheckState = {
  status: SpotCheckStatus;
  result?: SpotCheckResult;
  error?: string;
};

const initialState: SpotCheckState = {
  status: 'idle',
  result: undefined,
  error: undefined,
};

export const spotCheckSlice = createSlice({
  name: 'spotCheck',
  initialState,
  reducers: {
    clearSpotCheck: (state) => {
      state.status = 'idle';
      state.result = undefined;
      state.error = undefined;
    },
  },
  extraReducers: (builder) => {
    builder.addCase(spotCheckUser.pending, (state) => {
      state.status = 'loading';
      state.result = undefined;
      state.error = undefined;
    });
    builder.addCase(spotCheckUser.fulfilled, (state, action) => {
      state.status = 'succeeded';
      state.result = action.payload;
      state.error = undefined;
    });
    builder.addCase(spotCheckUser.rejected, (state, action) => {
      state.status = 'failed';
      state.result = undefined;
      state.error = action.error.message;
    });
  },
});

export const { clearSpotCheck } = spotCheckSlice.actions;

export const selectSpotCheckStatus = (state: RootState) => state.spotCheck.status;
export const selectSpotCheckResult = (state: RootState) => state.spotCheck.result;
export const selectSpotCheckError = (state: RootState) => state.spotCheck.error;

export default spotCheckSlice.reducer;
