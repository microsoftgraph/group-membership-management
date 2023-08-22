// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type Configuration, type PopupRequest } from '@azure/msal-browser';

// Config object to be passed to Msal on creation
export const msalConfig: Configuration = {
  auth: {
    clientId: '43bbe0bf-c699-41b1-9a2c-29bb31169aef',
    authority: 'https://login.microsoftonline.com/organizations',
    redirectUri: '/',
    postLogoutRedirectUri: '/',
  },
};

// scopes
export const loginRequest = {
  scopes: [
    'api://11316d26-c6fb-404a-8701-7638112bc9af/user_impersonation',
  ],
};

export const graphRequest: PopupRequest = {
  scopes: ['User.Read'],
};

// endpoints
export const config = {
  getJobs: 'https://gmm-compute-ar-webapi.azurewebsites.net/api/v1/jobs',
  getJobDetails: 'https://gmm-compute-ar-webapi.azurewebsites.net/api/v1/jobDetails',
};
