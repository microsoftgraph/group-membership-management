// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { screen, waitFor } from '@testing-library/react';
import { vi, beforeAll } from 'vitest';
import { getTheme, initializeIcons } from '@fluentui/react';
import { ConfiguredAttributeRules, ConfiguredAttributeToggleCard, useConfiguredAttributeApproval } from './ConfiguredAttributeApprovalSettings';
import { defaultStrings } from '../../services/localization';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { GMMApi } from '../../apis/GMMApi';
import { SettingKey, SettingKeyMap } from '../../models/SettingKey';

const labels = defaultStrings.AdminConfig.AutoApproverSettings.labels;

beforeAll(() => initializeIcons());
afterEach(() => vi.restoreAllMocks());

const mockApi = (overrides: Record<string, unknown>) => {
  const api = {
    fetchConfiguration: vi.fn(),
    saveConfiguration: vi.fn(),
    ...overrides,
  };
  const sqlApi = {
    fetchDefaultSqlMembershipSourceAttributes: vi.fn().mockResolvedValue([
      { name: 'Company', customLabel: 'Company', type: 'varchar', enabled: true, hasMapping: true, values: [], description: '' },
    ]),
    resolveDefaultSqlMembershipSourceAttributeMappings: vi.fn().mockResolvedValue([{ code: '100', description: 'Contoso' }]),
    fetchDefaultSqlMembershipSourceAttributeMappings: vi.fn().mockResolvedValue({ mappings: [], hasMore: false }),
  };
  vi.spyOn(GMMApi.prototype, 'configuredAttributeApproval', 'get').mockReturnValue(api as never);
  vi.spyOn(GMMApi.prototype, 'sqlMembershipSources', 'get').mockReturnValue(sqlApi as never);
  return api;
};

const Harness: React.FunctionComponent<{ canEdit: boolean }> = ({ canEdit }) => {
  const state = useConfiguredAttributeApproval(labels, canEdit);
  return (
    <>
      <ConfiguredAttributeToggleCard labels={labels} canEdit={canEdit} state={state} />
      <ConfiguredAttributeRules labels={labels} canEdit={canEdit} state={state} />
    </>
  );
};

