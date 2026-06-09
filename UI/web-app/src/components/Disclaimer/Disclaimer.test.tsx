import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { Disclaimer } from './Disclaimer';
import { SettingKey } from '../../models/SettingKey';

describe('Disclaimer', () => {
  const baseSettingsState = {
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
  };

  const checkboxes = [
    { id: 'membershipRules', label: 'Review membership rules' },
    { id: 'autoSubscribeSettings', label: 'Acknowledge auto-subscribe settings' },
  ];

  beforeEach(() => {
    localStorage.clear();
  });

  it('enables submission after all checkboxes are confirmed', () => {
    const onDismiss = jest.fn();

    renderWithProviders(
      <Disclaimer checkboxes={checkboxes} onDismiss={onDismiss} />,
      {
        preloadedState: {
          settings: baseSettingsState,
        },
      }
    );

    const submitButton = screen.getByRole('button', { name: 'I Agree' });
    expect(submitButton).toBeDisabled();

    checkboxes.forEach(({ label }) => {
      fireEvent.click(screen.getByRole('checkbox', { name: label }));
    });

    expect(submitButton).not.toBeDisabled();

    fireEvent.click(submitButton);

    expect(localStorage.getItem('disclaimerSubmitted')).toBe('true');
    expect(onDismiss).toHaveBeenCalledTimes(1);
  });

  it('returns null when disclaimers are disabled', () => {
    const { container } = renderWithProviders(
      <Disclaimer checkboxes={checkboxes} />,
      {
        preloadedState: {
          settings: {
            ...baseSettingsState,
            settings: [
              {
                settingKey: SettingKey.IsDisclaimerEnabled,
                settingValue: 'false',
              },
            ],
          },
        },
      }
    );

    expect(container.firstChild).toBeNull();
  });
});
