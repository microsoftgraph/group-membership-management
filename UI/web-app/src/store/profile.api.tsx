// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';

export const getProfile = createAsyncThunk<string, void, ThunkConfig>(
  'profile/getProfile',
  async (_, { extra }) => {
    const { authenticationService } = extra;
    const token = await authenticationService.getTokenAsync(TokenType.Graph);
    const headers = new Headers();
    headers.append('Authorization', `Bearer ${token}`);
    headers.append('Scopes', 'User.ReadBasic.All');
    headers.append('Content-Type', 'application/json');

    const options = {
      method: 'GET',
      headers: headers,
    };

    const account = authenticationService.getActiveAccount();

    try {
      let url = `https://graph.microsoft.com/v1.0/users/${account?.id}?$select=preferredLanguage`;
      let response = await fetch(url, options).then((response) => response);
      const json = await response.json();
      const preferredLanguage = json.preferredLanguage;
      return preferredLanguage;
    } catch (error) {
      console.log(error);
    }
  }
);

export const getProfilePhoto = createAsyncThunk<string, void, ThunkConfig>(
  'profile/getProfilePhoto',
  async (_, {extra}) => {
    const { authenticationService } = extra;
    const token = await authenticationService.getTokenAsync(TokenType.Graph);
    const headers = new Headers();
    headers.append('Authorization', `Bearer ${token}`);

    const options = {
      method: 'GET',
      headers,
    };

    const base64ImageUrl: string = await fetch(
      `https://graph.microsoft.com/v1.0/me/photos/48x48/$value`,
      options
    ).then(async (response) => {
      const blob = await response.blob();
      const reader = new FileReader();
      reader.readAsDataURL(blob);
      return new Promise((resolve) => {
        reader.onloadend = () => resolve(reader.result as string);
      });
    });
    return base64ImageUrl;
  }
);
