// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { v4 as uuidv4 } from 'uuid';
import { config } from '../authConfig';
import { ThunkConfig, RootState } from './store';
import { TokenType } from '../services/auth';
import { IChatMessage } from '../components/CopilotPanel/CopilotPanel.types';
import { ISourcePart } from '../models/ISourcePart';
import { HRSourcePart } from '../models/HRSourcePart';
import { GroupMembershipSourcePart } from '../models/GroupMembershipSourcePart';
import { SourcePartType } from '../models/SourcePartType';

export interface CopilotResponse {
    message: IChatMessage;
    sourceParts: ISourcePart[]; // Array of source parts, each with its own org leader info
    useOrgStructure: boolean; // Whether any part uses org hierarchy
}

// Backend API response format (supports multiple source parts with per-part org leader info)
interface CopilotApiResponse {
    message: string;
    sourceParts?: ApiSourcePart[]; // Array of source parts from backend
}

// Backend source part format (now includes org leader info per-part and group membership support)
interface ApiSourcePart {
    partId: string;
    sourceType?: string; // "SqlMembership" (default) or "GroupMembership"
    filter: string | null; // Can be null for org-only queries or group membership
    title: string;
    isExclusion: boolean;
    useOrgStructure: boolean;
    orgLeaderName?: string;
    orgLeaderEmail?: string;
    orgLeaderObjectId?: string;
    orgLeaderDepth?: number;
    groupId?: string; // For GroupMembership: the Entra ID group's objectId
    groupName?: string; // For GroupMembership: the group's display name
}

function transformSourcePart(apiPart: ApiSourcePart): ISourcePart {
    const isGroupMembership = apiPart.sourceType === 'GroupMembership' && !!apiPart.groupId;

    if (isGroupMembership) {
        const groupQuery: GroupMembershipSourcePart = {
            type: SourcePartType.GroupMembership,
            source: apiPart.groupId!,
            exclusionary: apiPart.isExclusion,
        };

        return {
            id: apiPart.partId || uuidv4(),
            title: apiPart.title || apiPart.groupName || 'Group Source',
            query: groupQuery,
            isNew: true,
            isExpanded: false,
        };
    }

    // Default: HR / SqlMembership source part
    const hrQuery: HRSourcePart = {
        type: SourcePartType.HR,
        source: {
            filter: apiPart.filter || undefined, // Convert null/empty to undefined
        },
        exclusionary: apiPart.isExclusion,
    };

    return {
        id: apiPart.partId || uuidv4(),
        title: apiPart.title,
        query: hrQuery,
        isNew: true,
        isExpanded: false,
        // Per-part org leader info
        useOrgStructure: apiPart.useOrgStructure || false,
        managerToAutoSelect: apiPart.orgLeaderName ? {
            objectId: apiPart.orgLeaderObjectId || undefined,
            displayName: apiPart.orgLeaderName,
            email: apiPart.orgLeaderEmail || undefined,
        } : undefined,
        depthToAutoSelect: apiPart.orgLeaderDepth ?? undefined,
    };
}

export interface UserContext {
    department?: string;
    companyName?: string;
    jobTitle?: string;
    managerName?: string;
    managerEmail?: string;
    managerAlias?: string;
}

export interface SendMessagePayload {
    message: string;
    userContext?: UserContext;
    hrAttributes?: { name: string; hasMapping: boolean; customLabel?: string; description?: string }[];
    currentFilter?: string;
}

export const sendCopilotMessage = createAsyncThunk<CopilotResponse, SendMessagePayload, ThunkConfig>(
    'copilot/sendMessage',
    async ({ message: userMessage, userContext, hrAttributes, currentFilter }, { extra, getState, rejectWithValue }) => {
        const { authenticationService } = extra.services;
        const token = await authenticationService.getTokenAsync(TokenType.GMM);
        const headers = new Headers();
        const bearer = `Bearer ${token}`;
        headers.append('Authorization', bearer);
        headers.append('Content-Type', 'application/json');

        // Get conversation history from state
        const state = getState() as RootState;
        const conversationHistory = state.copilot.messages.map(msg => ({
            role: msg.role,
            content: msg.content,
        }));

        const requestBody = {
            message: userMessage,
            conversationHistory,
            userContext,
            hrAttributes,
            currentFilter,
            conversationId: state.copilot.conversationId,
        };

        const options = {
            method: 'POST',
            headers,
            body: JSON.stringify(requestBody),
        };

        try {
            const response = await fetch(config.copilotChat, options);
            const responseText = await response.text();

            if (!response.ok) {
                return rejectWithValue({
                    message: 'Failed to send message to Copilot',
                    status: response.status,
                    statusText: response.statusText,
                    body: responseText,
                });
            }

            let data: CopilotApiResponse;
            try {
                data = JSON.parse(responseText) as CopilotApiResponse;
            } catch (parseError) {
                return rejectWithValue({
                    message: 'Failed to parse Copilot response',
                    body: responseText,
                    error: parseError instanceof Error ? parseError.message : String(parseError),
                });
            }

            const assistantMessage: IChatMessage = {
                id: uuidv4(),
                role: 'assistant',
                content: data.message,
                timestamp: new Date().toISOString(),
            };

            // Transform source parts array (each part carries its own org leader info)
            const sourceParts = (data.sourceParts ?? []).map(transformSourcePart);

            return {
                message: assistantMessage,
                sourceParts,
                useOrgStructure: sourceParts.some(p => p.useOrgStructure),
            };
        } catch (error) {
            return rejectWithValue({
                message: 'Failed to communicate with GMM Copilot. Please try again.',
                error: error instanceof Error ? error.message : String(error),
            });
        }
    }
);
