// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface SyncJobHistory {
  runId: string;
  startTime: string | null;
  endTime: string | null;
  duration: number | null;
  status: string;
  usersAdded: number | null;
  usersRemoved: number | null;
  thresholdViolations: number | null;
  updatedByFunction: string;
  createdAt: string;
  updatedAt: string;
}
