// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { config } from '../authConfig';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';
import { Destination } from '../models';
import { IPersonaProps } from '@fluentui/react';

interface ValidateGroupResponse {
    groupId: string;
    isValid: boolean;
  }

export const searchGroups = createAsyncThunk<IPersonaProps[], string, ThunkConfig>(
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
            const response = await fetch(`${config.searchDestinations}/${encodeURIComponent(query)}`, options)
                .then(response => response.json());

            const payload: IPersonaProps[] = response.map((destination: Destination, index: number) => ({
                key: index.toString(),
                text: destination.name,
                secondaryText: destination.email,
                id: destination.id,
            }));

            return payload;
        } catch (error) {
            console.error('Failed to fetch destination data!', error);
            throw new Error('Failed to fetch destination data!');
        }
    }
);


export const validateGroup = createAsyncThunk<ValidateGroupResponse, string, ThunkConfig>(
    'destinations/searchDestinations',
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
            const response = await fetch(`${config.searchDestinations}/${encodeURIComponent(groupId)}`, options);
            if (!response.ok) {
                console.error('Group is not valid!', response.statusText);
                return { groupId, isValid: false };
            }
            return { groupId, isValid: true };
        } catch (error) {
            console.error('Failed to validate group!', error);
            throw new Error('Failed to validate group!');
        }
    }
);

