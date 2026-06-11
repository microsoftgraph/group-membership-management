// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface GetOrgLeaderDetailsRequest {
  objectId: string;
  key: number;
  text: string;
  partId: string;
}

export interface GetOrgLeaderDetailsUsingIdRequest {
  employeeId: number;
  partId: string;
};