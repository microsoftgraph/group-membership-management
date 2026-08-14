// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { vi, beforeAll } from 'vitest';
import { initializeIcons } from '@fluentui/react';
import { MemoryRouter } from 'react-router-dom';
import { AdminConfigView, fractionToPercentText } from './AdminConfig.view';
import { getStyles } from './AdminConfig.styles';
import type { AdminConfigViewProps } from './AdminConfig.types';
import { SettingKey, SqlMembershipAttribute } from '../../models';
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
        isAutoApproverAdministrator={false}
        isCustomMembershipProviderAdmin={false}
        isOperationsResetAdministrator={false}
        isGeneralSettingsAdministrator={false}
        isAISettingsAdministrator={true}
        defaultAIPrompt={defaultAIPrompt}
      />
    </MemoryRouter>
  );

describe('AdminConfigView tab layout', () => {
  const renderWithRoles = (roles: {
    isAutoApproverAdministrator: boolean;
    isCustomMembershipProviderAdmin: boolean;
    isOperationsResetAdministrator: boolean;
    isGeneralSettingsAdministrator: boolean;
    isAISettingsAdministrator: boolean;
  }) =>
    renderWithProviders(
      <MemoryRouter>
        <AdminConfigView
          isSaving={false}
          onSave={vi.fn()}
          handleGetValues={vi.fn()}
          settings={createSettings()}
          sqlMembershipSource={undefined}
          sqlMembershipSourceAttributes={undefined}
          strings={defaultStrings.AdminConfig}
          styles={getStyles}
          defaultAIPrompt={''}
          {...roles}
        />
      </MemoryRouter>
    );

  const allRoles = {
    isAutoApproverAdministrator: true,
    isCustomMembershipProviderAdmin: true,
    isOperationsResetAdministrator: true,
    isGeneralSettingsAdministrator: true,
    isAISettingsAdministrator: true,
  };

  test('renders the six tabs in the redesigned order', () => {
    renderWithRoles(allRoles);

    const tabNames = screen.getAllByRole('tab').map((tab) => tab.textContent?.trim());

    expect(tabNames).toEqual([
      defaultStrings.AdminConfig.GeneralSettings.labels.general,
      defaultStrings.AdminConfig.Operations.labels.operations,
      defaultStrings.AdminConfig.CustomSourceSettings.labels.customSource,
      defaultStrings.AdminConfig.AISettings.labels.aiSettings,
      defaultStrings.AdminConfig.AutoApproverSettings.labels.autoApprover,
      defaultStrings.AdminConfig.labels.alertBanner,
    ]);
  });

  test('hides the Auto Approver tab when the user lacks the Auto Approver role', () => {
    renderWithRoles({ ...allRoles, isAutoApproverAdministrator: false });

    const tabNames = screen.getAllByRole('tab').map((tab) => tab.textContent?.trim());

    expect(tabNames).not.toContain(defaultStrings.AdminConfig.AutoApproverSettings.labels.autoApprover);
  });

  test('keeps auto approval toggles off the General tab', () => {
    renderWithRoles(allRoles);

    expect(
      screen.queryByText(defaultStrings.AdminConfig.AutoApproverSettings.labels.isAutoApprovalForGroupBasedSyncsEnabledTitle)
    ).not.toBeInTheDocument();
    expect(screen.getByText(defaultStrings.AdminConfig.GeneralSettings.labels.featureControl)).toBeInTheDocument();
  });

  test('shows the auto approval toggles once the Auto Approver tab is selected', () => {
    renderWithRoles(allRoles);

    fireEvent.click(
      screen.getByRole('tab', { name: defaultStrings.AdminConfig.AutoApproverSettings.labels.autoApprover })
    );

    expect(
      screen.getByText(defaultStrings.AdminConfig.AutoApproverSettings.labels.isAutoApprovalForGroupBasedSyncsEnabledTitle)
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        defaultStrings.AdminConfig.AutoApproverSettings.labels.isAutoApprovalForRequestorIsOrgLeaderSyncsEnabledTitle
      )
    ).toBeInTheDocument();
  });
});

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

