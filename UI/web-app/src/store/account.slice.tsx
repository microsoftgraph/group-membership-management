// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';

import { fetchAccount } from './account.api';
import type { RootState } from './store';
import { type Account } from '../models/Account';

// Define a type for the slice state
export interface AccountState {
  /** @deprecated */
  loading: boolean;
  account?: Account;
  loggedIn: boolean;
  loggingIn: boolean;
}

// Define the initial state using that type
const initialState: AccountState = {
  loading: false,
  account: undefined,
  loggedIn: false,
  loggingIn: false
};

export const accountSlice = createSlice({
  name: 'account',
  initialState,
  reducers: {
    setAccount: (state, action: PayloadAction<Account>) => {
      state.account = action.payload;
    },
    setLoggedIn: (state, action: PayloadAction<boolean>) => {
      state.loggedIn = action.payload;
    },
    setLoggingIn: (state, action: PayloadAction<boolean>) => {
      state.loggingIn = action.payload;
    }
  },
  extraReducers: (builder) => {
    builder.addCase(fetchAccount.fulfilled, (state, action) => {
      state.loading = false;
      state.account = action.payload;
    });
  },
});

export const { setAccount, setLoggedIn, setLoggingIn } = accountSlice.actions;
export const selectAccount = (state: RootState) => state.account.account;
export const selectAccountName = (state: RootState) =>
  state.account.account?.name;
export const selectLoggedIn = (state: RootState) => state.account.loggedIn;
export default accountSlice.reducer;
