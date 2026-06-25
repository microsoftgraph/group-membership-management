// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';

import { config } from '../authConfig';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';
import { SpotCheckResult } from '../models/SpotCheckResult';

export const spotCheckUser = createAsyncThunk<
  SpotCheckResult,
  { syncJobId: string; userId: string },
  ThunkConfig
>('spotCheck/spotCheckUser', async ({ syncJobId, userId }, { extra }) => {
  const { authenticationService } = extra.services;
  const token = await authenticationService.getTokenAsync(TokenType.GMM);
  const headers = new Headers({
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
  });

  const response = await fetch(config.spotCheckUser(syncJobId, userId), {
    method: 'GET',
    headers,
  });

  if (response.status === 404) {
    throw new Error('notFound');
  }

  if (!response.ok) {
    throw new Error('Failed to spot-check user membership.');
  }

  return (await response.json()) as SpotCheckResult;
});
