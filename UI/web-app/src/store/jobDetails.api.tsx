// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';

import { config } from '../authConfig';
import { PatchJobResponse } from '../models/PatchJobResponse';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';
import { GetJobDetailsRequest, Job, RemoveGMMResponse, SyncJobChange } from '../models';
import { PatchJobRequest } from '../models/PatchJobRequest';
import { processJob } from '../utils/jobUtils';
import { GetJobChangesRequest } from '../models/GetJobChangesRequest';
import { GetChannelRequest } from '../models/GetChannelRequest';

export const fetchJobDetails = createAsyncThunk<
  Job,
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
        `/${encodeURIComponent(jobDetailsRequest.syncJobId)}`,
      options
    );
    if (!response.ok) {
      throw new Error('Failed to fetch job details data!');
    }

    const job: Job = await response.json();
    return processJob(job);
  } catch (error) {
    throw new Error('Failed to fetch job details data!');
  }
});

export const getGroupDetails = createAsyncThunk<Job, string, ThunkConfig>(
  'groupDetails',
  async (groupId: string, { extra }) => {
    const { authenticationService } = extra.services;
    const token = await authenticationService.getTokenAsync(TokenType.GMM);
    const headers = new Headers();
    const bearer = `Bearer ${token}`;
    headers.append('Authorization', bearer);

    const options = {
      method: 'GET',
      headers,
    };

    try {
      const response = await fetch(`${config.getGroupDetails(groupId)}`, options).then(
        async (response) => await response.json()
      );
      const job: Job = response;
      return processJob(job);
    } catch (error) {
      throw new Error('Failed to fetch job details data!');
    }
  }
);

export const getChannelDetails = createAsyncThunk<Job, GetChannelRequest, ThunkConfig>(
  'channelDetails',
  async (request, { extra }) => {
    const { authenticationService } = extra.services;
    const token = await authenticationService.getTokenAsync(TokenType.GMM);
    const headers = new Headers();
    const bearer = `Bearer ${token}`;
    headers.append('Authorization', bearer);

    const options = {
      method: 'GET',
      headers,
    };

    try {
      const response = await fetch(`${config.getChannelDetails(request.groupId, request.channelId)}`, options).then(
        async (response) => await response.json()
      );
      const job: Job = response;
      return processJob(job);
    } catch (error) {
      throw new Error('Failed to fetch job details data!');
    }
  }
);

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
  headers.append('X-Change-Reason', request.changeReason);
  headers.append('X-Business-Justification', request.businessJustification);

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
          patchResponse.errorCode = 'Forbidden';
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
        removeGMMResponse.errorCode = 'Forbidden';
      } else if (errorResponse.status === 500) {
        removeGMMResponse.errorCode = 'InternalError';
      }

      return removeGMMResponse;
    }
  } catch (error) {
    throw new Error(`Failed to remove GMM: ${error}`);
  }
});

export const fetchJobChanges = createAsyncThunk<
  SyncJobChange[],
  GetJobChangesRequest,
  ThunkConfig
>('jobs/fetchJobChanges', async (request, { extra }) => {
    const { authenticationService } = extra.services;
    const token = await authenticationService.getTokenAsync(TokenType.GMM);
    const headers = new Headers({
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    });

    const options = {
      method: 'GET',
      headers
    };

    try {
      const response = await fetch(`${config.getJobChanges}/${encodeURIComponent(request.syncJobId)}`, options)
        .then(async (response) => await response.json());

      return response.items;
    } catch (error) {
      throw new Error('Failed to fetch job changes data!');
    }
  }
);