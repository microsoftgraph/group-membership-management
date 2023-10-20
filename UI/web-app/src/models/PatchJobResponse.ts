// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
export interface PatchJobResponse {
    ok: boolean;
    statusCode: number;
    errorCode?: string;
    responseData?: string[];
}