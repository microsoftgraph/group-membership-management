// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type Job = {
  partitionKey: string;
  rowKey: string;
  targetGroupId: string;
  targetGroupType: string;
  targetGroupName: string;
  startDate: string;
  lastSuccessfulStartTime: string;
  lastSuccessfulRunTime: string;
  actionRequired: string;
  enabledOrNot: string;
  status: string;
  period: number;
  estimatedNextRunTime: string;
  thresholdPercentageForAdditions: number;
  thresholdPercentageForRemovals: number;
};
