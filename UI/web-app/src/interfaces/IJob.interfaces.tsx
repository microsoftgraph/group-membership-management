// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface IJob {
  syncJobId: string;
  targetGroupId: string;
  targetGroupType: string;
  startDate: string;
  status: string;
  lastSuccessfulStartTime: string;
  lastSuccessfulRunTime: string;
  estimatedNextRunTime: string;
  thresholdPercentageForAdditions: number;
  thresholdPercentageForRemovals: number;
}
