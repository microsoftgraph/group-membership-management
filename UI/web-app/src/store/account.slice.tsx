// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';

import { loginAsync } from './account.api';
import type { RootState } from './store';
import { type User } from '../models/User';

// Define a type for the slice state
export interface AccountState {
  user?: User;
  loggedIn: boolean;
  loggingIn: boolean;
  loginError?: string;
}

// Define the initial state using that type
const initialState: AccountState = {
  user: undefined,
  loggedIn: false,
  loggingIn: false,
  loginError: undefined,
};

export const accountSlice = createSlice({
  name: 'account',
  initialState,
  reducers: {
    /** This should only be used within loginAsync. */
    setLoggedIn: (state, action: PayloadAction<boolean>) => {
      state.loggedIn = action.payload;
    },
    /** This should only be used within loginAsync. */
    setLoggingIn: (state, action: PayloadAction<boolean>) => {
      state.loggingIn = action.payload;
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(loginAsync.fulfilled, (state, action) => {
        state.user = action.payload;
      })
      .addCase(loginAsync.rejected, (state, action) => {
        state.loginError = action.payload;
      });
  },
});

export const { setLoggedIn, setLoggingIn } = accountSlice.actions;
export const selectAccount = (state: RootState) => state.account.user;
export const selectAccountName = (state: RootState) => state.account.user?.name;
export const selectLoggedIn = (state: RootState) => state.account.loggedIn;
export const selectLoginError = (state: RootState) => state.account.loginError;
export default accountSlice.reducer;
