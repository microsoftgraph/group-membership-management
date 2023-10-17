// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';

import { loginRequest, config } from '../authConfig';
import { msalInstance } from '../index';
import { type GetJobDetailsRequest } from '../models/GetJobDetailsRequest';
import { type JobDetails } from '../models/JobDetails';
import { PatchJobRequest } from '../models/PatchJobRequest';
import { PatchJobResponse } from '../models/PatchJobResponse';

const getActiveAccount = async () => {
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

  return authResult;
}

export const fetchJobDetails = createAsyncThunk(
  'jobs/fetchJobDetails',
  async (jobDetailsRequest: GetJobDetailsRequest) => {

    const authResult = await getActiveAccount();
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
        `?syncJobId=${encodeURIComponent(
          jobDetailsRequest.syncJobId
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

export const patchJobDetails = createAsyncThunk(
  'jobs/patchJobDetails',
  async (patchJobRequest: PatchJobRequest) => {

    const authResult = await getActiveAccount();
    const headers = new Headers();
    const bearer = `Bearer ${authResult.accessToken}`;
    headers.append('Authorization', bearer);
    headers.append('Content-Type', 'application/json-patch+json');

    const options = {
      method: 'PATCH',
      headers,
      body: JSON.stringify(patchJobRequest.operations)
    };

    try {
      const response = await fetch(
        `${config.patchJobDetails}/${encodeURIComponent(patchJobRequest.syncJobId)}`,
        options
      ).then(async (response) => {

        if (response.ok) {
          let patchResponse: PatchJobResponse = {
            ok: response.ok,
            statusCode: response.status,
          };
          return patchResponse;
        } else {

          let jsonResponse

          try {
            jsonResponse =await response.json()
          } catch (error) {
            // there is no reponse body
          }

          let patchResponse: PatchJobResponse = {
            ok: response.ok,
            statusCode: response.status,
            errorCode: jsonResponse?.detail,
            responseData: jsonResponse?.responseData
          };

          if(response.status === 403) {
            patchResponse.errorCode = 'NotGroupOwner';
          }

          return patchResponse;
        }
      });

      return response;

    } catch (error) {
      throw new Error('Failed to update job details!');
    }
  }
);
