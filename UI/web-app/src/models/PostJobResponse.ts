// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
export interface PostJobResponse {
    ok: boolean;
    statusCode: number;
    errorCode?: string;
    newSyncJobId?: string;
}