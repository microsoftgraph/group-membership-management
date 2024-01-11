// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type JobEntity = {
  syncJobId: string;
  targetGroupId: string;
  targetGroupType: string;
  targetGroupName: string;
  startDate: string;
  lastSuccessfulStartTime: string;
  lastSuccessfulRunTime: string;
  actionRequired: string;
  enabledOrNot: boolean;
  status: string;
  period: number;
  arrow: string;
  estimatedNextRunTime: string;
  thresholdPercentageForAdditions: number;
  thresholdPercentageForRemovals: number;
  endpoints: string[];
};