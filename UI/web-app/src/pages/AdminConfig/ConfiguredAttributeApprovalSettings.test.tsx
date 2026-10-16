// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { screen, waitFor } from '@testing-library/react';
import { vi, beforeAll } from 'vitest';
import { initializeIcons } from '@fluentui/react';
import { ConfiguredAttributeRules, ConfiguredAttributeToggleCard, useConfiguredAttributeApproval } from './ConfiguredAttributeApprovalSettings';
import { defaultStrings } from '../../services/localization';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { GMMApi } from '../../apis/GMMApi';

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

  it('hides add and delete controls for read-only users', async () => {
    mockApi({
      fetchConfiguration: vi.fn().mockResolvedValue({ enabled: false, schemaVersion: 1, attributes: [] }),
    });
    renderWithProviders(<Harness canEdit={false} />);
    await screen.findByTestId('configured-attribute-approval');
    expect(screen.queryByText(labels.configuredAddAttribute)).toBeNull();
  });
});
