import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { Banner } from './Banner';
import { SettingKey } from '../../models/SettingKey';
import type { SettingsState } from '../../store/settings.slice';
import { defaultStrings } from '../../services/localization';

describe('Banner', () => {
  const baseSettingsState: SettingsState = {
    selectedSettingLoading: false,
    selectedSetting: undefined,
    settings: [
      {
        settingKey: SettingKey.DashboardUrl,
        settingValue: 'https://contoso.example/dashboard',
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
  } as const;

  it('renders messaging when a dashboard URL is configured', () => {
    renderWithProviders(<Banner />, {
      preloadedState: {
        settings: baseSettingsState,
      },
    });

    expect(screen.getByText(/Need help\?/)).toBeInTheDocument();
    expect(
      screen.getByRole('link', {
        name: defaultStrings.learnMembershipManagement,
      })
    ).toHaveAttribute(
      'href',
      'https://contoso.example/dashboard'
    );
  });

  it('opens the dashboard in a new tab when the link is clicked', () => {
    const openSpy = jest
      .spyOn(window, 'open')
      .mockImplementation(() => null);

    renderWithProviders(<Banner />, {
      preloadedState: {
        settings: baseSettingsState,
      },
    });

    fireEvent.click(
      screen.getByRole('link', {
        name: defaultStrings.learnMembershipManagement,
      })
    );

    expect(openSpy).toHaveBeenCalledWith(
      'https://contoso.example/dashboard',
      '_blank',
      'noopener,noreferrer'
    );

    openSpy.mockRestore();
  });

  it('hides the banner content when toggled', () => {
    renderWithProviders(<Banner />, {
      preloadedState: {
        settings: baseSettingsState,
      },
    });

    fireEvent.click(screen.getByTitle('Expand banner'));

    expect(screen.queryByText('Click here')).toBeNull();
  });

  it('sets an accessible label on the help link', () => {
    renderWithProviders(<Banner />, {
      preloadedState: {
        settings: baseSettingsState,
      },
    });

    expect(
      screen.getByRole('link', {
        name: defaultStrings.learnMembershipManagement,
      })
    ).toBeInTheDocument();
  });

  it('returns null when no dashboard URL is available', () => {
    const { container } = renderWithProviders(<Banner />, {
      preloadedState: {
        settings: {
          ...baseSettingsState,
          settings: undefined,
        },
      },
    });

    expect(container.firstChild).toBeNull();
  });
});
