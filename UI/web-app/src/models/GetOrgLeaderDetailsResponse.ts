// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface GetOrgLeaderDetailsResponse {
    maxDepth: number;
    employeeId: number;
    objectId: string;
    partId: string;
    text: string;
}

export type OrgLeaderPickerPersona = {
    key: number;
    text: string;
    id: string;
};