describe('AdminConfigView Custom Source', () => {
  const csLabels = defaultStrings.AdminConfig.CustomSourceSettings.labels;

  const sensitiveAttributes: SqlMembershipAttribute[] = [
    { name: 'Salary', customLabel: '', type: 'int', hasMapping: false, values: [], description: '', enabled: false, isSensitive: true },
    { name: 'Country', customLabel: '', type: 'nvarchar', hasMapping: false, values: [], description: '', enabled: true, isSensitive: false },
  ];

  const renderCustomSourceView = (onSave: ReturnType<typeof vi.fn> = vi.fn()) =>
    renderWithProviders(
      <MemoryRouter>
        <AdminConfigView
          isSaving={false}
          onSave={onSave as unknown as AdminConfigViewProps['onSave']}
          handleGetValues={vi.fn()}
          settings={createSettings()}
          sqlMembershipSource={undefined}
          sqlMembershipSourceAttributes={sensitiveAttributes}
          strings={defaultStrings.AdminConfig}
          styles={getStyles}
          isAutoApproverAdministrator={false}
          isCustomMembershipProviderAdmin={true}
          isOperationsResetAdministrator={false}
          isGeneralSettingsAdministrator={false}
          isAISettingsAdministrator={false}
          defaultAIPrompt={''}
        />
      </MemoryRouter>
    );

  test('renders the Sensitive column alongside Enabled on the Custom Source tab', () => {
    renderCustomSourceView();

    expect(screen.getByText(csLabels.enabledColumn)).toBeInTheDocument();
    expect(screen.getByText(csLabels.sensitiveColumn)).toBeInTheDocument();
  });

  test('clicking the Sensitive header sorts rows so sensitive attributes group together', () => {
    renderCustomSourceView();

    // Unsorted initial order matches the input: Salary (sensitive) before Country (not sensitive)
    expect(
      screen.getByText('Salary').compareDocumentPosition(screen.getByText('Country')) & Node.DOCUMENT_POSITION_FOLLOWING
    ).toBeTruthy();

    // Click the Sensitive column header -> ascending sort by isSensitive (non-sensitive first)
    fireEvent.click(screen.getByText(csLabels.sensitiveColumn));

    // Rows reorder: Country (not sensitive) now comes before Salary (sensitive)
    expect(
      screen.getByText('Country').compareDocumentPosition(screen.getByText('Salary')) & Node.DOCUMENT_POSITION_FOLLOWING
    ).toBeTruthy();
  });

});

