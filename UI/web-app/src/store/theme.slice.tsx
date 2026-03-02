// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import type { RootState } from './store';

export interface ThemeState {
  isDarkMode: boolean;
}

const getInitialThemeState = (): boolean => {
  // Check localStorage first
  const storedTheme = localStorage.getItem('gmmThemeMode');
  if (storedTheme !== null) {
    return storedTheme === 'dark';
  }
  
  // Fall back to system preference
  if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
    return true;
  }
  
  return false;
};

const initialState: ThemeState = {
  isDarkMode: getInitialThemeState(),
};

export const themeSlice = createSlice({
  name: 'theme',
  initialState,
  reducers: {
    toggleTheme: (state) => {
      state.isDarkMode = !state.isDarkMode;
      localStorage.setItem('gmmThemeMode', state.isDarkMode ? 'dark' : 'light');
    },
    setTheme: (state, action: PayloadAction<boolean>) => {
      state.isDarkMode = action.payload;
      localStorage.setItem('gmmThemeMode', state.isDarkMode ? 'dark' : 'light');
    },
  },
});

export const { toggleTheme, setTheme } = themeSlice.actions;

export const selectIsDarkMode = (state: RootState): boolean => state.theme.isDarkMode;

export default themeSlice.reducer;
