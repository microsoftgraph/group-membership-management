import { createAsyncThunk } from '@reduxjs/toolkit';
import { config } from '../authConfig';
import { OnboardingStatus } from '../models/GroupOnboardingStatus';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';
import { Destination, DestinationPickerPersona } from '../models';

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
