// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';

import { setLanguage } from './localization.api';
import type { RootState } from './store';
import { defaultStrings, IStrings } from '../services/localization';

// Define a type for the slice state
export type LocalizationState = {
  language: string;
  strings: IStrings;
};

// Define the initial state using that type
const initialState: LocalizationState = {
  language: 'en',
  strings: defaultStrings,
};

export const localizationSlice = createSlice({
  name: 'localization',
  initialState,
  reducers: {},
  extraReducers: (builder) => {
    builder.addCase(setLanguage.fulfilled, (state, action) => {
      const { language, strings } = action.payload;
      state.language = language;
      state.strings = strings;
    });
  },
});

export const selectStrings = (state: RootState) => state.localization.strings;
export default localizationSlice.reducer;
