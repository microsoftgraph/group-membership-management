// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen, within } from '@testing-library/react';
import { vi, beforeAll } from 'vitest';
import { initializeIcons } from '@fluentui/react';
import { MemoryRouter } from 'react-router-dom';
import { AdminConfigView } from './AdminConfig.view';
import { getStyles } from './AdminConfig.styles';
import { SettingKey } from '../../models';
import { defaultStrings } from '../../services/localization';
import { renderWithProviders } from '../../testing/renderWithProviders';

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

beforeAll(() => {
  initializeIcons(undefined, { disableWarnings: true });
});

const createSettings = (overrides?: Partial<Record<SettingKey, string>>): { readonly [key in SettingKey]: string } => ({
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
  [SettingKey.IsAISearchForUserEnabled]: 'false',
  [SettingKey.IsAIRunExplanationEnabled]: 'false',
  [SettingKey.RunHistoryOpenViewingAndUnifiedTab]: 'false',
  [SettingKey.CopilotTemperature]: '0.7',
  [SettingKey.CopilotTopP]: '0.9',
  [SettingKey.CopilotInstructions]: '',
  [SettingKey.CopilotSuggestedPrompts]: '',
  ...overrides,
});

const renderAdminConfigView = (defaultAIPrompt: string, settingsOverrides?: Partial<Record<SettingKey, string>>) =>
  renderWithProviders(
    <MemoryRouter>
      <AdminConfigView
        isSaving={false}
        onSave={vi.fn()}
        handleGetValues={vi.fn()}
        settings={createSettings(settingsOverrides)}
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
    </MemoryRouter>
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

describe('SuggestedPromptsEditor', () => {
  const twoPrompts = JSON.stringify([
    { label: 'Include reports', prompt: 'Include all reports' },
    { label: 'Include group members', prompt: 'Include all group members' },
  ]);

  test('renders existing prompts from settings', () => {
    renderAdminConfigView('', {
      [SettingKey.CopilotSuggestedPrompts]: twoPrompts,
    });

    expect(screen.getByText(defaultStrings.AdminConfig.AISettings.labels.suggestedPromptsTitle)).toBeInTheDocument();
    expect(screen.getByDisplayValue('Include reports')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Include all reports')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Include group members')).toBeInTheDocument();
  });

  test('renders empty state when no prompts configured', () => {
    renderAdminConfigView('');

    expect(screen.getByText(defaultStrings.AdminConfig.AISettings.labels.suggestedPromptsTitle)).toBeInTheDocument();
    expect(screen.queryByDisplayValue('Include reports')).not.toBeInTheDocument();
  });

  test('add prompt button creates a new empty row', () => {
    renderAdminConfigView('');

    const addButton = screen.getByText(defaultStrings.AdminConfig.AISettings.labels.suggestedPromptAdd);
    fireEvent.click(addButton);

    const labelPlaceholder = defaultStrings.AdminConfig.AISettings.labels.suggestedPromptLabelPlaceholder;
    expect(screen.getByPlaceholderText(labelPlaceholder)).toBeInTheDocument();
  });

  test('populate defaults button fills in default prompts', () => {
    renderAdminConfigView('');

    const populateButton = screen.getByText(defaultStrings.AdminConfig.AISettings.labels.suggestedPromptPopulateDefaults);
    fireEvent.click(populateButton);

    const default1Label = defaultStrings.AdminConfig.AISettings.labels.suggestedPromptDefault1Label;
    const default2Label = defaultStrings.AdminConfig.AISettings.labels.suggestedPromptDefault2Label;
    expect(screen.getAllByDisplayValue(default1Label).length).toBeGreaterThanOrEqual(1);
    expect(screen.getByDisplayValue(default2Label)).toBeInTheDocument();
  });

  test('delete button removes a prompt row', () => {
    renderAdminConfigView('', {
      [SettingKey.CopilotSuggestedPrompts]: twoPrompts,
    });

    expect(screen.getByDisplayValue('Include reports')).toBeInTheDocument();

    const deleteButtons = screen.getAllByRole('button', { name: 'Remove' });
    fireEvent.click(deleteButtons[0]);

    expect(screen.queryByDisplayValue('Include reports')).not.toBeInTheDocument();
    expect(screen.getByDisplayValue('Include group members')).toBeInTheDocument();
  });

  test('editing a prompt label updates the field value', () => {
    renderAdminConfigView('', {
      [SettingKey.CopilotSuggestedPrompts]: twoPrompts,
    });

    const labelInput = screen.getByDisplayValue('Include reports');
    fireEvent.change(labelInput, { target: { value: 'Updated label' } });

    expect(screen.getByDisplayValue('Updated label')).toBeInTheDocument();
  });

  test('handles malformed JSON gracefully with no prompts shown', () => {
    renderAdminConfigView('', {
      [SettingKey.CopilotSuggestedPrompts]: 'not valid json',
    });

    expect(screen.getByText(defaultStrings.AdminConfig.AISettings.labels.suggestedPromptsTitle)).toBeInTheDocument();
    const deleteButtons = screen.queryAllByRole('button', { name: 'Remove' });
    expect(deleteButtons).toHaveLength(0);
  });

  test('coerces non-string fields to empty strings and keeps rows editable', () => {
    const mixedJson = JSON.stringify([
      { label: 'Valid', prompt: 'Valid prompt' },
      { label: 123, prompt: 'Number label' },
    ]);

    renderAdminConfigView('', {
      [SettingKey.CopilotSuggestedPrompts]: mixedJson,
    });

    expect(screen.getByDisplayValue('Valid')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Valid prompt')).toBeInTheDocument();
    // Non-string label coerced to empty string — row kept so admin can fix it
    const deleteButtons = screen.getAllByRole('button', { name: 'Remove' });
    expect(deleteButtons).toHaveLength(2);
  });

  test('renders empty state for empty JSON array', () => {
    renderAdminConfigView('', {
      [SettingKey.CopilotSuggestedPrompts]: '[]',
    });

    expect(screen.getByText(defaultStrings.AdminConfig.AISettings.labels.suggestedPromptsTitle)).toBeInTheDocument();
    expect(screen.queryAllByRole('button', { name: 'Remove' })).toHaveLength(0);
  });

  test('populate defaults creates exactly 2 default prompts', () => {
    renderAdminConfigView('');

    fireEvent.click(screen.getByText(defaultStrings.AdminConfig.AISettings.labels.suggestedPromptPopulateDefaults));

    expect(screen.getAllByRole('button', { name: 'Remove' })).toHaveLength(2);
  });
});
