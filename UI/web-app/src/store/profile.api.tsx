// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { graphRequest } from '../authConfig';
import { msalInstance } from '../index';

export const getProfile = createAsyncThunk('profile/getProfile', async () => {
  const account = msalInstance.getActiveAccount();
  if (!account) {
    throw Error(
      'No active account! Verify a user has been signed in and setActiveAccount has been called.'
    );
  }

  const authResult = await msalInstance.acquireTokenSilent({
    ...graphRequest,
    account: account,
  });

  const headers = new Headers();
  const bearer = `Bearer ${authResult.accessToken}`;
  headers.append('Authorization', bearer);
  headers.append('Scopes', 'User.ReadBasic.All');
  headers.append('Content-Type', 'application/json');

  const options = {
    method: 'GET',
    headers: headers,
  };

  try {
    let url = `https://graph.microsoft.com/v1.0/users/${account.localAccountId}?$select=preferredLanguage`;
    let response = await fetch(url, options).then((response) => response);
    const json = await response.json();
    const preferredLanguage = json.preferredLanguage;
    return preferredLanguage;
  } catch (error) {
    console.log(error);
  }
});

export const getProfilePhoto = createAsyncThunk(
  'profile/getProfilePhoto',
  async () => {
    const account = msalInstance.getActiveAccount();
    if (account == null) {
      throw Error(
        'No active account! Verify a user has been signed in and setActiveAccount has been called.'
      );
    }

    const authResult = await msalInstance.acquireTokenSilent({
      ...graphRequest,
      account,
    });

    const headers = new Headers();
    const bearer = `Bearer ${authResult.accessToken}`;
    headers.append('Authorization', bearer);

    const options = {
      method: 'GET',
      headers,
    };

    const base64ImageUrl: string = await fetch(
      `https://graph.microsoft.com/v1.0/me/photos/48x48/$value`,
      options
    ).then(async (response) => {
      console.dir(response);
      const blob = await response.blob();
      const reader = new FileReader();
      reader.readAsDataURL(blob);
      return new Promise((resolve) => {
        reader.onloadend = () => resolve(reader.result as string);
      });
    });
    console.log(base64ImageUrl);
    return base64ImageUrl;
  }
);
