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
  getJobs: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobs`,
  getJobDetails: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobDetails`,
  getSettings: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/settings`
};
