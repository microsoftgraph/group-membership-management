// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { config } from '../authConfig';
import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';
import { Destination, PostGroupResponse } from '../models';
import { IPersonaProps } from '@fluentui/react';
import { PostGroupRequest } from '../models/PostGroupRequest';
import { isAxiosError } from 'axios';
import { ThunkConfigWithErrors } from './store';

export interface CreateGroupFailure {
    kind: 'rejected' | 'uncertain';
    statusCode?: number;
}

export const isConfirmedGroupRejection = (statusCode?: number): boolean =>
    statusCode !== undefined && [400, 401, 403, 404, 405, 409, 422].includes(statusCode);

interface ValidateGroupResponse {
    groupId: string;
    isValid: boolean;
  }

export const searchGroups = createAsyncThunk<IPersonaProps[], string, ThunkConfig>(
    'groups/searchGroups',
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
            const response = await fetch(`${config.searchDestinations}/${encodeURIComponent(query)}`, options);
            if (!response.ok) {
                console.error('Failed to search destinations!', response.statusText);
                return [];
            }
            const body = await response.json();

            const payload: IPersonaProps[] = body.map((destination: Destination, index: number) => ({
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
    'groups/validateGroup',
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

export const createGroup = createAsyncThunk<PostGroupResponse, PostGroupRequest, ThunkConfigWithErrors<CreateGroupFailure>>(
    '/groups',
    async (postGroupRequest: PostGroupRequest, { extra, rejectWithValue } ) => {
        const { gmmApi } = extra.apis;
        const { groupName, groupAlias, groupType, description } = postGroupRequest;
        const payload: PostGroupRequest = {
            groupName, groupAlias, groupType,
            ...(description !== undefined ? { description } : {}),
        };
        try {
            const response = await gmmApi.destinations.createGroup(payload);
            return response.data;
        } catch (error) {
            const statusCode = isAxiosError(error) ? error.response?.status : undefined;
            const kind = isConfirmedGroupRejection(statusCode) ? 'rejected' : 'uncertain';
            return rejectWithValue({ kind, statusCode });
        }
    },
    {
        condition: (_, { getState }) => {
            const state = getState().manageMembership;
            return !state.createGroupLoading && !state.createdGroupId && !state.createGroupUncertain;
        },
    }
);
