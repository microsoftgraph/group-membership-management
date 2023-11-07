// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';

import { type User } from '../models/User';
import { ThunkConfigWithErrors } from './store';
import { setLoggingIn, setLoggedIn } from './account.slice';

export const loginAsync = createAsyncThunk<User, void, ThunkConfigWithErrors<string>>(
  'account/login',
  async (_, { rejectWithValue, dispatch, getState, extra }) => {
    const { authenticationService } = extra;
    const { loggingIn, loggedIn } = getState().account;
    const { strings } = getState().localization;

    if (!loggedIn && !loggingIn) {
      dispatch(setLoggingIn(true));
      await authenticationService.loginAsync();
      dispatch(setLoggingIn(false));
      dispatch(setLoggedIn(true));
    }

    return authenticationService.getActiveAccount() ?? rejectWithValue(strings.Authentication.loginFailed);
  }
);
