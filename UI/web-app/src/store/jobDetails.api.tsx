// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';

import { config } from '../authConfig';
import { type GetJobDetailsRequest } from '../models/GetJobDetailsRequest';
import { type JobDetails } from '../models/JobDetails';
import { PatchJobResponse } from '../models/PatchJobResponse';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';
import { Job } from '../models/Job';
import { RemoveGMMResponse } from '../models';
import { PatchJobRequest } from '../models/PatchJobRequest';

export const fetchJobDetails = createAsyncThunk<
  JobDetails,
  GetJobDetailsRequest,
  ThunkConfig
>('jobs/fetchJobDetails', async (jobDetailsRequest, { extra }) => {
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
      config.getJobDetails +
        `?syncJobId=${encodeURIComponent(jobDetailsRequest.syncJobId)}`,
      options
    ).then(async (response) => await response.json());

    const payload: JobDetails = response;
    return payload;
  } catch (error) {
    throw new Error('Failed to fetch job details data!');
  }
});

export const patchJobDetails = createAsyncThunk<
  PatchJobResponse,
  PatchJobRequest,
  ThunkConfig
>('jobs/patchJobDetails', async (request, { extra }) => {
  const { authenticationService } = extra.services;
  const token = await authenticationService.getTokenAsync(TokenType.GMM);
  const headers = new Headers();
  headers.append('Authorization', `Bearer ${token}`);
  headers.append('Content-Type', 'application/json-patch+json');


  const options = {
    method: 'PATCH',
    headers,
    body: JSON.stringify(request.patchOperation),
  };

  try {
    const response = await fetch(
      `${config.patchJobDetails}/${encodeURIComponent(
        request.syncJobId
      )}`,
      options
    ).then(async (response) => {
      if (response.ok) {
        let patchResponse: PatchJobResponse = {
          ok: response.ok,
          statusCode: response.status,
        };
        return patchResponse;
      } else {
        let jsonResponse;

        try {
          jsonResponse = await response.json();
        } catch (error) {
          // there is no reponse body
        }

        let patchResponse: PatchJobResponse = {
          ok: response.ok,
          statusCode: response.status,
          errorCode: jsonResponse?.detail,
          responseData: jsonResponse?.responseData,
        };

        if (response.status === 403) {
          patchResponse.errorCode = 'NotGroupOwner';
        } else if (response.status === 500) {
          patchResponse.errorCode = 'InternalError';
        }

        return patchResponse;
      }
    });

    return response;
  } catch (error) {
    throw new Error('InternalError');
  }
});

export const removeGMM = createAsyncThunk<
  RemoveGMMResponse,
  { syncJobId: string },
  ThunkConfig
>('jobs/removeGMM', async ({ syncJobId }, { extra }) => {
  const { authenticationService } = extra.services;
  const token = await authenticationService.getTokenAsync(TokenType.GMM);
  const headers = new Headers({
    'Authorization': `Bearer ${token}`,
    'Content-Type': 'application/json'
  });


  const options = {
    method: 'POST',
    headers
  };

  try {
    const response = await fetch(`${config.removeGMM(syncJobId)}`, options);
    if (response.ok) {
      return {
        ok: response.ok,
        statusCode: response.status
      };
    } else {
      const errorResponse = await response.json();
      let removeGMMResponse: RemoveGMMResponse = {
        ok: errorResponse.ok,
        statusCode: errorResponse.status,
        errorCode: errorResponse?.detail,
        responseData: errorResponse?.responseData,
      };

      if (errorResponse.status === 403) {
        removeGMMResponse.errorCode = 'NotGroupOwner';
      } else if (errorResponse.status === 500) {
        removeGMMResponse.errorCode = 'InternalError';
      }

      return removeGMMResponse;
    }
  } catch (error) {
    throw new Error(`Failed to remove GMM: ${error}`);
  }
});

