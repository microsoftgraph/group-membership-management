// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface PatchJobRequest {
    syncJobId: string;
    operations: PatchOperation[];
}

export interface PatchOperation {
    op: string;
    path: string;
    value: string;
}
