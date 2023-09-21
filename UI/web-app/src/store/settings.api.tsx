// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { loginRequest, config } from '../authConfig';
import { msalInstance } from '../index';
import { type Setting } from '../models/Settings';

export const fetchSettingByKey = createAsyncThunk('settings/fetchSettingByKey', async (key: string) => {
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
            config.settings+
            `?key=${encodeURIComponent(
                key
            )}`, options
        ).then(async (response) => await response.json());

        const payload: Setting = response;
        return payload;
    } catch (error) {
        throw new Error('Failed to fetch setting data!');
    }
});

export const postSetting = createAsyncThunk('settings/postSetting', async (data: Setting) => {
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
    headers.append('Content-Type', 'application/json');

    const options = {
        method: 'POST',
        headers,
        body: JSON.stringify(data), // Convert data to JSON and send it in the request body
    };

    try {
        const response = await fetch(config.settings, options); // Use the appropriate API endpoint for posting data

        if (!response.ok) {
            throw new Error('Failed to post setting data!');
        }

        return data; // Return the posted data on success
    } catch (error) {
        throw new Error('Failed to post setting data!');
    }
});
