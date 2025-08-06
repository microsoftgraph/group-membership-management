// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { config } from '../authConfig';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';

const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));
const backoff: number = 3000;
const retries: number = 3;

export const getTitle = createAsyncThunk<
  string,
  string,
  ThunkConfig
>('title', async (filter, { extra }): Promise<string> => {
  const { authenticationService } = extra.services;

  const makeRequest = async (currentRetries: number, currentBackoff: number): Promise<string> => {
    const token = await authenticationService.getTokenAsync(TokenType.GMM);
    const headers = new Headers();
    headers.append('Authorization', `Bearer ${token}`);
    headers.append('Content-Type', 'application/json');

    const options = {
      method: 'POST',
      headers,
      body: JSON.stringify(filter),
    };

    try {
      const response = await fetch(config.getTitle, options);
      const responseText = await response.text();
      return responseText;
    }
    catch (error: any) {
      if (error.response && error.response.status === 429 && currentRetries > 0) {
        console.warn(`Rate limit exceeded. Retrying in ${currentBackoff} ms...`);
        await sleep(currentBackoff);
        return makeRequest(currentRetries - 1, currentBackoff * 2);
      } else {
        throw error;
      }
    }
  };
  return makeRequest(retries, backoff);
});

