// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
import { getTitle } from './title.api';
import { RootState } from './store';

export interface TitleState {
  isGeneratingTitle: boolean;
  title: string;
  error: string | undefined;
};

const initialState: TitleState = {
  isGeneratingTitle: false,
  title: '',
  error: undefined
};

const titleSlice = createSlice({
  name: 'title',
  initialState,
  reducers: { },
  extraReducers: (builder) => {
    builder.addCase(getTitle.pending, (state) => {
      state.isGeneratingTitle = true;
    });
    builder.addCase(getTitle.fulfilled, (state, action) => {
      state.isGeneratingTitle = false;
      state.title = action.payload;
    });
    builder.addCase(getTitle.rejected, (state, action) => {
      state.isGeneratingTitle = false;
      state.error = action.error.message;
    });
  }
});

export const selectIsGeneratingTitle = (state: RootState) => state.title.isGeneratingTitle;
export const selectTitle = (state: RootState) => state.title.title;
export const selectTitleError = (state: RootState) => state.title.error;

export default titleSlice.reducer;