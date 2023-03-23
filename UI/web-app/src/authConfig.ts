// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Configuration } from "@azure/msal-browser";

// Config object to be passed to Msal on creation
export const msalConfig: Configuration = {
    auth: {
        clientId: `${process.env.REACT_APP_AAD_UI_APP_CLIENT_ID}`,
        authority: `https://login.microsoftonline.com/${process.env.REACT_APP_AAD_APP_TENANT_ID}`,
        redirectUri: "/",
        postLogoutRedirectUri: "/"
    }
};

// scopes
export const loginRequest = {
    scopes: [`api://${process.env.REACT_APP_AAD_API_APP_CLIENT_ID}/user_impersonation`]
  };

// endpoints
export const config = {
    endpoint: `${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/api/v1/jobs`
};