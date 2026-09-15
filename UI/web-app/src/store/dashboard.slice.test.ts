// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import dashboardReducer, {
  resetDashboardError,
  setApprovalWindowDays,
  selectDashboardError,
  selectDashboardLoading,
  selectDashboardSummary,
  selectApprovalActivity,
  selectApprovalActivityError,
  selectApprovalActivityLoading,
  selectApprovalWindowDays,
} from './dashboard.slice';
import { fetchApprovalActivity, fetchDashboardSummary } from './dashboard.api';
import { ApprovalActivity, DashboardSummary } from '../models';
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

const activity: ApprovalActivity = {
  windowDays: 30,
  sinceUtc: '2026-01-01T00:00:00Z',
  queriedAtUtc: '2026-01-31T00:00:00Z',
  automatedTotal: 17,
  manualTotal: 482,
  rejectedTotal: 83,
  reviewers: [
    { reviewerKey: 'r1', displayName: 'Ada Lovelace', approved: 400, rejected: 60, isOther: false },
    { reviewerKey: '__other__', displayName: 'Other', approved: 82, rejected: 23, isOther: true },
  ],
  days: [
    {
      date: '2026-01-30T00:00:00Z',
      automated: 2,
      reviewers: [{ reviewerKey: 'r1', approved: 10, rejected: 1 }],
    },
  ],
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

  it('read approval activity, loading, error and window off the root state', () => {
    const root = {
      dashboard: {
        approvalActivity: activity,
        approvalActivityLoading: true,
        approvalActivityError: 'boom',
        approvalWindowDays: 90,
      },
    } as unknown as RootState;
    expect(selectApprovalActivity(root)).toEqual(activity);
    expect(selectApprovalActivityLoading(root)).toBe(true);
    expect(selectApprovalActivityError(root)).toBe('boom');
    expect(selectApprovalWindowDays(root)).toBe(90);
  });
});

describe('dashboard.slice — approval activity', () => {
  it('defaults to a seven day window with no activity loaded', () => {
    expect(initial.approvalWindowDays).toBe(7);
    expect(initial.approvalActivity).toBeUndefined();
    expect(initial.approvalActivityLoading).toBe(false);
    expect(initial.approvalActivityError).toBeUndefined();
  });

  it('setApprovalWindowDays updates the selected window', () => {
    expect(dashboardReducer(initial, setApprovalWindowDays(90)).approvalWindowDays).toBe(90);
  });

  it('pending sets loading and clears any previous error', () => {
    const seeded = { ...initial, approvalActivityError: 'boom' };
    const state = dashboardReducer(seeded, fetchApprovalActivity.pending('req1', 7));
    expect(state.approvalActivityLoading).toBe(true);
    expect(state.approvalActivityError).toBeUndefined();
  });

  it('fulfilled stores the activity and echoes the window the API served', () => {
    const state = dashboardReducer(initial, fetchApprovalActivity.fulfilled(activity, 'req1', 7));
    expect(state.approvalActivityLoading).toBe(false);
    expect(state.approvalActivity).toEqual(activity);
    expect(state.approvalWindowDays).toBe(30);
  });

  it('fulfilled with no payload does not throw and keeps the requested window', () => {
    const seeded = { ...initial, approvalWindowDays: 30 };
    const state = dashboardReducer(
      seeded,
      fetchApprovalActivity.fulfilled(undefined as unknown as ApprovalActivity, 'req1', 30)
    );
    expect(state.approvalActivityLoading).toBe(false);
    expect(state.approvalActivity).toBeUndefined();
    expect(state.approvalWindowDays).toBe(30);
  });

  it('rejected records the error and stops loading', () => {
    const action = {
      type: fetchApprovalActivity.rejected.type,
      error: { message: 'network down' },
      meta: { requestId: 'req1', arg: 7, requestStatus: 'rejected' },
    };
    const state = dashboardReducer({ ...initial, approvalActivityLoading: true }, action);
    expect(state.approvalActivityLoading).toBe(false);
    expect(state.approvalActivityError).toBe('network down');
  });

  it('rejected falls back to a generic message when none is supplied', () => {
    const action = {
      type: fetchApprovalActivity.rejected.type,
      error: {},
      meta: { requestId: 'req1', arg: 7, requestStatus: 'rejected' },
    };
    expect(dashboardReducer(initial, action).approvalActivityError).toBe('Failed to fetch the approval activity.');
  });

  it('a failed refresh keeps the previously loaded activity visible', () => {
    const loaded = dashboardReducer(initial, fetchApprovalActivity.fulfilled(activity, 'req1', 30));
    const action = {
      type: fetchApprovalActivity.rejected.type,
      error: { message: 'network down' },
      meta: { requestId: 'req2', arg: 30, requestStatus: 'rejected' },
    };
    expect(dashboardReducer(loaded, action).approvalActivity).toEqual(activity);
  });
});
