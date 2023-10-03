// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { loginRequest, config } from '../authConfig';
import { msalInstance } from '../index';
import { type Group } from '../models/Group';

export const searchGroups = createAsyncThunk('groups/searchGroups', async (query: string) => {
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
            config.getGroups +
            `/search/${encodeURIComponent(
                query
            )}`, options
        ).then(async (response) => await response.json());

        const payload: Group[] = response;
        return payload;
    } catch (error) {
        throw new Error('Failed to fetch group data!');
    }
});


export const isAppIDOwnerOfGroup = createAsyncThunk(
    'isAppIDOwnerOfGroup',
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
                `${config.getGroups}/isAppIDOwnerOfGroup?groupId=${groupId}`, options
            ).then(async (response) => await response.json());
            const payload: boolean = response;
            return payload;
        } catch (error) {
            throw new Error('Failed to fetch group data!');
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
                `${config.getGroups}/groupEndpoints?groupId=${groupId}`, options
            ).then(async (response) => await response.json());
            const payload: string[] = response;
            return payload;
        } catch (error) {
            throw new Error('Failed to fetch group data!');
        }
    }
);