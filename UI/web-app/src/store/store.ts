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
import rolesReducer from './roles.slice';

import { Services } from '../services';
import { MsalAuthenticationService, TokenType } from '../services/auth';
import { LocalizationService } from '../services/localization';
import { ApiOptions, Apis, GraphApi } from '../apis';
import { GMMApi } from '../apis/GMMApi';

// use OfflineAuthenticationService for offline development.
const services: Services = {
  authenticationService: new MsalAuthenticationService(),
  localizationService: new LocalizationService(),
};

const gmmApiOptions: ApiOptions = {
  baseUrl: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1`,
  getTokenAsync: async () => await services.authenticationService.getTokenAsync(TokenType.GMM),
};

const graphApiOptions: ApiOptions = {
  baseUrl: 'https://graph.microsoft.com/v1.0',
  getTokenAsync: async () => await services.authenticationService.getTokenAsync(TokenType.Graph),
};

const apis: Apis = {
  gmmApi: new GMMApi(gmmApiOptions),
  graphApi: new GraphApi(graphApiOptions),
};

const rootReducer = combineReducers({
  account: accountReducer,
  jobs: jobsReducer,
  localization: localizationReducer,
  manageMembership: manageMembershipReducer,
  owner: ownerReducer,
  profile: profileReducer,
  settings: settingsReducer,
  roles: rolesReducer,
});

export const store = configureStore({
  reducer: rootReducer,
  middleware: (getDefaultMiddleware) =>
    getDefaultMiddleware({
      thunk: {
        extraArgument: {
          services,
          apis,
        },
      },
    }),
});

/**
 * Allows us to create a redux store for testing that accepts a preloadedState
 * and allows us to supply mocks for the apis and services.
 */
export function setupStore(
  preloadedState?: PreloadedState<RootState>,
  serviceMocks?: Partial<Services>,
  apiMocks?: Partial<Apis>
) {
  return configureStore({
    reducer: rootReducer,
    middleware: (getDefaultMiddleware) =>
      getDefaultMiddleware({
        thunk: {
          extraArgument: {
            apis: {
              ...apis,
              ...apiMocks,
            },
            services: {
              ...services,
              ...serviceMocks,
            },
          },
        },
      }),
    preloadedState,
  });
}

type ExtraArgument = {
  services: Services;
  apis: Apis;
};

// Infer the `RootState` and `AppDispatch` types from the store itself
export type RootState = ReturnType<typeof store.getState>;
export type AppStore = ReturnType<typeof setupStore>;
export type AppDispatch = typeof store.dispatch;

// Used by thunk actions to strongly type the 'extra' argument.
export type ThunkConfig = { state: RootState; extra: ExtraArgument };
export type ThunkConfigWithErrors<RejectValueType> = ThunkConfig & { rejectValue: RejectValueType };
