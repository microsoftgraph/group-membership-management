// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { screen, waitFor } from '@testing-library/react';
import { initializeIcons } from '@fluentui/react';
import { MemoryRouter } from 'react-router-dom';

import { App } from './App';
import { renderWithProviders } from '../testing';
import { OfflineAuthenticationService } from '../testing/OfflineAuthenticationService';
import { setupStore, type RootState } from '../store';
import { defaultStrings } from '../services/localization';
import { ServiceStatuses } from '../models/ServiceStatuses';
import type { Roles as RolesResponse } from '../apis/roles/IRolesApi';
import type { IGMMApi } from '../apis/IGMMApi';
import { SqlMembershipSource } from '../models/SqlMembershipSource';
import { SqlMembershipAttribute } from '../models/SqlMembershipAttribute';
import { SettingKey } from '../models/SettingKey';

beforeAll(() => {
  initializeIcons(undefined, { disableWarnings: true });
});

test('renders header after login', async () => {
  const authenticationService = new OfflineAuthenticationService();

  const rolesResponse: RolesResponse = {
    isJobOwnerReader: true,
    isJobOwnerEnabler: false,
    isJobOwnerDeleter: false,
    isJobOwnerWriter: false,
    isJobTenantReader: false,
    isJobTenantWriter: false,
    isSubmissionReviewer: false,
    isSubmissionRejector: false,
    isHyperlinkAdministrator: false,
    isCustomMembershipProviderAdministrator: false,
    isOperationsResetAdministrator: false,
    isGeneralSettingsAdministrator: false,
    isAIOnboardingChat: false,
    isAISettingsAdministrator: false,
    isFetchingRoles: false,
  };

  const defaultSqlSource: SqlMembershipSource = {
    name: 'Default Source',
    customLabel: 'Default Label',
  };

  const defaultSqlAttributes: SqlMembershipAttribute[] = [];

  const preloadedState: Partial<RootState> = {
    account: {
      user: authenticationService.getActiveAccount(),
      loggedIn: true,
      loggingIn: false,
      loginError: undefined,
    },
    localization: {
      language: 'en',
      strings: defaultStrings,
    },
    roles: rolesResponse,
    profile: {
      userPreferredLanguage: 'en',
      userProfilePhoto: 'data:image/png;base64,placeholder',
      userProfilePhotoUsingId: undefined,
      lastModifiedOnBehalfOfUserProfilePhoto: undefined,
      userProfile: undefined,
      lastModifiedOnBehalfOfUserProfile: undefined,
    },
    operations: {
      status: ServiceStatuses.Running,
      displayStatus: ServiceStatuses.Running,
      isLoading: false,
      error: null,
      isOperationInProgress: false,
    },
  };

  const gmmApiMock: IGMMApi = {
    settings: {
      fetchSettings: jest.fn().mockResolvedValue([]),
      fetchSettingByKey: jest
        .fn()
        .mockResolvedValue({ settingKey: SettingKey.DashboardUrl, settingValue: '' }),
      patchSetting: jest
        .fn()
        .mockResolvedValue({ settingKey: SettingKey.DashboardUrl, settingValue: '' }),
      getSupportEmailAddress: jest.fn().mockResolvedValue('support@example.com'),
    },
    roles: {
      getAllRoles: jest.fn().mockResolvedValue(rolesResponse),
    },
    sqlMembershipSources: {
      fetchDefaultSqlMembershipSource: jest.fn().mockResolvedValue(defaultSqlSource),
      fetchDefaultSqlMembershipSourceAttributes: jest
        .fn()
        .mockResolvedValue(defaultSqlAttributes),
      fetchDefaultSqlMembershipSourceAttributeMappings: jest.fn().mockResolvedValue([]),
      fetchDefaultSqlMembershipSourceAttributeValues: jest.fn().mockResolvedValue([]),
      patchDefaultSqlMembershipSourceCustomLabel: jest.fn().mockResolvedValue(undefined),
      patchDefaultSqlMembershipSourceAttributes: jest.fn().mockResolvedValue(undefined),
      validateSqlFilters: jest.fn().mockResolvedValue({ isValid: true, errors: new Map<number, string>() }),
    },
    operationsApi: {
      fetchServiceStatus: jest.fn().mockResolvedValue(ServiceStatuses.Running),
      processOperation: jest.fn().mockResolvedValue(undefined),
    },
    jobs: {
      getAllJobs: jest.fn().mockResolvedValue({ items: [], totalNumberOfPages: 0 }),
      postNewJob: jest.fn().mockResolvedValue({} as never),
      downloadJobs: jest.fn().mockResolvedValue({} as never),
      approveJobs: jest.fn().mockResolvedValue({} as never),
    },
    destinations: {
      createGroup: jest.fn().mockResolvedValue({} as never),
    },
    title: {
      getTitle: jest.fn().mockResolvedValue({} as never),
    },
  };

  const store = setupStore(preloadedState, { authenticationService }, { gmmApi: gmmApiMock });

  renderWithProviders(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <App />
    </MemoryRouter>,
    { store }
  );

  expect(await screen.findByText(/Membership Management/i)).toBeInTheDocument();
});

