// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface JobDetails {
  startDate: string;
  lastSuccessfulStartTime: string;
  source: string;
  requestor: string;
  thresholdViolations: number;
  thresholdPercentageForAdditions: number;
  thresholdPercentageForRemovals: number;
  endpoints: string[];
}
