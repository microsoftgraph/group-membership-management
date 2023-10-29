// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';

import { type Account } from '../models/Account';
import { ThunkConfig } from './store';
import { setLoggingIn, setLoggedIn } from './account.slice';

export const login = createAsyncThunk<void, void, ThunkConfig>(
  'account/login',
  async (_, { dispatch, getState, extra }) => {
    const { authenticationService } = extra;
    const { loggingIn, loggedIn } = getState().account;

    if (!loggedIn && !loggingIn) {
      dispatch(setLoggingIn(true));
      await authenticationService.loginAsync();
      dispatch(setLoggingIn(false));
      dispatch(setLoggedIn(true));
      dispatch(fetchAccount());
    }
  }
);

export const fetchAccount = createAsyncThunk<Account, void, ThunkConfig>(
  'account/fetchAccount',
  async (_, { rejectWithValue, getState, extra }) => {
    const { authenticationService } = extra;
    const { loggedIn } = getState().account;

    if (!loggedIn) {
      return rejectWithValue('You must login before attempting to retrieve an active account.');
    }

    const account = authenticationService.getActiveAccount();

    if (!account) {
      return rejectWithValue('No active account found. Have you logged in?');
    }

    return account;
  }
);