const buildNoAccessRoles = (): RolesResponse => ({
  isJobOwnerReader: false,
  isJobOwnerEnabler: false,
  isJobOwnerDeleter: false,
  isJobOwnerWriter: false,
  isJobTenantReader: false,
  isJobTenantWriter: false,
  isSubmissionReviewer: false,
  isSubmissionRejector: false,
  isHyperlinkAdministrator: false,
  isCustomMembershipProviderAdministrator: false,
  isOperationsResetAdministrator: false,
  isGeneralSettingsAdministrator: false,
  isFetchingRoles: false,
});

const buildPreloadedState = (
  authenticationService: OfflineAuthenticationService,
  rolesResponse: RolesResponse
): Partial<RootState> => ({
  account: {
    user: authenticationService.getActiveAccount(),
    loggedIn: true,
    loggingIn: false,
    loginError: undefined,
  },
  localization: {
    language: 'en',
    strings: defaultStrings,
  },
  roles: rolesResponse,
  profile: {
    userPreferredLanguage: 'en',
    userProfilePhoto: 'data:image/png;base64,placeholder',
    userProfilePhotoUsingId: undefined,
    lastModifiedOnBehalfOfUserProfilePhoto: undefined,
    userProfile: undefined,
    lastModifiedOnBehalfOfUserProfile: undefined,
  },
  operations: {
    status: ServiceStatuses.Running,
    displayStatus: ServiceStatuses.Running,
    isLoading: false,
    error: null,
    isOperationInProgress: false,
  },
});

const buildGmmApiMock = (
  rolesResponse: RolesResponse,
  settings: Array<{ settingKey: SettingKey; settingValue: string }>
): IGMMApi => ({
  settings: {
    fetchSettings: jest.fn().mockResolvedValue(settings),
    fetchSettingByKey: jest
      .fn()
      .mockResolvedValue({ settingKey: SettingKey.DashboardUrl, settingValue: '' }),
    patchSetting: jest
      .fn()
      .mockResolvedValue({ settingKey: SettingKey.DashboardUrl, settingValue: '' }),
    getSupportEmailAddress: jest.fn().mockResolvedValue('support@example.com'),
  },
  roles: {
    getAllRoles: jest.fn().mockResolvedValue(rolesResponse),
  },
  sqlMembershipSources: {
    fetchDefaultSqlMembershipSource: jest
      .fn()
      .mockResolvedValue({ name: 'Default Source', customLabel: 'Default Label' } as SqlMembershipSource),
    fetchDefaultSqlMembershipSourceAttributes: jest
      .fn()
      .mockResolvedValue([] as SqlMembershipAttribute[]),
    fetchDefaultSqlMembershipSourceAttributeMappings: jest.fn().mockResolvedValue([]),
    fetchDefaultSqlMembershipSourceAttributeValues: jest.fn().mockResolvedValue([]),
    patchDefaultSqlMembershipSourceCustomLabel: jest.fn().mockResolvedValue(undefined),
    patchDefaultSqlMembershipSourceAttributes: jest.fn().mockResolvedValue(undefined),
    validateSqlFilters: jest.fn().mockResolvedValue({ isValid: true, errors: new Map<number, string>() }),
  },
  operationsApi: {
    fetchServiceStatus: jest.fn().mockResolvedValue(ServiceStatuses.Running),
    processOperation: jest.fn().mockResolvedValue(undefined),
  },
  jobs: {
    getAllJobs: jest.fn().mockResolvedValue({ items: [], totalNumberOfPages: 0 }),
    postNewJob: jest.fn().mockResolvedValue({} as never),
    downloadJobs: jest.fn().mockResolvedValue({} as never),
    approveJobs: jest.fn().mockResolvedValue({} as never),
  },
  destinations: {
    createGroup: jest.fn().mockResolvedValue({} as never),
  },
  title: {
    getTitle: jest.fn().mockResolvedValue({} as never),
  },
});

