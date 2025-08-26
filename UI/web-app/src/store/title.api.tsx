// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { config } from '../authConfig';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';
import { HRPart } from '../models/HRPart';

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

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }

      const responseText = await response.text();
      return responseText;
    }
    catch (error: any) {
      if (currentRetries > 0) {
        console.warn(`Error: ${error.message}. Retrying in ${currentBackoff} ms...`);
        await sleep(currentBackoff);
        return makeRequest(currentRetries - 1, currentBackoff * 2);
      } else {
        throw error;
      }
    }
  };
  return makeRequest(retries, backoff);
});

export const generateTitles = createAsyncThunk<
  HRPart[],
  HRPart[],
  ThunkConfig
>('titles/generate', async (parts, { extra }): Promise<HRPart[]> => {
  const { authenticationService } = extra.services;

  const token = await authenticationService.getTokenAsync(TokenType.GMM);
  const headers = new Headers();
  headers.append('Authorization', `Bearer ${token}`);
  headers.append('Content-Type', 'application/json');

  const options = {
    method: 'POST',
    headers,
    body: JSON.stringify(parts),
  };

  try {
    const startTime = Date.now();
    const response = await fetch(config.generateTitles, options);
    const duration = Date.now() - startTime;

    if (duration > 1000) {
      console.log("MORE TIME");
    }

    if (!response.ok) {
      console.warn(`HTTP ${response.status}: ${response.statusText}. Using filters as titles.`);
      return parts.map(part => ({ ...part, title: part.filter }));
    }

    const generatedParts: HRPart[] = await response.json();
    return generatedParts;
  }
  catch (error: any) {
    console.warn(`Failed to generate titles: ${error.message}. Using filters as titles.`);
    return parts.map(part => ({ ...part, title: part.filter }));
  }
});