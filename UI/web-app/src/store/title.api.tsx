// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { config } from '../authConfig';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';

export const getTitle = createAsyncThunk<
  string,
  string,
  ThunkConfig
>('title', async (filter, { extra }) => {
  const { authenticationService } = extra.services;
  const token = await authenticationService.getTokenAsync(TokenType.GMM);
  const headers = new Headers();
  headers.append('Authorization', `Bearer ${token}`);
  headers.append('Content-Type', 'application/json');

  const options = {
    method: 'POST',
    headers,
    body: JSON.stringify(filter),
  };

  try
  {
    const response = await fetch(config.getTitle, options);
    const responseText = await response.text();
    return responseText;
  }
  catch (error) {
    throw new Error('Failed to generate title!');
  }
});