test('renders existing permissionDenied message when user has no access and dashboardUrl is unset', async () => {
  const authenticationService = new OfflineAuthenticationService();
  const rolesResponse = buildNoAccessRoles();
  const preloadedState = buildPreloadedState(authenticationService, rolesResponse);
  const gmmApiMock = buildGmmApiMock(rolesResponse, []);

  const store = setupStore(preloadedState, { authenticationService }, { gmmApi: gmmApiMock });

  const { container } = renderWithProviders(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <App />
    </MemoryRouter>,
    { store }
  );

  expect(await screen.findByText(defaultStrings.permissionDenied)).toBeInTheDocument();
  expect(
    container.querySelector('a[href="https://example.com/gmm-info"]')
  ).toBeNull();
});

test('renders access guidance with dashboard link when user has no access and dashboardUrl is set', async () => {
  const authenticationService = new OfflineAuthenticationService();
  const rolesResponse = buildNoAccessRoles();
  const dashboardSettings = [
    { settingKey: SettingKey.DashboardUrl, settingValue: 'https://example.com/gmm-info' },
  ];
  const preloadedState = buildPreloadedState(authenticationService, rolesResponse);
  const gmmApiMock = buildGmmApiMock(rolesResponse, dashboardSettings);

  const store = setupStore(preloadedState, { authenticationService }, { gmmApi: gmmApiMock });

  const { container } = renderWithProviders(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <App />
    </MemoryRouter>,
    { store }
  );

  expect(
    await screen.findByText(defaultStrings.noOwnedGroupsAccessGuidanceLinkLabel)
  ).toBeInTheDocument();

  const link = container.querySelector('a[href="https://example.com/gmm-info"]') as HTMLAnchorElement | null;
  expect(link).not.toBeNull();
  expect(link?.getAttribute('target')).toBe('_blank');
  expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
});

test('renders existing permissionDenied message when dashboardUrl uses an unsafe scheme', async () => {
  const authenticationService = new OfflineAuthenticationService();
  const rolesResponse = buildNoAccessRoles();
  const dashboardSettings = [
    { settingKey: SettingKey.DashboardUrl, settingValue: 'javascript:alert(1)' },
  ];
  const preloadedState = buildPreloadedState(authenticationService, rolesResponse);
  const gmmApiMock = buildGmmApiMock(rolesResponse, dashboardSettings);

  const store = setupStore(preloadedState, { authenticationService }, { gmmApi: gmmApiMock });

  const { container } = renderWithProviders(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <App />
    </MemoryRouter>,
    { store }
  );

  expect(await screen.findByText(defaultStrings.permissionDenied)).toBeInTheDocument();
  expect(container.querySelector('a[href^="javascript:"]')).toBeNull();
});

test('renders the maintenance page when fetchServiceStatus fails for a service-class reason', async () => {
  const authenticationService = new OfflineAuthenticationService();
  const rolesResponse = buildNoAccessRoles();
  const preloadedState = buildPreloadedState(authenticationService, rolesResponse);
  const gmmApiMock = buildGmmApiMock(rolesResponse, []);

  // The backend is genuinely unavailable (5xx). This is the only class of
  // failure that should surface the maintenance page.
  const serviceUnavailableError = Object.assign(new Error('Service Unavailable'), {
    isAxiosError: true,
    response: { status: 503 },
  });
  (gmmApiMock.operationsApi.fetchServiceStatus as any).mockRejectedValue(serviceUnavailableError);

  const store = setupStore(preloadedState, { authenticationService }, { gmmApi: gmmApiMock });

  renderWithProviders(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <App />
    </MemoryRouter>,
    { store }
  );

  expect(await screen.findByText(defaultStrings.maintenanceTitle)).toBeInTheDocument();
});

