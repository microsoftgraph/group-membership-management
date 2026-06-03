// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { vi } from 'vitest';
import { AdminConfigView } from './AdminConfig.view';
import { getStyles } from './AdminConfig.styles';
import { SettingKey } from '../../models';
import { defaultStrings } from '../../services/localization';

vi.mock('../../components/PageHeader', () => ({
  PageHeader: () => <div data-testid="page-header" />,
}));

vi.mock('../../components/Page', () => ({
  Page: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('../../components/PageSection', () => ({
  PageSection: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('../../components/GeneralSetting', () => ({
  GeneralSetting: ({ title, description }: { title: string; description: string }) => (
    <div>
      <div>{title}</div>
      <div>{description}</div>
    </div>
  ),
}));

vi.mock('../../components/HyperlinkSetting', () => ({
  HyperlinkSetting: () => <div />,
}));

vi.mock('../../components/Operation', () => ({
  Operation: () => <div />,
}));

const createSettings = (): { readonly [key in SettingKey]: string } => ({
  [SettingKey.DashboardUrl]: 'https://contoso.example/dashboard',
  [SettingKey.OutlookWarningUrl]: 'https://contoso.example/outlook-warning',
  [SettingKey.PrivacyPolicyUrl]: 'https://contoso.example/privacy',
  [SettingKey.UIUrl]: 'http://localhost:3000',
  [SettingKey.CanReviewOwnSubmissions]: 'true',
  [SettingKey.CreateGroupFeatureEnabled]: 'true',
  [SettingKey.IsBusinessJustificationRequired]: 'false',
  [SettingKey.IsDisclaimerEnabled]: 'false',
  [SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled]: 'false',
  [SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled]: 'false',
  [SettingKey.IsAITitleEnabled]: 'true',
  [SettingKey.IsAICopilotEnabled]: 'true',
  [SettingKey.CopilotTemperature]: '0.7',
  [SettingKey.CopilotTopP]: '0.9',
  [SettingKey.CopilotInstructions]: '',
});

const renderAdminConfigView = (defaultAIPrompt: string) =>
  render(
    <AdminConfigView
      isSaving={false}
      onSave={vi.fn()}
      handleGetValues={vi.fn()}
      settings={createSettings()}
      sqlMembershipSource={undefined}
      sqlMembershipSourceAttributes={undefined}
      strings={defaultStrings.AdminConfig}
      styles={getStyles}
      isHyperlinkAdmin={false}
      isCustomMembershipProviderAdmin={false}
      isOperationsResetAdministrator={false}
      isGeneralSettingsAdministrator={false}
      isAISettingsAdministrator={true}
      defaultAIPrompt={defaultAIPrompt}
    />
  );

describe('AdminConfigView AI settings', () => {
  test('shows and toggles default instructions panel', () => {
    const defaultPrompt = 'You are a concise assistant.';
    renderAdminConfigView(defaultPrompt);

    const toggleButton = screen.getByRole('button', {
      name: defaultStrings.AdminConfig.AISettings.labels.currentDefaultInstructions,
    });

    expect(toggleButton).toBeInTheDocument();
    expect(screen.queryByText(defaultPrompt)).not.toBeInTheDocument();

    fireEvent.click(toggleButton);
    expect(screen.getByText(defaultPrompt)).toBeInTheDocument();

    fireEvent.click(toggleButton);
    expect(screen.queryByText(defaultPrompt)).not.toBeInTheDocument();
  });

  test('hides default instructions toggle when no prompt is available', () => {
    renderAdminConfigView('');

    expect(
      screen.queryByRole('button', {
        name: defaultStrings.AdminConfig.AISettings.labels.currentDefaultInstructions,
      })
    ).not.toBeInTheDocument();
  });
});
