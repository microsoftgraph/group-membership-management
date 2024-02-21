// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { config } from '../authConfig';
import { type GetOrgLeaderDetailsRequest } from '../models/GetOrgLeaderDetailsRequest';
import { type GetOrgLeaderDetailsResponse } from '../models/GetOrgLeaderDetailsResponse';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';

export const fetchOrgLeaderDetails = createAsyncThunk<
  GetOrgLeaderDetailsResponse,
  GetOrgLeaderDetailsRequest,
  ThunkConfig
>('orgLeaderDetails', async (orgLeaderDetailsDetailsRequest, { extra }) => {
  const { authenticationService } = extra.services;
  const token = await authenticationService.getTokenAsync(TokenType.GMM);
  const headers = new Headers();
  headers.append('Authorization', `Bearer ${token}`);

  const options = {
    method: 'GET',
    headers,
  };

  try {
    const response = await fetch(
      config.getOrgLeaderDetails +
        `/${encodeURIComponent(orgLeaderDetailsDetailsRequest.objectId)}`,
      options
    ).then(async (response) => await response.json());

    const payload: GetOrgLeaderDetailsResponse = {
      employeeId: response["employeeId"],
      objectId: orgLeaderDetailsDetailsRequest.objectId,
      text: orgLeaderDetailsDetailsRequest.text,        
      maxDepth: response["maxDepth"],
      partId: orgLeaderDetailsDetailsRequest.partId
    };
    return payload;
  } catch (error) {
    throw new Error('Failed to fetch orgLeaderDetails details data!');
  }
});