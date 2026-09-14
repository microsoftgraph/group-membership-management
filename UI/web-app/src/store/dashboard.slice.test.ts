// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import dashboardReducer, {
  resetDashboardError,
  selectDashboardError,
  selectDashboardLoading,
  selectDashboardSummary,
} from './dashboard.slice';
import { fetchDashboardSummary } from './dashboard.api';
import { DashboardSummary } from '../models';
import type { RootState } from './store';

const initial = dashboardReducer(undefined, { type: '@@INIT' });

const summary: DashboardSummary = {
  pendingReview: [{ requestType: 'Onboarding', count: 3, oldestChangeTime: '2026-01-01T00:00:00Z' }],
  inProgress: [{ status: 'InProgress', count: 12, oldestStartTime: '2026-01-02T00:00:00Z' }],
  anomalousStatuses: [
    {
      status: 'Error',
      count: 5,
      maxCyclesMissed: 4,
      oldestLastSuccessfulRun: '2026-01-03T00:00:00Z',
      neverSucceededCount: 1,
    },
  ],
  queriedAtUtc: '2026-01-04T00:00:00Z',
};

describe('dashboard.slice — reducers', () => {
  it('starts empty and not loading', () => {
    expect(initial.summary).toBeUndefined();
    expect(initial.loading).toBe(false);
    expect(initial.error).toBeUndefined();
  });

  it('resetDashboardError clears the error', () => {
    const seeded = { ...initial, error: 'boom' };
    expect(dashboardReducer(seeded, resetDashboardError()).error).toBeUndefined();
  });
});

describe('dashboard.slice — fetchDashboardSummary extraReducers', () => {
  it('pending sets loading and clears any previous error', () => {
    const seeded = { ...initial, error: 'boom' };
    const state = dashboardReducer(seeded, fetchDashboardSummary.pending('req1', undefined));
    expect(state.loading).toBe(true);
    expect(state.error).toBeUndefined();
  });

  it('fulfilled stores the summary and stops loading', () => {
    const state = dashboardReducer(initial, fetchDashboardSummary.fulfilled(summary, 'req1', undefined));
    expect(state.loading).toBe(false);
    expect(state.summary).toEqual(summary);
  });

  it('rejected records the error and stops loading', () => {
    const action = {
      type: fetchDashboardSummary.rejected.type,
      error: { message: 'network down' },
      meta: { requestId: 'req1', arg: undefined, requestStatus: 'rejected' },
    };
    const state = dashboardReducer({ ...initial, loading: true }, action);
    expect(state.loading).toBe(false);
    expect(state.error).toBe('network down');
  });

  it('rejected falls back to a generic message when none is supplied', () => {
    const action = {
      type: fetchDashboardSummary.rejected.type,
      error: {},
      meta: { requestId: 'req1', arg: undefined, requestStatus: 'rejected' },
    };
    expect(dashboardReducer(initial, action).error).toBe('Failed to fetch the dashboard summary.');
  });

  it('a failed refresh keeps the previously loaded summary visible', () => {
    const loaded = dashboardReducer(initial, fetchDashboardSummary.fulfilled(summary, 'req1', undefined));
    const action = {
      type: fetchDashboardSummary.rejected.type,
      error: { message: 'network down' },
      meta: { requestId: 'req2', arg: undefined, requestStatus: 'rejected' },
    };
    expect(dashboardReducer(loaded, action).summary).toEqual(summary);
  });
});

describe('dashboard.slice — selectors', () => {
  it('read summary, loading and error off the root state', () => {
    const root = { dashboard: { summary, loading: true, error: 'boom' } } as unknown as RootState;
    expect(selectDashboardSummary(root)).toEqual(summary);
    expect(selectDashboardLoading(root)).toBe(true);
    expect(selectDashboardError(root)).toBe('boom');
  });
});
