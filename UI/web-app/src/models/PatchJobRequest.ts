// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface PatchJobRequest {
  syncJobId: string;
  patchOperation: PatchOperation[];
  changeReason: string;
}

export interface PatchOperation {
  op: string;
  path: string;
  value: any;
}