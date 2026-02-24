// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type Configuration, type PopupRequest } from '@azure/msal-browser';

// Config object to be passed to Msal on creation
export const msalConfig: Configuration = {
  auth: {
    clientId: `${process.env.REACT_APP_AAD_UI_APP_CLIENT_ID}`,
    authority: 'https://login.microsoftonline.com/organizations',
    redirectUri: '/',
    postLogoutRedirectUri: '/',
  },
};

// scopes
export const loginRequest = {
  scopes: [
    `api://${process.env.REACT_APP_AAD_API_APP_CLIENT_ID}/user_impersonation`,
  ],
};

export const graphRequest: PopupRequest = {
  scopes: ['User.Read'],
};

// endpoints
export const config = {
  getTitle: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/OpenAI/generateTitle`,
  generateTitles: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/OpenAI/generateTitles`,
  getJobs: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobs`,
  getJobDetails: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobDetails/job`,
  getJobChanges: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobDetails/history/configuration`,
  getSyncJobHistory: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobDetails/history/sync`,
  downloadMembershipChanges: (syncJobId: string, runId: string) => `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobDetails/history/sync/${syncJobId}/runs/${runId}/download`,
  getOrgLeaderDetails: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/orgLeaderDetails`,
  settings: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/settings`,
  patchSetting: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/settings`,
  patchEnableJob: (jobId: string) => `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobDetails/${jobId}/enable`,
  patchReviewJob: (jobId: string) => `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobDetails/${jobId}/review`,
  patchUpdateJob: (jobId: string) => `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobDetails/${jobId}/update`,
  postJob: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobs`,
  downloadJobs: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobs/bulkDownload`,
  destinations: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/destinations`,
  searchDestinations: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/destinations/searchGroups`,
  createGroup: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/destinations/groups`,
  searchChannels: (teamId: string) => `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/destinations/teams/${teamId}/searchChannels`,
  getGroupEndpoints: (groupId: string) => `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/destinations/groups/${groupId}/endpoints`,
  getGroupOwners: (groupId: string) => `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/destinations/groups/${groupId}/owners`,
  getGroupMembers: (groupId: string) => `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/destinations/groups/${groupId}/group-members`,
  getGroupOnboardingStatus: (groupId: string) => `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/destinations/groups/${groupId}/onboarding-status`,
  getChannelOnboardingStatus: (teamId: string, channelId: string) => `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/destinations/teams/${teamId}/channel/${channelId}/onboarding-status`,
  getGroupDetails: (groupId: string) => `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobDetails/group/${groupId}`,
  getChannelDetails: (teamId: string, channelId: string) => `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobDetails/groups/${teamId}/channels/${channelId}`,
  removeGMM: (syncJobId: string) =>`${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobDetails/${syncJobId}/removeGmm`,
};
