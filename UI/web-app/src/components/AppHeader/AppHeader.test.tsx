// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { MemoryRouter } from 'react-router-dom';
import { screen } from '@testing-library/react';

import { renderWithProviders } from '../../testing/renderWithProviders';
import { defaultStrings } from '../../services/localization';
import { AppHeader } from './AppHeader';
import { SettingKey } from '../../models/SettingKey';

describe('AppHeader', () => {
  const authenticationServiceMock = {
    loginAsync: jest.fn().mockResolvedValue(undefined),
    getActiveAccount: jest.fn().mockReturnValue({
      id: 'test-account-id',
      name: 'Test User',
      username: 'test.user@example.com',
    }),
    getTokenAsync: jest.fn().mockResolvedValue('test-token'),
  };

  it('renders the app header title', () => {
    renderWithProviders(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <AppHeader />
      </MemoryRouter>,
      {
        preloadedState: {
          localization: {
            language: 'en',
            strings: defaultStrings,
          },
          profile: {
            userProfilePhoto: 'data:image/png;base64,placeholder',
          },
        },
        serviceMocks: {
          authenticationService: authenticationServiceMock,
        },
      }
    );

    expect(screen.getByText(defaultStrings.membershipManagement)).toBeInTheDocument();
  });

  it('renders settings and disclaimer actions when enabled', () => {
    renderWithProviders(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <AppHeader />
      </MemoryRouter>,
      {
        preloadedState: {
          localization: {
            language: 'en',
            strings: defaultStrings,
          },
          profile: {
            userProfilePhoto: 'data:image/png;base64,placeholder',
          },
          roles: {
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
            isGeneralSettingsAdministrator: true,
            isFetchingRoles: false,
          },
          settings: {
            selectedSettingLoading: false,
            selectedSetting: undefined,
            settings: [
              {
                settingKey: SettingKey.IsDisclaimerEnabled,
                settingValue: 'true',
              },
            ],
            isLoading: false,
            error: undefined,
            patchSettingResponse: undefined,
            patchSettingError: undefined,
            isSaving: false,
            supportEmail: '',
            supportEmailLoading: false,
            supportEmailError: undefined,
          },
        },
        serviceMocks: {
          authenticationService: authenticationServiceMock,
        },
      }
    );

    expect(
      screen.getByRole('button', {
        name: defaultStrings.Components.AppHeader.settings,
      })
    ).toBeInTheDocument();

    expect(
      screen.getByRole('button', {
        name: defaultStrings.Components.AppHeader.reviewDisclaimer,
      })
    ).toBeInTheDocument();
  });
});
