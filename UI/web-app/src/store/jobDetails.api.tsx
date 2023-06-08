// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';

import { loginRequest, config } from '../authConfig';
import { msalInstance } from '../index';
import { type GetJobDetailsRequest } from '../models/GetJobDetailsRequest';
import { type JobDetails } from '../models/JobDetails';

export const fetchJobDetails = createAsyncThunk(
  'jobs/fetchJobDetails',
  async (jobDetailsRequest: GetJobDetailsRequest) => {
    const account = msalInstance.getActiveAccount();
    if (account == null) {
      throw Error(
        'No active account! Verify a user has been signed in and setActiveAccount has been called.'
      );
    }

    const authResult = await msalInstance.acquireTokenSilent({
      ...loginRequest,
      account,
    });

    const headers = new Headers();
    const bearer = `Bearer ${authResult.accessToken}`;
    headers.append('Authorization', bearer);

    const options = {
      method: 'GET',
      headers,
    };

    try {
      const response = await fetch(
        config.getJobDetails +
          `?rowKey=${encodeURIComponent(
            jobDetailsRequest.rowKey
          )}&partitionKey=${encodeURIComponent(
            jobDetailsRequest.partitionKey
          )}`,
        options
      ).then(async (response) => await response.json());

      const payload: JobDetails = response;
      return payload;
    } catch (error) {
      throw new Error('Failed to fetch job details data!');
    }
  }
);
