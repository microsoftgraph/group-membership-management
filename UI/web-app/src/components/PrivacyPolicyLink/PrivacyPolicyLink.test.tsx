// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { screen } from '@testing-library/react';

import { renderWithProviders } from '../../testing/renderWithProviders';
import { PrivacyPolicyLink } from './PrivacyPolicyLink';
import { SettingKey } from '../../models/SettingKey';
import type { RootState } from '../../store';
import type { SettingsState } from '../../store/settings.slice';
import { defaultStrings } from '../../services/localization';

const createSettingsState = (settings?: SettingsState['settings']): SettingsState => ({
  selectedSettingLoading: false,
  selectedSetting: undefined,
  settings,
  isLoading: false,
  error: undefined,
  patchSettingResponse: undefined,
  patchSettingError: undefined,
  isSaving: false,
  supportEmail: '',
  supportEmailLoading: false,
  supportEmailError: undefined,
});

describe('PrivacyPolicyLink', () => {
  it('renders a link when a privacy policy URL is configured', () => {
    const preloadedState = {
      settings: createSettingsState([
        { settingKey: SettingKey.PrivacyPolicyUrl, settingValue: 'https://contoso.test/privacy' },
      ]),
      localization: {
        language: 'en',
        strings: defaultStrings,
      },
    } satisfies Partial<RootState>;

    renderWithProviders(<PrivacyPolicyLink />, { preloadedState });

    const link = screen.getByRole('link', { name: defaultStrings.privacyPolicy });
    expect(link).toHaveAttribute('href', 'https://contoso.test/privacy');
    expect(link).toHaveAttribute('target', '_blank');
  });

  it('does not render when a privacy policy URL is not provided', () => {
    const preloadedState = {
      settings: createSettingsState([
        { settingKey: SettingKey.PrivacyPolicyUrl, settingValue: '' },
      ]),
      localization: {
        language: 'en',
        strings: defaultStrings,
      },
    } satisfies Partial<RootState>;

    const { queryByRole } = renderWithProviders(<PrivacyPolicyLink />, { preloadedState });

    expect(queryByRole('link', { name: defaultStrings.privacyPolicy })).toBeNull();
  });
});