test('renders the maintenance page when fetchServiceStatus fails for an unexpected (non-auth) reason', async () => {
  const authenticationService = new OfflineAuthenticationService();
  const rolesResponse = buildNoAccessRoles();
  const preloadedState = buildPreloadedState(authenticationService, rolesResponse);
  const gmmApiMock = buildGmmApiMock(rolesResponse, []);

  // An unexpected, unclassified error must fail safe to maintenance rather than
  // being silently hidden as an auth failure.
  (gmmApiMock.operationsApi.fetchServiceStatus as any).mockRejectedValue(new Error('unexpected boom'));

  const store = setupStore(preloadedState, { authenticationService }, { gmmApi: gmmApiMock });

  renderWithProviders(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <App />
    </MemoryRouter>,
    { store }
  );

  expect(await screen.findByText(defaultStrings.maintenanceTitle)).toBeInTheDocument();
});

test('does not render the maintenance page when fetchServiceStatus fails for an auth-class reason', async () => {
  const authenticationService = new OfflineAuthenticationService();
  const rolesResponse: RolesResponse = { ...buildNoAccessRoles(), isJobOwnerReader: true };
  const preloadedState = buildPreloadedState(authenticationService, rolesResponse);
  const gmmApiMock = buildGmmApiMock(rolesResponse, []);

  // A 401 means the user is authenticated but not authorized for this call. It
  // is an auth problem, not a service outage, so it must never be surfaced as
  // maintenance (which would mask the real cause).
  const unauthorizedError = Object.assign(new Error('Unauthorized'), {
    isAxiosError: true,
    response: { status: 401 },
  });
  (gmmApiMock.operationsApi.fetchServiceStatus as any).mockRejectedValue(unauthorizedError);

  const store = setupStore(preloadedState, { authenticationService }, { gmmApi: gmmApiMock });

  renderWithProviders(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <App />
    </MemoryRouter>,
    { store }
  );

  // The app gets past the loader (header renders in both the maintenance and
  // non-maintenance branches)...
  expect(await screen.findByText(/Membership Management/i)).toBeInTheDocument();
  // ...the failing status fetch actually ran (so the thunk's pending reducer has
  // set isLoading=true)...
  await waitFor(() => expect(gmmApiMock.operationsApi.fetchServiceStatus).toHaveBeenCalled());
  // ...and once it has SETTLED (isLoading back to false), an auth-class failure
  // leaves the maintenance error unset — asserting isLoading too prevents this
  // from passing prematurely while the thunk is still pending (error is also
  // null during pending).
  await waitFor(() => {
    const operations = store.getState().operations;
    expect(operations.isLoading).toBe(false);
    expect(operations.error).toBeNull();
  });
  expect(screen.queryByText(defaultStrings.maintenanceTitle)).toBeNull();
});

test('does not render the maintenance page when fetchServiceStatus fails with an MSAL token-flow error', async () => {
  const authenticationService = new OfflineAuthenticationService();
  const rolesResponse: RolesResponse = { ...buildNoAccessRoles(), isJobOwnerReader: true };
  const preloadedState = buildPreloadedState(authenticationService, rolesResponse);
  const gmmApiMock = buildGmmApiMock(rolesResponse, []);

  // A stale MSAL session can make getTokenAsync rethrow an MSAL auth-layer
  // error (e.g. interaction_in_progress when a redirect is already in flight).
  // It is not an AxiosError and carries a string `errorCode`, so it must be
  // classified as auth and never surfaced as maintenance.
  const msalTokenError = Object.assign(new Error('interaction_in_progress: a redirect is already in progress'), {
    errorCode: 'interaction_in_progress',
    name: 'BrowserAuthError',
  });
  (gmmApiMock.operationsApi.fetchServiceStatus as any).mockRejectedValue(msalTokenError);

  const store = setupStore(preloadedState, { authenticationService }, { gmmApi: gmmApiMock });

  renderWithProviders(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <App />
    </MemoryRouter>,
    { store }
  );

  expect(await screen.findByText(/Membership Management/i)).toBeInTheDocument();
  await waitFor(() => expect(gmmApiMock.operationsApi.fetchServiceStatus).toHaveBeenCalled());
  await waitFor(() => {
    const operations = store.getState().operations;
    expect(operations.isLoading).toBe(false);
    expect(operations.error).toBeNull();
  });
  expect(screen.queryByText(defaultStrings.maintenanceTitle)).toBeNull();
});
