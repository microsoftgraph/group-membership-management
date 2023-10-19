
import { createAsyncThunk } from '@reduxjs/toolkit';
import { loginRequest, config } from '../authConfig';
import { msalInstance } from '../index';
import { OnboardingStatus } from '../models/GroupOnboardingStatus';
import { Destination } from '../models/Destination';

export class OdataQueryOptions {
  pageSize?: number;
  itemsToSkip?: number;
  filter?: String;
  orderBy?: String;
}

export const searchDestinations = createAsyncThunk('destinations/searchDestinations', async (query: string) => {
    const account = msalInstance.getActiveAccount();
    if (account == null) {
        throw Error(
            'No active account! Verify a user has been signed in and setActiveAccount has been called.'
        );
    }

    const authResult = await msalInstance.acquireTokenSilent({
        ...loginRequest,
        account,
    });

    const headers = new Headers();
    const bearer = `Bearer ${authResult.accessToken}`;
    headers.append('Authorization', bearer);

    const options = {
        method: 'GET',
        headers,
    };

    try {
        const response = await fetch(
            `${config.searchDestinations}/${encodeURIComponent(query)}`,
            options
        ).then(async (response) => await response.json());

        const payload: Destination[] = response;
        return payload;
    } catch (error) {
        throw new Error('Failed to fetch destination data!');
    }
});

export const getGroupOnboardingStatus = createAsyncThunk(
    'groups/getGroupOnboardingStatus',
    async (groupId: string) => {
        const account = msalInstance.getActiveAccount();
        if (!account) {
            throw new Error(
                'No active account! Verify a user has been signed in and setActiveAccount has been called.'
            );
        }

        const authResult = await msalInstance.acquireTokenSilent({
            ...loginRequest,
            account: account,
        });

        const headers = new Headers();
        const bearer = `Bearer ${authResult.accessToken}`;
        headers.append('Authorization', bearer);

        const requestOptions = {
            method: 'GET',
            headers: headers,
        };

        try {
            const response = await fetch(
                `${config.getGroupOnboardingStatus(groupId)}`, 
                requestOptions
            );
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

export const getGroupEndpoints = createAsyncThunk(
    'groupEndpoints',
    async (groupId: string) => {
        const account = msalInstance.getActiveAccount();
        if (account == null) {
            throw Error(
                'No active account! Verify a user has been signed in and setActiveAccount has been called.'
            );
        }

        const authResult = await msalInstance.acquireTokenSilent({
            ...loginRequest,
            account,
        });

        const headers = new Headers();
        const bearer = `Bearer ${authResult.accessToken}`;
        headers.append('Authorization', bearer);

        const options = {
            method: 'GET',
            headers,
        };

        try {
            const response = await fetch(
                `${config.getGroupEndpoints(groupId)}`,
                options
            ).then(async (response) => await response.json());
            const payload: string[] = response;
            return payload;
        } catch (error) {
            throw new Error('Failed to fetch destination data!');
        }
    }
);

