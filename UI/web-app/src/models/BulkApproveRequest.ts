// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface BulkApproveRequest {
  jobIdsToApprove: string[];
  totalNumberOfJobs: number;
}