// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import { fetchGroupDetailsAndGenerateTitle, fetchOrgLeaderDetailsAndGenerateHRTitle, generateTitles, getTitle } from './title.api';
import { RootState } from './store';
import { HRPart } from '../models/HRPart';
import { ISourcePart } from '../models';

export interface TitleState {
  isGeneratingTitle: boolean;
  title: string;
  error: string | undefined;
  isGeneratingTitles: boolean;
  titles: HRPart[];
  isGeneratingHRTitle: boolean;
  generatedHRParts: ISourcePart[];
  isGeneratingGroupTitle: boolean;
  generatedGroupParts: ISourcePart[];
};

const initialState: TitleState = {
  isGeneratingTitle: false,
  title: '',
  error: undefined,
  isGeneratingTitles: false,
  titles: [],
  isGeneratingHRTitle: false,
  generatedHRParts: [],
  isGeneratingGroupTitle: false,
  generatedGroupParts: []
};

const titleSlice = createSlice({
  name: 'title',
  initialState,
  reducers: {
    clearTitles: (state) => {
      state.titles = [];
    },
    upsertGeneratedTitle: (state, action: PayloadAction<HRPart>) => {
      const existingIndex = state.titles.findIndex(t => t.partId === action.payload.partId);
      if (existingIndex >= 0) {
        state.titles[existingIndex] = action.payload;
      } else {
        state.titles.push(action.payload);
      }
    },
    clearGeneratedHRParts: (state) => {
      state.generatedHRParts = [];
    },
    clearGeneratedGroupParts: (state) => {
      state.generatedGroupParts = [];
    }
  },
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
    builder.addCase(generateTitles.rejected, (state, action) => {
      state.isGeneratingTitles = false;
      state.titles = action.meta.arg || [];
    });
    builder.addCase(fetchOrgLeaderDetailsAndGenerateHRTitle.pending, (state) => {
      state.isGeneratingHRTitle = true;
    });
    builder.addCase(fetchOrgLeaderDetailsAndGenerateHRTitle.fulfilled, (state, action) => {
      state.isGeneratingHRTitle = false;
      const existingIndex = state.generatedHRParts.findIndex(p => p.id === action.payload.id);
      if (existingIndex >= 0) {
        state.generatedHRParts[existingIndex] = action.payload;
      } else {
        state.generatedHRParts.push(action.payload);
      }
    });
    builder.addCase(fetchOrgLeaderDetailsAndGenerateHRTitle.rejected, (state, action) => {
      state.isGeneratingHRTitle = false;
      state.error = action.error.message;
    });
    builder.addCase(fetchGroupDetailsAndGenerateTitle.pending, (state) => {
      state.isGeneratingGroupTitle = true;
    });
    builder.addCase(fetchGroupDetailsAndGenerateTitle.fulfilled, (state, action) => {
      state.isGeneratingGroupTitle = false;
      const existingIndex = state.generatedGroupParts.findIndex(p => p.id === action.payload.id);
      if (existingIndex >= 0) {
        state.generatedGroupParts[existingIndex] = action.payload;
      } else {
        state.generatedGroupParts.push(action.payload);
      }
    });
    builder.addCase(fetchGroupDetailsAndGenerateTitle.rejected, (state, action) => {
      state.isGeneratingGroupTitle = false;
      state.error = action.error.message;
    });
  }
});

export const { clearTitles, upsertGeneratedTitle, clearGeneratedHRParts, clearGeneratedGroupParts } = titleSlice.actions;

export const selectIsGeneratingTitle = (state: RootState) => state.title.isGeneratingTitle;
export const selectTitle = (state: RootState) => state.title.title;
export const selectTitleError = (state: RootState) => state.title.error;
export const selectIsGeneratingTitles = (state: RootState) => state.title.isGeneratingTitles;
export const selectTitles = (state: RootState) => state.title.titles;
export const selectIsGeneratingHRTitle = (state: RootState) => state.title.isGeneratingHRTitle;
export const selectGeneratedHRParts = (state: RootState) => state.title.generatedHRParts;
export const selectIsGeneratingGroupTitle = (state: RootState) => state.title.isGeneratingGroupTitle;
export const selectGeneratedGroupParts = (state: RootState) => state.title.generatedGroupParts;

export default titleSlice.reducer;
