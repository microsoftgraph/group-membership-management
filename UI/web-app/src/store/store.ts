// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { AnyAction, ThunkDispatch, configureStore } from '@reduxjs/toolkit';

import accountReducer from './account.slice';
import jobsReducer from './jobs.slice';
import ownerReducer from './owner.slice';
import profileReducer from './profile.slice';
import settingsReducer from './settings.slice';
import manageMembershipReducer from './manageMembership.slice';
import {
  MsalAuthenticationService,
  IAuthenticationService,
} from '../auth';

// use OfflineAuthenticationService for offline development.
const authenticationService = new MsalAuthenticationService();

export const store = configureStore({
  reducer: {
    account: accountReducer,
    jobs: jobsReducer,
    owner: ownerReducer,
    profile: profileReducer,
    settings: settingsReducer,
    manageMembership: manageMembershipReducer
  },
  middleware: (getDefaultMiddleware) =>
    getDefaultMiddleware({
      thunk: {
        extraArgument: { authenticationService },
      },
    }),
});

export type Services = {
  authenticationService: IAuthenticationService;
};

// Infer the `RootState` and `AppDispatch` types from the store itself
export type RootState = ReturnType<typeof store.getState>;
// Inferred type: { jobs: userState }
export type AppDispatch = typeof store.dispatch;
export type AppThunkDispatch = ThunkDispatch<RootState, Services, AnyAction>;

export type ThunkConfig = { state: RootState; extra: Services };