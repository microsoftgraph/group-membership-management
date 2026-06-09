import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { EndpointsList } from './EndpointsList';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { SettingKey } from '../../models/SettingKey';
import type { SettingsState } from '../../store/settings.slice';

const createSettingsState = (): SettingsState => ({
  selectedSettingLoading: false,
  selectedSetting: undefined,
  settings: [
    {
      settingKey: SettingKey.OutlookWarningUrl,
      settingValue: 'https://contoso.example/help',
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
}) as SettingsState;

describe('EndpointsList', () => {
  const originalDomain = process.env.REACT_APP_DOMAINNAME;
  const originalSharePointDomain = process.env.REACT_APP_SHAREPOINTDOMAIN;

  beforeAll(() => {
    process.env.REACT_APP_DOMAINNAME = 'contoso.onmicrosoft.com';
    process.env.REACT_APP_SHAREPOINTDOMAIN = 'contoso.sharepoint.com';
  });

  afterAll(() => {
    process.env.REACT_APP_DOMAINNAME = originalDomain;
    process.env.REACT_APP_SHAREPOINTDOMAIN = originalSharePointDomain;
  });

  it('returns null when endpoints do not include supported apps', () => {
    const { container } = renderWithProviders(
      <EndpointsList endpoints={['Other']} groupName="Example Group" />, {
        preloadedState: {
          settings: createSettingsState(),
        },
      }
    );

    expect(container.firstChild).toBeNull();
  });

  it('renders the security group message when the only endpoint is SecurityGroup', () => {
    renderWithProviders(
      <EndpointsList endpoints={['SecurityGroup']} groupName="Example Group" />, {
        preloadedState: {
          settings: createSettingsState(),
        },
      }
    );

    expect(
      screen.getByText('This is an Entra Security Group')
    ).toBeInTheDocument();
  });

  it('opens the associated applications when their buttons are clicked', () => {
    const openSpy = jest.spyOn(window, 'open').mockImplementation(() => null);

    renderWithProviders(
      <EndpointsList
        endpoints={['Outlook', 'SharePoint', 'Microsoft Teams', 'Yammer']}
        groupName="Team Space"
        showOutlookWarning
      />, {
        preloadedState: {
          settings: createSettingsState(),
        },
      }
    );

    fireEvent.click(screen.getByRole('button', { name: 'Outlook' }));
    expect(openSpy).toHaveBeenCalledWith(
      'https://outlook.office.com/mail/group/contoso.onmicrosoft.com/TeamSpace',
      '_blank',
      'noopener,noreferrer'
    );

    fireEvent.click(screen.getByRole('button', { name: 'Learn more' }));
    expect(openSpy).toHaveBeenCalledWith(
      'https://contoso.example/help',
      '_blank',
      'noopener,noreferrer'
    );

    fireEvent.click(screen.getByRole('button', { name: 'SharePoint' }));
    expect(openSpy).toHaveBeenCalledWith(
      'https://contoso.sharepoint.com/sites/TeamSpace',
      '_blank',
      'noopener,noreferrer'
    );

    fireEvent.click(screen.getByRole('button', { name: 'Teams' }));
    expect(openSpy).toHaveBeenCalledWith(
      'https://teams.microsoft.com/l/team/contoso.onmicrosoft.com/TeamSpace',
      '_blank',
      'noopener,noreferrer'
    );

    expect(screen.getByText('Viva Engage')).toBeInTheDocument();

    openSpy.mockRestore();
  });
});
