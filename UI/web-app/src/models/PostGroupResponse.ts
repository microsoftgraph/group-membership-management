// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
export interface PostGroupResponse {
    ok: boolean;
    statusCode: number;
    errorCode?: string;
    responseData?: string;
    groupId?: string;
};