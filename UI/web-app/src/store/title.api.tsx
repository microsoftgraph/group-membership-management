// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { config } from '../authConfig';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';
import { HRPart } from '../models/HRPart';
import { HRSourcePartSource, ISourcePart } from '../models';
import { fetchOrgLeaderDetailsUsingId } from './orgLeaderDetails.api';
import { generateGroupTitle, generateHRTitle } from '../utils/titleGenerator';
import { GetOrgLeaderDetailsResponse } from '../models/GetOrgLeaderDetailsResponse';
import { searchGroups } from './groups.api';
import { IPersonaProps } from '@fluentui/react';

const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));
const backoff: number = 3000;
const retries: number = 3;

function extractColumnNames(filter: string) {
  const regex = /\b([a-zA-Z0-9_]+)\s*(?:=|<>|>|<|>=|<=|\bIN\b|\bNOT\s+IN\b)\s*/gi;
  let match;
  const columns = new Set();
  while ((match = regex.exec(filter)) !== null) {
    columns.add(match[1]);
  }
  return Array.from(columns);
}

function createFallbackTitles(parts: HRPart[]): HRPart[] {
  return parts.map(part => {
    const uniqueFilterKeys = extractColumnNames(part.filter).join(', ');
    return { ...part, title: uniqueFilterKeys ? `Users matching ${uniqueFilterKeys}` : part.filter };
  });
}

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
    const response = await fetch(config.generateTitles, options);

    if (!response.ok) {
      console.warn(`HTTP ${response.status}: ${response.statusText}. Using filters as titles.`);
      return createFallbackTitles(parts);
    }

    const generatedParts: HRPart[] = await response.json();
    return generatedParts;
  }
  catch (error: any) {
    console.warn(`Failed to generate titles: ${error.message}. Using filters as titles.`);
    return createFallbackTitles(parts);
  }
});

export const fetchOrgLeaderDetailsAndGenerateHRTitle = createAsyncThunk<
  ISourcePart,
  { part: ISourcePart; strings: any },
  ThunkConfig
>('title/fetchOrgLeaderDetailsAndGenerateHRTitle', async ({ part, strings }, { dispatch }): Promise<ISourcePart> => {
  const hrSource = part.query.source as HRSourcePartSource;

  try {
    const results = await dispatch(fetchOrgLeaderDetailsUsingId({
      employeeId: hrSource.manager?.id as number,
      partId: part.id as string
    }));

    const response = results.payload as GetOrgLeaderDetailsResponse;
    const orgLeaderName = response.text;

    // Use the utility function to generate the title
    const newTitle = generateHRTitle({
      orgLeaderName,
      depth: hrSource.manager?.depth
    }, {
      orgLeaderTitle: strings.HROnboarding.orgLeaderTitle,
      orgLeaderSingleLevelTitle: strings.HROnboarding.orgLeaderSingleLevelTitle,
      orgLeaderMultipleLevelsTitle: strings.HROnboarding.orgLeaderMultipleLevelsTitle
    });

    const updatedPart = { ...part, title: newTitle };
    return updatedPart;
  } catch (error) {
    console.error("Error fetching org leader details", error);
    return part;
  }
});

export const fetchGroupDetailsAndGenerateTitle = createAsyncThunk<
  ISourcePart,
  { part: ISourcePart; strings: any },
  ThunkConfig
>('title/fetchGroupDetailsAndGenerateTitle', async ({ part, strings }, { dispatch }): Promise<ISourcePart> => {
  const groupSource = part.query.source as string;

  try {
    const results = await dispatch(searchGroups(groupSource));
    const searchResults = results.payload as IPersonaProps[];

    let groupName: string | undefined;
    if (searchResults.length > 0) {
      groupName = searchResults[0].text;
    }

    // Use the utility function to generate the title
    const newTitle = generateGroupTitle(
      groupName,
      groupSource,
      {
        allUsersInGroup: strings.ManageMembership.labels.allUsersInGroup,
        allUsersInFallback: strings.ManageMembership.labels.allUsersInFallback
      }
    );

    const updatedPart = { ...part, title: newTitle };
    return updatedPart;
  } catch (error) {
    console.error("Error fetching group details", error);
    return part;
  }
});