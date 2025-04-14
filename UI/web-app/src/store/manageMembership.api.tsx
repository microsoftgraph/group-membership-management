// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { config } from '../authConfig';
import { OnboardingStatus } from '../models/GroupOnboardingStatus';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';
import { Destination, DestinationPickerPersona } from '../models';
import { SearchChannelRequest } from '../models/SearchChannelRequest';
import { Search } from 'react-router-dom';
import { Channel } from '../models/Channel';
import { ChannelOnboardingStatusRequest } from '../models/ChannelOnboardingStatusRequest';

export class OdataQueryOptions {
  pageSize?: number;
  itemsToSkip?: number;
  filter?: String;
  orderBy?: String;
}

export const searchDestinations = createAsyncThunk<DestinationPickerPersona[], string, ThunkConfig>(
  'destinations/searchDestinations',
  async (query: string, { extra }) => {
    const { authenticationService } = extra.services;
    const token = await authenticationService.getTokenAsync(TokenType.GMM);
    const headers = new Headers();
    const bearer = `Bearer ${token}`;
    headers.append('Authorization', bearer);

    const options = {
      method: 'GET',
      headers,
    };

    try {
      const response = await fetch(`${config.searchDestinations}/${encodeURIComponent(query)}`, options).then(
        async (response) => await response.json()
      );

      const payload: DestinationPickerPersona[] = response.map((destination: Destination, index: number) => ({
        key: index, 
        text: destination.name,
        secondaryText: destination.email, 
        id: destination.id,
        endpoints: destination.endpoints,
      }));
      
      return payload;
    } catch (error) {
      throw new Error('Failed to fetch destination data!');
    }
  }
);

export const searchChannels = createAsyncThunk<DestinationPickerPersona[], SearchChannelRequest, ThunkConfig>(
  'destinations/searchChannels',
  async (searchChannelRequest: SearchChannelRequest, { extra }) => {
    const { authenticationService } = extra.services;
    const token = await authenticationService.getTokenAsync(TokenType.GMM);
    const headers = new Headers();
    const bearer = `Bearer ${token}`;
    headers.append('Authorization', bearer);

    const options = {
      method: 'GET',
      headers,
    };

    try {
      const response = await fetch(`${config.searchChannels(searchChannelRequest.teamId)}/${encodeURIComponent(searchChannelRequest.query)}`, options).then(
        async (response) => await response.json()
      );

      const payload: DestinationPickerPersona[] = response.map((channel: Channel, index: number) => ({
        key: index, 
        id: channel.channelId,
        text: channel.name,
      }));
      
      return payload;
    } catch (error) {
      throw new Error('Failed to fetch channel data!');
    }
  }
);

export const getGroupOnboardingStatus = createAsyncThunk<OnboardingStatus, string, ThunkConfig>(
  'groups/getGroupOnboardingStatus',
  async (groupId: string, { extra }) => {
    const { authenticationService } = extra.services;
    const token = await authenticationService.getTokenAsync(TokenType.GMM);
    const headers = new Headers();
    const bearer = `Bearer ${token}`;
    headers.append('Authorization', bearer);

    const requestOptions = {
      method: 'GET',
      headers: headers,
    };

    try {
      const response = await fetch(`${config.getGroupOnboardingStatus(groupId)}`, requestOptions);
      if (!response.ok) {
        throw new Error('Failed to fetch group onboarding status!');
      }
      const data: OnboardingStatus = await response.json();
      return data;
    } catch (error) {
      throw new Error('Failed to fetch group onboarding status!');
    }
  }
);

export const getChannelOnboardingStatus = createAsyncThunk<OnboardingStatus, ChannelOnboardingStatusRequest, ThunkConfig>(
  'groups/getChannelOnboardingStatus',
  async (channelOnboardingStatusRequest: ChannelOnboardingStatusRequest, { extra }) => {
    const { authenticationService } = extra.services;
    const token = await authenticationService.getTokenAsync(TokenType.GMM);
    const headers = new Headers();
    const bearer = `Bearer ${token}`;
    headers.append('Authorization', bearer);

    const requestOptions = {
      method: 'GET',
      headers: headers,
    };

    try {
      const response = await fetch(`${config.getChannelOnboardingStatus(channelOnboardingStatusRequest.teamId, channelOnboardingStatusRequest.channelId)}`, requestOptions);
      if (!response.ok) {
        throw new Error('Failed to fetch channel onboarding status!');
      }
      const data: OnboardingStatus = await response.json();
      return data;
    } catch (error) {
      throw new Error('Failed to fetch channel onboarding status!');
    }
  }
);

export const getGroupEndpoints = createAsyncThunk<string[], string, ThunkConfig>(
  'groupEndpoints',
  async (groupId: string, { extra }) => {
    const { authenticationService } = extra.services;
    const token = await authenticationService.getTokenAsync(TokenType.GMM);
    const headers = new Headers();
    const bearer = `Bearer ${token}`;
    headers.append('Authorization', bearer);

    const options = {
      method: 'GET',
      headers,
    };

    try {
      const response = await fetch(`${config.getGroupEndpoints(groupId)}`, options).then(
        async (response) => await response.json()
      );
      const payload: string[] = response;
      return payload;
    } catch (error) {
      throw new Error('Failed to fetch destination data!');
    }
  }
);
