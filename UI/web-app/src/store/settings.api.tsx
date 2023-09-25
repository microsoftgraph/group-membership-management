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

export const updateSetting = createAsyncThunk('settings/updateSetting', async (data: Setting) => {
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
        body: JSON.stringify(data.value),
    };

    try {
        const response = await fetch(
            config.settings+
            `/${encodeURIComponent(
                data.key
            )}`, options
        ).then(async (response) => await response.json());

        const payload: Setting = response;

        if (!response.ok) {
            throw new Error('Failed to update setting data!');
        }

        return payload;
    } catch (error) {
        throw new Error('Failed to update setting data!');
    }
});
