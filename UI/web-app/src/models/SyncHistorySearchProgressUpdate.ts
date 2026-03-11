// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface SyncHistorySearchProgressUpdate {
  requestId: string;
  syncJobId: string;
  userObjectId: string;
  processedRuns: number;
  totalRuns: number;
  matchingRuns: number;
  completed: boolean;
}