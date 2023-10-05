// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { configureStore } from '@reduxjs/toolkit';

import accountReducer from './account.slice';
import jobsReducer from './jobs.slice';
import ownerReducer from './owner.slice';
import profileReducer from './profile.slice';
import settingsReducer from './settings.slice';

export const store = configureStore({
  reducer: {
    account: accountReducer,
    jobs: jobsReducer,
    owner: ownerReducer,
    profile: profileReducer,
    settings: settingsReducer
  }
});

// Infer the `RootState` and `AppDispatch` types from the store itself
export type RootState = ReturnType<typeof store.getState>;
// Inferred type: { jobs: userState }
export type AppDispatch = typeof store.dispatch;
