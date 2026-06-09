// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Title } from './Title';

export type Job = {
  syncJobId: string;
  targetGroupId: string;
  targetChannelId: string;
  targetDestinationType: string;
  targetGroupName: string;
  targetChannelName: string;
  email: string;
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
  estimatedPurgeDate?: string;
  lastModifiedTime?: string;
  thresholdPercentageForAdditions: number;
  thresholdPercentageForRemovals: number;
  endpoints: string[];
  vivaEngageUrl?: string;
  requestor: string;
  lastModifiedByDisplayName?: string;
  lastModifiedByObjectId?: string;
  lastModifiedOnBehalfOfDisplayName?: string;
  lastModifiedOnBehalfOfObjectId?: string;
  hiddenMembershipSourceIds?: string[];
  hasHiddenMembershipSources?: boolean;
};
