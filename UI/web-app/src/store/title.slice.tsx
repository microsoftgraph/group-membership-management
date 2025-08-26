// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
import { generateTitles, getTitle } from './title.api';
import { RootState } from './store';
import { HRPart } from '../models/HRPart';

export interface TitleState {
  isGeneratingTitle: boolean;
  title: string;
  error: string | undefined;
  isGeneratingTitles: boolean;
  titles: HRPart[];
};

const initialState: TitleState = {
  isGeneratingTitle: false,
  title: '',
  error: undefined,
  isGeneratingTitles: false,
  titles: []
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
    builder.addCase(generateTitles.pending, (state) => {
      state.isGeneratingTitles = true;
    });
    builder.addCase(generateTitles.fulfilled, (state, action) => {
      state.isGeneratingTitles = false;
      state.titles = action.payload;
    });
  }
});

export const selectIsGeneratingTitle = (state: RootState) => state.title.isGeneratingTitle;
export const selectTitle = (state: RootState) => state.title.title;
export const selectTitleError = (state: RootState) => state.title.error;
export const selectIsGeneratingTitles = (state: RootState) => state.title.isGeneratingTitles;
export const selectTitles = (state: RootState) => state.title.titles;

export default titleSlice.reducer;