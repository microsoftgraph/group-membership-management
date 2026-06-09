// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { screen } from '@testing-library/react';
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
