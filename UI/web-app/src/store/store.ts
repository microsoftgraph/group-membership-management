// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { PreloadedState, combineReducers, configureStore } from '@reduxjs/toolkit';

import accountReducer from './account.slice';
import jobsReducer from './jobs.slice';
import localizationReducer from './localization.slice';
import manageMembershipReducer from './manageMembership.slice';
import ownerReducer from './owner.slice';
import profileReducer from './profile.slice';
import settingsReducer from './settings.slice';

import { Services } from '../services';
import { MsalAuthenticationService } from '../services/auth';
import { LocalizationService } from '../services/localization';

// use OfflineAuthenticationService for offline development.
const services: Services = {
  authenticationService: new MsalAuthenticationService(),
  localizationService: new LocalizationService(),
};

const rootReducer = combineReducers({
  account: accountReducer,
  jobs: jobsReducer,
  localization: localizationReducer,
  manageMembership: manageMembershipReducer,
  owner: ownerReducer,
  profile: profileReducer,
  settings: settingsReducer,
});

export const store = configureStore({
  reducer: rootReducer,
  middleware: (getDefaultMiddleware) =>
    getDefaultMiddleware({
      thunk: {
        extraArgument: services,
      },
    }),
});

export function setupStore(preloadedState?: PreloadedState<RootState>, serviceMocks?: Partial<Services>) {
  return configureStore({
    reducer: rootReducer,
    middleware: (getDefaultMiddleware) =>
      getDefaultMiddleware({
        thunk: {
          extraArgument: {
            ...services,
            ...serviceMocks,
          },
        },
      }),
    preloadedState
  })
}

// Infer the `RootState` and `AppDispatch` types from the store itself
export type RootState = ReturnType<typeof store.getState>;
export type AppStore = ReturnType<typeof setupStore>
export type AppDispatch = typeof store.dispatch;

// Used by thunk actions to strongly type the 'extra' argument.
export type ThunkConfig = { state: RootState; extra: Services; };
export type ThunkConfigWithErrors<RejectValueType> = ThunkConfig & { rejectValue: RejectValueType; };