describe('ConfiguredAttributeApprovalSettings', () => {
  it('uses the WebAPI setting key ordinals and names', () => {
    expect(SettingKey.IsAutoApprovalForConfiguredAttributeEnabled).toBe(24);
    expect(SettingKey.ConfiguredAttributeAutoApprovalConfiguration).toBe(25);
    expect(SettingKeyMap[SettingKey.IsAutoApprovalForConfiguredAttributeEnabled]).toBe('IsAutoApprovalForConfiguredAttributeEnabled');
    expect(SettingKeyMap[SettingKey.ConfiguredAttributeAutoApprovalConfiguration]).toBe('ConfiguredAttributeAutoApprovalConfiguration');
  });

  it('shows a retry option when loading fails', async () => {
    mockApi({ fetchConfiguration: vi.fn().mockRejectedValue(new Error('boom')) });
    renderWithProviders(<Harness canEdit={true} />);
    expect(await screen.findByText(labels.configuredLoadError)).toBeTruthy();
    expect(screen.getByText(labels.configuredRetry)).toBeTruthy();
  });

  it('renders saved rows with resolved labels and a trailing empty row', async () => {
    mockApi({
      fetchConfiguration: vi.fn().mockResolvedValue({
        enabled: true,
        schemaVersion: 1,
        attributes: [{ attribute: 'Company', values: ['100'] }],
      }),
    });
    renderWithProviders(<Harness canEdit={true} />);
    await waitFor(() => expect(screen.getAllByTestId('configured-attribute-row').length).toBeGreaterThan(0));
    expect(await screen.findByText('Contoso')).toBeTruthy();
  });

  it('matches the settings card and label styles without a redundant Attributes heading', async () => {
    mockApi({
      fetchConfiguration: vi.fn().mockResolvedValue({
        enabled: true,
        schemaVersion: 1,
        maxDepth: 1,
        attributes: [{ attribute: 'Company', values: ['100'] }],
      }),
    });
    renderWithProviders(<Harness canEdit={true} />);

    const card = await screen.findByTestId('configured-attribute-approval');
    expect(card).toHaveStyle({
      outline: `1px solid ${getTheme().palette.neutralQuaternary}`,
      borderRadius: '10px',
      padding: '24px',
      boxSizing: 'border-box',
    });
    expect(screen.queryByText('Attributes', { exact: true })).not.toBeInTheDocument();
    for (const label of [labels.configuredDepthLabel, labels.configuredAttribute, labels.configuredValues]) {
      expect(screen.getAllByText(label, { exact: true }).some((el) => el.classList.contains('ms-Label'))).toBe(true);
    }
    expect(screen.getByRole('combobox', { name: labels.configuredDepthLabel })).toBeInTheDocument();
    expect(await screen.findByText('1 level down')).toBeInTheDocument();
    expect(screen.getAllByRole('combobox', { name: labels.configuredAttribute })).toHaveLength(1);
    expect(screen.getAllByPlaceholderText(labels.configuredValuesPlaceholder)).toHaveLength(1);
  });

  it('flags a row that has an attribute but no values, and ignores a fully empty row', async () => {
    mockApi({
      fetchConfiguration: vi.fn().mockResolvedValue({
        enabled: true,
        schemaVersion: 1,
        attributes: [{ attribute: 'Company', values: ['100'] }],
      }),
    });
    const Probe: React.FunctionComponent = () => {
      const state = useConfiguredAttributeApproval(labels, true);
      return (
        <>
          <span data-testid="has-errors">{String(state.hasErrors)}</span>
          <button onClick={() => state.setRows((r) => [...r, { attribute: '', tags: [] }])}>empty</button>
          <button onClick={() => state.setRows((r) => [...r, { attribute: 'Other', tags: [] }])}>half</button>
        </>
      );
    };
    const { default: userEvent } = await import('@testing-library/user-event');
    renderWithProviders(<Probe />);
    await waitFor(() => expect(screen.getByTestId('has-errors').textContent).toBe('false'));
    await userEvent.click(screen.getByText('empty'));
    expect(screen.getByTestId('has-errors').textContent).toBe('false');
    await userEvent.click(screen.getByText('half'));
    expect(screen.getByTestId('has-errors').textContent).toBe('true');
  });

  it('defaults depth to 2 for a legacy configuration and marks a change as dirty', async () => {
    mockApi({
      fetchConfiguration: vi.fn().mockResolvedValue({ enabled: true, schemaVersion: 1, attributes: [{ attribute: 'Company', values: ['100'] }] }),
    });
    const Probe: React.FunctionComponent = () => {
      const state = useConfiguredAttributeApproval(labels, true);
      return (
        <>
          <span data-testid="depth">{state.maxDepth}</span>
          <span data-testid="dirty">{String(state.isDirty)}</span>
          <span data-testid="loading">{String(state.loading)}</span>
          <button onClick={() => state.setMaxDepth(4)}>deeper</button>
        </>
      );
    };
    const { default: userEvent } = await import('@testing-library/user-event');
    renderWithProviders(<Probe />);
    await waitFor(() => expect(screen.getByTestId('loading').textContent).toBe('false'));
    expect(screen.getByTestId('depth').textContent).toBe('2');
    expect(screen.getByTestId('dirty').textContent).toBe('false');
    await userEvent.click(screen.getByText('deeper'));
    expect(screen.getByTestId('depth').textContent).toBe('4');
    expect(screen.getByTestId('dirty').textContent).toBe('true');
  });

  it('hides add and delete controls for read-only users', async () => {
    mockApi({
      fetchConfiguration: vi.fn().mockResolvedValue({ enabled: false, schemaVersion: 1, maxDepth: 2, attributes: [] }),
    });
    renderWithProviders(<Harness canEdit={false} />);
    await screen.findByTestId('configured-attribute-approval');
    expect(screen.queryByText(labels.configuredAddAttribute)).toBeNull();
  });
});
