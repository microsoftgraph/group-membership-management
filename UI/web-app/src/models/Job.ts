// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type Job = {
  partitionKey: string;
  rowKey: string;
  targetGroupId: string;
  targetGroupType: string;
  startDate: string;
  lastSuccessfulStartTime: string;
  lastSuccessfulRunTime: string;
  ago: string;
  left: string;
  actionRequired: string;
  enabledOrNot: string;
  status: string;
  estimatedNextRunTime: string;
  thresholdPercentageForAdditions: number;
  thresholdPercentageForRemovals: number;
  }