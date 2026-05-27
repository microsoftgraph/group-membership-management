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

const appServiceBaseUri = process.env.REACT_APP_PLAYWRIGHT_MOCK_MODE === 'true'
  ? ''
  : (process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI || '');

// endpoints
export const config = {
  copilotChat: `${appServiceBaseUri}/api/v1/Copilot/chat`,
  getTitle: `${appServiceBaseUri}/api/v1/OpenAI/generateTitle`,
  generateTitles: `${appServiceBaseUri}/api/v1/OpenAI/generateTitles`,
  getJobs: `${appServiceBaseUri}/api/v1/jobs`,
  getJobDetails: `${appServiceBaseUri}/api/v1/jobDetails/job`,
  getJobChanges: `${appServiceBaseUri}/api/v1/jobDetails/history/configuration`,
  getSyncJobHistory: `${appServiceBaseUri}/api/v1/jobDetails/history/sync`,
  getThresholdNotification: (syncJobId: string) => `${appServiceBaseUri}/api/v1/jobDetails/history/sync/${encodeURIComponent(syncJobId)}/threshold-notification`,
  searchSyncHistoryUser: (syncJobId: string, userObjectId: string) => `${appServiceBaseUri}/api/v1/jobDetails/history/sync/${syncJobId}/search-user/${userObjectId}`,
  downloadMembershipChanges: (syncJobId: string, runId: string) => `${appServiceBaseUri}/api/v1/jobDetails/history/sync/${syncJobId}/runs/${runId}/download`,
  getSyncExplanation: (syncJobId: string, runId: string, userObjectId: string) => `${appServiceBaseUri}/api/v1/jobDetails/history/sync/${syncJobId}/runs/${runId}/explain-user/${userObjectId}`,
  getOrgLeaderDetails: `${appServiceBaseUri}/api/v1/orgLeaderDetails`,
  settings: `${appServiceBaseUri}/api/v1/settings`,
  patchSetting: `${appServiceBaseUri}/api/v1/settings`,
  patchEnableJob: (jobId: string) => `${appServiceBaseUri}/api/v1/jobDetails/${jobId}/enable`,
  patchReviewJob: (jobId: string) => `${appServiceBaseUri}/api/v1/jobDetails/${jobId}/review`,
  patchUpdateJob: (jobId: string) => `${appServiceBaseUri}/api/v1/jobDetails/${jobId}/update`,
  patchScheduleNowJob: (jobId: string) => `${appServiceBaseUri}/api/v1/jobDetails/${jobId}/scheduleNow`,
  getScheduleNowUsage: `${appServiceBaseUri}/api/v1/jobDetails/scheduleNow/usage`,
  postJob: `${appServiceBaseUri}/api/v1/jobs`,
  downloadJobs: `${appServiceBaseUri}/api/v1/jobs/bulkDownload`,
  destinations: `${appServiceBaseUri}/api/v1/destinations`,
  searchDestinations: `${appServiceBaseUri}/api/v1/destinations/searchGroups`,
  createGroup: `${appServiceBaseUri}/api/v1/destinations/groups`,
  searchChannels: (teamId: string) => `${appServiceBaseUri}/api/v1/destinations/teams/${teamId}/searchChannels`,
  getGroupEndpoints: (groupId: string) => `${appServiceBaseUri}/api/v1/destinations/groups/${groupId}/endpoints`,
  getGroupOwners: (groupId: string) => `${appServiceBaseUri}/api/v1/destinations/groups/${groupId}/owners`,
  getGroupMembers: (groupId: string) => `${appServiceBaseUri}/api/v1/destinations/groups/${groupId}/group-members`,
  getGroupOnboardingStatus: (groupId: string) => `${appServiceBaseUri}/api/v1/destinations/groups/${groupId}/onboarding-status`,
  getChannelOnboardingStatus: (teamId: string, channelId: string) => `${appServiceBaseUri}/api/v1/destinations/teams/${teamId}/channel/${channelId}/onboarding-status`,
  getGroupDetails: (groupId: string) => `${appServiceBaseUri}/api/v1/jobDetails/group/${groupId}`,
  getChannelDetails: (teamId: string, channelId: string) => `${appServiceBaseUri}/api/v1/jobDetails/groups/${teamId}/channels/${channelId}`,
  removeGMM: (syncJobId: string) =>`${appServiceBaseUri}/api/v1/jobDetails/${syncJobId}/removeGmm`,
  resolveNotification: (notificationId: string) => `${appServiceBaseUri}/api/v1/notifications/${encodeURIComponent(notificationId)}/resolve`,
};
