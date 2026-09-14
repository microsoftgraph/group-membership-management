// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface PendingReviewSummaryRow {
  requestType: string;
  count: number;
  oldestChangeTime: string | null;
}

export interface InProgressStatusRow {
  status: string;
  count: number;
  oldestStartTime: string | null;
}

export interface AnomalousStatusRow {
  status: string;
  count: number;
  maxCyclesMissed: number | null;
  oldestLastSuccessfulRun: string | null;
  neverSucceededCount: number;
}

export interface DashboardSummary {
  pendingReview: PendingReviewSummaryRow[];
  inProgress: InProgressStatusRow[];
  anomalousStatuses: AnomalousStatusRow[];
  queriedAtUtc: string;
}

export interface GetDashboardSummaryResponse {
  statusCode: number;
  errorCode: string | null;
  summary: DashboardSummary;
}