describe('AdminConfigView custom source null threshold', () => {
  const nullThresholdLabels = defaultStrings.AdminConfig.CustomSourceSettings.labels;

  const attributes: SqlMembershipAttribute[] = [
    {
      name: 'AssignmentType',
      customLabel: '',
      type: 'nvarchar',
      hasMapping: true,
      values: [],
      description: '',
      enabled: true,
      nullThreshold: 1,
    },
    {
      name: 'WorkRoom',
      customLabel: '',
      type: 'nvarchar',
      hasMapping: false,
      values: [],
      description: '',
      enabled: true,
    },
  ];

  const renderCustomSource = (onSave = vi.fn()) =>
    renderWithProviders(
      <MemoryRouter>
        <AdminConfigView
          isSaving={false}
          onSave={onSave}
          handleGetValues={vi.fn()}
          settings={createSettings()}
          sqlMembershipSource={{ name: 'SqlMembership', customLabel: 'HR Data' }}
          sqlMembershipSourceAttributes={attributes}
          strings={defaultStrings.AdminConfig}
          styles={getStyles}
          isCustomMembershipProviderAdmin={true}
          isOperationsResetAdministrator={false}
          isGeneralSettingsAdministrator={false}
          isAutoApproverAdministrator={false}
          isAISettingsAdministrator={false}
          defaultAIPrompt={''}
        />
      </MemoryRouter>
    );

  const getThresholdInput = (attributeName: string) =>
    screen.getByLabelText(`${nullThresholdLabels.nullThresholdColumn} ${attributeName}`) as HTMLInputElement;

  test('renders the null threshold column and shows the stored fraction as a percentage', () => {
    renderCustomSource();

    expect(screen.getByText(nullThresholdLabels.nullThresholdColumn)).toBeInTheDocument();

    // 1 (fraction) is displayed as 100 (percent); an unset threshold renders blank.
    expect(getThresholdInput('AssignmentType').value).toBe('100');
    expect(getThresholdInput('WorkRoom').value).toBe('');
  });

  test('converts an entered percentage back to a fraction on save', () => {
    const onSave = vi.fn();
    renderCustomSource(onSave);

    fireEvent.change(getThresholdInput('WorkRoom'), { target: { value: '80' } });
    fireEvent.click(screen.getByText(defaultStrings.AdminConfig.labels.saveButton));

    expect(onSave).toHaveBeenCalled();
    const savedAttributes = onSave.mock.calls[0][2] as SqlMembershipAttribute[];
    expect(savedAttributes.find((a) => a.name === 'WorkRoom')?.nullThreshold).toBe(0.8);
    // Untouched attributes keep their existing value.
    expect(savedAttributes.find((a) => a.name === 'AssignmentType')?.nullThreshold).toBe(1);
  });

  test('clearing the field removes the override so the default applies', () => {
    const onSave = vi.fn();
    renderCustomSource(onSave);

    fireEvent.change(getThresholdInput('AssignmentType'), { target: { value: '' } });
    fireEvent.click(screen.getByText(defaultStrings.AdminConfig.labels.saveButton));

    const savedAttributes = onSave.mock.calls[0][2] as SqlMembershipAttribute[];
    expect(savedAttributes.find((a) => a.name === 'AssignmentType')?.nullThreshold).toBeUndefined();
  });

  test('blocks saving and shows an error for an out-of-range value', async () => {
    const onSave = vi.fn();
    renderCustomSource(onSave);

    fireEvent.change(getThresholdInput('WorkRoom'), { target: { value: '150' } });

    // Fluent renders validation messages through DelayedRender, so wait for it to appear.
    expect(await screen.findByText(nullThresholdLabels.nullThresholdValidationError)).toBeInTheDocument();

    fireEvent.click(screen.getByText(defaultStrings.AdminConfig.labels.saveButton));
    expect(onSave).not.toHaveBeenCalled();
  });

  test('blocks saving for a non-numeric value', async () => {
    const onSave = vi.fn();
    renderCustomSource(onSave);

    fireEvent.change(getThresholdInput('WorkRoom'), { target: { value: 'abc' } });

    expect(await screen.findByText(nullThresholdLabels.nullThresholdValidationError)).toBeInTheDocument();

    fireEvent.click(screen.getByText(defaultStrings.AdminConfig.labels.saveButton));
    expect(onSave).not.toHaveBeenCalled();
  });

  test('recovers once an invalid value is corrected', async () => {
    const onSave = vi.fn();
    renderCustomSource(onSave);

    fireEvent.change(getThresholdInput('WorkRoom'), { target: { value: '150' } });
    expect(await screen.findByText(nullThresholdLabels.nullThresholdValidationError)).toBeInTheDocument();

    fireEvent.change(getThresholdInput('WorkRoom'), { target: { value: '75' } });

    await waitFor(() => {
      expect(screen.queryByText(nullThresholdLabels.nullThresholdValidationError)).not.toBeInTheDocument();
    });

    fireEvent.click(screen.getByText(defaultStrings.AdminConfig.labels.saveButton));

    const savedAttributes = onSave.mock.calls[0][2] as SqlMembershipAttribute[];
    expect(savedAttributes.find((a) => a.name === 'WorkRoom')?.nullThreshold).toBe(0.75);
  });

  test('sorts by null threshold when the column header is clicked', () => {
    renderCustomSource();

    // Unsorted order matches input: AssignmentType (100%) before WorkRoom (unset)
    expect(
      screen.getByText('AssignmentType').compareDocumentPosition(screen.getByText('WorkRoom')) &
        Node.DOCUMENT_POSITION_FOLLOWING
    ).toBeTruthy();

    // Ascending sort places attributes without an override first.
    fireEvent.click(screen.getByText(nullThresholdLabels.nullThresholdColumn));

    expect(
      screen.getByText('WorkRoom').compareDocumentPosition(screen.getByText('AssignmentType')) &
        Node.DOCUMENT_POSITION_FOLLOWING
    ).toBeTruthy();
  });

  test('converts values without introducing floating point artifacts', () => {
    const onSave = vi.fn();
    renderCustomSource(onSave);

    fireEvent.change(getThresholdInput('WorkRoom'), { target: { value: '29' } });
    fireEvent.click(screen.getByText(defaultStrings.AdminConfig.labels.saveButton));

    const savedAttributes = onSave.mock.calls[0][2] as SqlMembershipAttribute[];
    expect(savedAttributes.find((a) => a.name === 'WorkRoom')?.nullThreshold).toBe(0.29);
  });

  test('displays an awkward stored fraction as a clean percentage', () => {
    renderCustomSource();

    expect(fractionToPercentText(0.29)).toBe('29');
    expect(fractionToPercentText(0.07)).toBe('7');
    expect(fractionToPercentText(undefined)).toBe('');
  });

  const renderWithGeneralTab = (onSave = vi.fn()) =>
    renderWithProviders(
      <MemoryRouter>
        <AdminConfigView
          isSaving={false}
          onSave={onSave}
          handleGetValues={vi.fn()}
          settings={createSettings()}
          sqlMembershipSource={{ name: 'SqlMembership', customLabel: 'HR Data' }}
          sqlMembershipSourceAttributes={attributes}
          strings={defaultStrings.AdminConfig}
          styles={getStyles}
          isCustomMembershipProviderAdmin={true}
          isOperationsResetAdministrator={false}
          isGeneralSettingsAdministrator={true}
          isAutoApproverAdministrator={false}
          isAISettingsAdministrator={false}
          defaultAIPrompt={''}
        />
      </MemoryRouter>
    );

  test('does not strand the Save button when an invalid row unmounts on tab switch', async () => {
    renderWithGeneralTab();

    // General is the first tab after the redesign, so open Custom Source explicitly.
    fireEvent.click(screen.getByText(defaultStrings.AdminConfig.CustomSourceSettings.labels.customSource));

    const saveButton = screen.getByText(defaultStrings.AdminConfig.labels.saveButton).closest('button')!;

    // A valid edit enables Save.
    fireEvent.change(getThresholdInput('AssignmentType'), { target: { value: '80' } });
    expect(saveButton).toBeEnabled();

    // An invalid edit on another row correctly blocks Save.
    fireEvent.change(getThresholdInput('WorkRoom'), { target: { value: '150' } });
    expect(saveButton).toBeDisabled();

    // Switching tabs unmounts the invalid row, so the block must be released.
    fireEvent.click(screen.getByText(defaultStrings.AdminConfig.GeneralSettings.labels.general));

    await waitFor(() => {
      expect(screen.queryByText(nullThresholdLabels.nullThresholdColumn)).not.toBeInTheDocument();
    });

    expect(saveButton).toBeEnabled();
  });

  test('does not leak typed text between rows when the grid is re-sorted', () => {
    renderCustomSource();

    fireEvent.change(getThresholdInput('WorkRoom'), { target: { value: '42' } });

    // Re-sorting reorders rows; each row must still show its own attribute's value.
    fireEvent.click(screen.getByText(nullThresholdLabels.nullThresholdColumn));

    expect(getThresholdInput('WorkRoom').value).toBe('42');
    expect(getThresholdInput('AssignmentType').value).toBe('100');
  });
});
