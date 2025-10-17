// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Title } from '../../models/Title';

export type JobEntity = {
  syncJobId: string;
  targetGroupId: string;
  targetChannelId: string;
  targetDestinationType: string;
  targetGroupName: string;
  targetChannelName: string;
  targetGroupEmail: string;
  startDate: string;
  lastSuccessfulStartTime: string;
  lastSuccessfulRunTime: string;
  query: string;
  titles: Title[];
  actionRequired: string;
  enabledOrNot: boolean;
  status: string;
  period: number;
  arrow: string;
  estimatedNextRunTime: string;
  lastModifiedTime?: string;
  thresholdPercentageForAdditions: number;
  thresholdPercentageForRemovals: number;
  endpoints: string[];
  requestor: string;
};