// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { PreloadedState, combineReducers, configureStore } from '@reduxjs/toolkit';

import accountReducer from './account.slice';
import copilotReducer from './copilot.slice';
import groupPartReducer from './groupPart.slice';
import jobsReducer from './jobs.slice';
import localizationReducer from './localization.slice';
import manageMembershipReducer from './manageMembership.slice';
import ownerReducer from './owner.slice';
import pagingBarReducer from './pagingBar.slice';
import profileReducer from './profile.slice';
import orgLeaderDetailsReducer from './orgLeaderDetails.slice';
import settingsReducer from './settings.slice';
import rolesReducer from './roles.slice';
import sqlMembershipSourcesReducer from './sqlMembershipSources.slice';
import operationsReducer from './operations.slice';
import titleReducer from './title.slice';
import themeReducer from './theme.slice';
import userProfileReducer from './userProfile.slice';

import { Services } from '../services';
import { MsalAuthenticationService, TokenType } from '../services/auth';
import { LocalizationService } from '../services/localization';
import { ApiOptions, Apis, GraphApi } from '../apis';
import { GMMApi } from '../apis/GMMApi';
import { localStorageMiddleware } from './localStorage.middleware';
import { OfflineAuthenticationService } from '../testing/OfflineAuthenticationService';

const isPlaywrightMockMode = process.env.REACT_APP_PLAYWRIGHT_MOCK_MODE === 'true';

// use OfflineAuthenticationService for offline development.
const services: Services = {
  authenticationService: isPlaywrightMockMode ? new OfflineAuthenticationService() : new MsalAuthenticationService(),
  localizationService: new LocalizationService(),
};

const gmmApiOptions: ApiOptions = {
  baseUrl: isPlaywrightMockMode ? '/api/v1' : `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1`,
  getTokenAsync: async () => await services.authenticationService.getTokenAsync(TokenType.GMM),
};

const graphApiOptions: ApiOptions = {
  baseUrl: isPlaywrightMockMode ? '/graph/v1.0' : 'https://graph.microsoft.com/v1.0',
  getTokenAsync: async () => await services.authenticationService.getTokenAsync(TokenType.Graph),
};

const apis: Apis = {
  gmmApi: new GMMApi(gmmApiOptions),
  graphApi: new GraphApi(graphApiOptions),
};

const rootReducer = combineReducers({
  account: accountReducer,
  copilot: copilotReducer,
  groupPart: groupPartReducer,
  jobs: jobsReducer,
  localization: localizationReducer,
  manageMembership: manageMembershipReducer,
  owner: ownerReducer,
  pagingBar: pagingBarReducer,
  profile: profileReducer,
  orgLeaderDetails: orgLeaderDetailsReducer,
  settings: settingsReducer,
  roles: rolesReducer,
  sqlMembershipSources: sqlMembershipSourcesReducer,
  operations: operationsReducer,
  title: titleReducer,
  userProfile: userProfileReducer,
  theme: themeReducer
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
    }).concat(localStorageMiddleware),
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
      }).concat(localStorageMiddleware),
    preloadedState,
  });
}

type ExtraArgument = {
  services: Services;
  apis: Apis;
};

// Infer the `RootState` and `AppDispatch` types from the store itself
export type RootState = ReturnType<typeof rootReducer>;
export type AppStore = ReturnType<typeof setupStore>;
export type AppDispatch = typeof store.dispatch;

// Used by thunk actions to strongly type the 'extra' argument.
export type ThunkConfig = { state: RootState; extra: ExtraArgument };
export type ThunkConfigWithErrors<RejectValueType> = ThunkConfig & { rejectValue: RejectValueType };
