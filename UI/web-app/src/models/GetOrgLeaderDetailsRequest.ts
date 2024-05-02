// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface GetOrgLeaderDetailsRequest {
  objectId: string;
  key: number;
  text: string;
  partId: number;
}

export interface GetOrgLeaderDetailsUsingIdRequest {
  employeeId: number;
  partId: number;
}