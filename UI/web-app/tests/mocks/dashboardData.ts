// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { ApprovalActivity } from '../../src/models/ApprovalActivity';
import type { DashboardSummary } from '../../src/models/DashboardSummary';
import type { OnBehalfOfActivity } from '../../src/models/OnBehalfOfActivity';

const queriedAtUtc = '2026-10-23T12:00:00Z';
const reviewerKey = 'mock-reviewer';

export const mockDashboardSummary: DashboardSummary = {
  queriedAtUtc,
  pendingReview: [
    { requestType: 'Onboarding', count: 12, oldestChangeTime: '2026-10-09T11:26:00Z' },
    { requestType: 'Update', count: 6, oldestChangeTime: '2026-10-21T11:26:00Z' },
  ],
  inProgress: [{ status: 'InProgress', count: 34, oldestStartTime: '2026-10-23T11:26:00Z' }],
  anomalousStatuses: [
    {
      status: 'SchemaError',
      count: 3,
      maxCyclesMissed: 4,
      oldestLastSuccessfulRun: '2026-10-22T11:26:00Z',
      neverSucceededCount: 1,
    },
    {
      status: 'Error',
      count: 4,
      maxCyclesMissed: 2,
      oldestLastSuccessfulRun: '2026-10-22T17:26:00Z',
      neverSucceededCount: 0,
    },
  ],
};

const windowDates = (windowDays: number): Date[] => {
  const end = new Date(queriedAtUtc);
  end.setUTCHours(0, 0, 0, 0);
  return Array.from({ length: windowDays }, (_, index) => new Date(end.getTime() - (windowDays - index) * 86400000));
};

export const createMockApprovalActivity = (windowDays = 7): ApprovalActivity => {
  const days = windowDates(windowDays).map((date) => {
    const index = date.getUTCDay();
    return {
      date: date.toISOString(),
      automated: [0, 5, 1, 0, 0, 0, 1][index],
      reviewers: [
        {
          reviewerKey,
          approved: [0, 0, 0, 2, 1, 3, 2][index],
          rejected: index === 5 ? 1 : 0,
        },
      ],
    };
  });
  const automatedTotal = days.reduce((sum, day) => sum + day.automated, 0);
  const manualTotal = days.reduce((sum, day) => sum + day.reviewers[0].approved, 0);
  const rejectedTotal = days.reduce((sum, day) => sum + day.reviewers[0].rejected, 0);

  return {
    windowDays,
    sinceUtc: days[0].date,
    queriedAtUtc,
    automatedTotal,
    manualTotal,
    rejectedTotal,
    reviewers: [
      { reviewerKey, displayName: 'Contoso Reviewer', approved: manualTotal, rejected: rejectedTotal, isOther: false },
    ],
    days,
  };
};

export const createMockOnBehalfOfActivity = (windowDays = 7): OnBehalfOfActivity => {
  const days = windowDates(windowDays).map((date) => ({
    date: date.toISOString(),
    submitters: [{ submitterKey: reviewerKey, onboarding: date.getUTCDay() === 5 ? 1 : 0, update: 0 }],
  }));
  const onboardingTotal = days.reduce((sum, day) => sum + day.submitters[0].onboarding, 0);

  return {
    windowDays,
    sinceUtc: days[0].date,
    queriedAtUtc,
    onboardingTotal,
    updateTotal: 0,
    grandTotal: onboardingTotal,
    submitters: [
      {
        submitterKey: reviewerKey,
        displayName: 'Contoso Reviewer',
        onboarding: onboardingTotal,
        update: 0,
        isOther: false,
      },
    ],
    days,
  };
};
