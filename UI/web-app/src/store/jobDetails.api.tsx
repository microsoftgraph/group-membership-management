// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';

import { config } from '../authConfig';
import { PatchJobResponse } from '../models/PatchJobResponse';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';
import { GetJobDetailsRequest, Job, RemoveGMMResponse, SyncJobChange, SyncJobHistory } from '../models';
import { PatchJobRequest } from '../models/PatchJobRequest';
import { processJob } from '../utils/jobUtils';
import { GetJobChangesRequest } from '../models/GetJobChangesRequest';
import { GetChannelRequest } from '../models/GetChannelRequest';
import { SyncJobChangeReason } from '../models/SyncJobChangeReason';
import { SearchSyncHistoryByUserResult } from '../models/SearchSyncHistoryByUserResult';

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
  headers.append('Content-Type', 'application/json');

  const options = {
    method: 'PATCH',
    headers,
    body: JSON.stringify({
      patchOperation: request.patchOperation,
      changeReason: request.changeReason,
      businessJustification: request.businessJustification,
    }),
  };

  let patchJobDetailsApiUrl: string;

  switch (request.changeReason) {
    case SyncJobChangeReason.SubmissionApproved:
    case SyncJobChangeReason.SubmissionRejected:
      patchJobDetailsApiUrl = `${config.patchReviewJob(request.syncJobId)}`;
      break;
    case SyncJobChangeReason.StatusUpdate:
      patchJobDetailsApiUrl = `${config.patchEnableJob(request.syncJobId)}`;
      break;
    case SyncJobChangeReason.Update:
      patchJobDetailsApiUrl = `${config.patchUpdateJob(request.syncJobId)}`;
      break;
    default:
      throw new Error('Invalid change reason');
  }

  try {
    const response = await fetch(
      patchJobDetailsApiUrl,
      options
    ).then(async (response) => {
      if (response.ok) {
        const patchResponse: PatchJobResponse = {
          ok: response.ok,
          statusCode: response.status,
        };
        return patchResponse;
      } else {
        const json: PatchJobResponse = await response.json();
        return {
          ...json,
          ok: response.ok,
          statusCode: response.status,
          responseData: json.responseData,
          errorCode: json.errorCode,
        };
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

export const fetchSyncJobHistory = createAsyncThunk<
  SyncJobHistory[],
  string,
  ThunkConfig
>('jobs/fetchSyncJobHistory', async (syncJobId, { extra }) => {
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
      const response = await fetch(`${config.getSyncJobHistory}/${encodeURIComponent(syncJobId)}`, options);
      if (!response.ok) {
        throw new Error('Failed to fetch sync job history data!');
      }
      return await response.json();
    } catch (error) {
      throw new Error('Failed to fetch sync job history data!');
    }
  }
);

export const searchSyncHistoryByUser = createAsyncThunk<
  SearchSyncHistoryByUserResult,
  { syncJobId: string; userObjectId: string; requestId?: string },
  ThunkConfig
>('jobs/searchSyncHistoryByUser', async ({ syncJobId, userObjectId, requestId }, { extra }) => {
    const { authenticationService } = extra.services;
    const token = await authenticationService.getTokenAsync(TokenType.GMM);
    const headers = new Headers({
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    });

    const url = new URL(config.searchSyncHistoryUser(encodeURIComponent(syncJobId), encodeURIComponent(userObjectId)));
    if (requestId) {
      url.searchParams.set('requestId', requestId);
    }

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers,
    });

    if (!response.ok) {
      throw new Error('Failed to search sync job history by user.');
    }

    return await response.json();
  }
);

export const downloadMembershipChanges = createAsyncThunk<
  void,
  { syncJobId: string; runId: string; targetGroupId: string },
  ThunkConfig
>('jobs/downloadMembershipChanges', async ({ syncJobId, runId, targetGroupId }, { extra }) => {
  const { authenticationService } = extra.services;
  const token = await authenticationService.getTokenAsync(TokenType.GMM);
  const headers = new Headers({
    'Authorization': `Bearer ${token}`,
  });

  const response = await fetch(config.downloadMembershipChanges(syncJobId, runId), {
    method: 'GET',
    headers,
  });

  if (!response.ok) {
    throw new Error('Failed to download membership changes.');
  }

  const blob = await response.blob();
  const url = window.URL.createObjectURL(blob);
  try {
    const link = document.createElement('a');
    link.href = url;
    link.download = `membership_changes_${targetGroupId}_${runId}.zip`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  } finally {
    window.URL.revokeObjectURL(url);
  }
});