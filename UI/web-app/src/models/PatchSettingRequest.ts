// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface PatchSettingRequest {
    settingKey: string;
    operations: PatchOperation[];
}

export interface PatchOperation {
    op: string;
    path: string;
    value: string;
}
