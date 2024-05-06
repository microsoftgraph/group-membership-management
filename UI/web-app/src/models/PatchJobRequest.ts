// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Job } from "./Job";

export interface PatchJobRequest {
  syncJobId: string;
  patchOperation: PatchOperation[];
}

export interface PatchOperation {
  op: string;
  path: string;
  value: any;
}