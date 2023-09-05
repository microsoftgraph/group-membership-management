// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { loginRequest, config } from '../authConfig';
import { msalInstance } from '../index';
import { type Settings } from '../models/Settings';


export interface SettingsResponse {
    settings: Settings[];
}

export const fetchSettings = createAsyncThunk('settings/fetchSettings', async () => {
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
            config.getSettings, options
        ).then(async (response) => await response.json());

        const payload: Settings = response;
        return payload;
    } catch (error) {
        throw new Error('Failed to fetch settings data!');
    }
});
